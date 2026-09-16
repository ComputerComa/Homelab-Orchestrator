using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Provisioning;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Tests;

public class ContainerReconciliationServiceTests
{
    private static readonly ProxmoxOptions Options = new()
    {
        NetworkPrefix = "10.0.150",
        IpHostMin = 2,
        IpHostMax = 254,
    };

    [Fact]
    public async Task ListCandidatesAsync_excludes_containers_already_tagged_managed_by_orchestrator()
    {
        var proxmox = new FakeProxmoxService(
            [new ContainerSummary(141, "web-01", "running", ["managed-by-orchestrator"])],
            addresses: new Dictionary<int, string?> { [141] = "10.0.150.141" });
        var service = Build(proxmox);

        Assert.Empty(await service.ListCandidatesAsync());
    }

    [Fact]
    public async Task ListCandidatesAsync_excludes_a_vmid_that_cannot_map_to_the_convention()
    {
        var proxmox = new FakeProxmoxService(
            [new ContainerSummary(1, "too-low", "running", [])],
            addresses: new Dictionary<int, string?> { [1] = "10.0.150.1" });
        var service = Build(proxmox);

        Assert.Empty(await service.ListCandidatesAsync());
    }

    [Fact]
    public async Task ListCandidatesAsync_marks_a_matching_address_eligible()
    {
        var proxmox = new FakeProxmoxService(
            [new ContainerSummary(141, "legacy-01", "running", ["base"])],
            addresses: new Dictionary<int, string?> { [141] = "10.0.150.141" });
        var service = Build(proxmox);

        var candidates = await service.ListCandidatesAsync();

        var candidate = Assert.Single(candidates);
        Assert.Equal("10.0.150.141", candidate.ExpectedAddress);
        Assert.Equal("10.0.150.141", candidate.ActualAddress);
        Assert.True(candidate.IsEligible);
    }

    [Fact]
    public async Task ListCandidatesAsync_marks_a_mismatched_or_missing_address_ineligible()
    {
        var proxmox = new FakeProxmoxService(
            [
                new ContainerSummary(141, "wrong-ip", "running", []),
                new ContainerSummary(142, "dhcp", "running", []),
            ],
            addresses: new Dictionary<int, string?> { [141] = "10.0.99.5", [142] = null });
        var service = Build(proxmox);

        var candidates = await service.ListCandidatesAsync();

        Assert.All(candidates, c => Assert.False(c.IsEligible));
    }

    [Fact]
    public async Task AdoptAsync_tags_only_containers_whose_freshly_read_address_still_matches()
    {
        var proxmox = new FakeProxmoxService(
            [
                new ContainerSummary(141, "matches", "running", []),
                new ContainerSummary(142, "no-longer-matches", "running", []),
            ],
            addresses: new Dictionary<int, string?> { [141] = "10.0.150.141", [142] = "10.0.99.5" });
        var service = Build(proxmox);

        var result = await service.AdoptAsync([141, 142]);

        Assert.Equal([141], result.Adopted);
        Assert.Equal([142], result.Skipped);
        Assert.Equal(["managed-by-orchestrator"], proxmox.TaggedVmids[141]);
        Assert.False(proxmox.TaggedVmids.ContainsKey(142));
    }

    [Fact]
    public async Task AdoptAsync_skips_a_vmid_that_cannot_map_to_the_convention_without_calling_proxmox()
    {
        var proxmox = new FakeProxmoxService([], addresses: new Dictionary<int, string?>());
        var service = Build(proxmox);

        var result = await service.AdoptAsync([1]);

        Assert.Equal([1], result.Skipped);
        Assert.Empty(result.Adopted);
        Assert.DoesNotContain(1, proxmox.AddressLookedUp);
    }

    [Fact]
    public async Task AdoptAsync_re_reads_the_address_fresh_rather_than_trusting_a_prior_listing()
    {
        // Simulates the address changing between when the candidate list was shown and when the
        // operator confirms — the worker must re-check, not trust the browser's earlier snapshot.
        var proxmox = new FakeProxmoxService(
            [new ContainerSummary(141, "changed", "running", [])],
            addresses: new Dictionary<int, string?> { [141] = "10.0.150.141" });
        await proxmox.ListContainersAsync();
        proxmox.Addresses[141] = "10.0.99.9";

        var service = Build(proxmox);
        var result = await service.AdoptAsync([141]);

        Assert.Equal([141], result.Skipped);
        Assert.Empty(result.Adopted);
    }

    private static ContainerReconciliationService Build(FakeProxmoxService proxmox) =>
        new(proxmox, Microsoft.Extensions.Options.Options.Create(Options));

    private sealed class FakeProxmoxService(IReadOnlyList<ContainerSummary> containers, Dictionary<int, string?> addresses) : IProxmoxService
    {
        public Dictionary<int, string?> Addresses { get; } = addresses;
        public List<int> AddressLookedUp { get; } = [];
        public Dictionary<int, List<string>> TaggedVmids { get; } = [];

        public Task<int> GetNextVmIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> FindLatestDebianTemplateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CreatedContainer> CreateContainerAsync(ContainerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(containers);

        public Task<string?> GetContainerAddressAsync(int vmid, CancellationToken cancellationToken = default)
        {
            AddressLookedUp.Add(vmid);
            return Task.FromResult(Addresses.GetValueOrDefault(vmid));
        }

        public Task AddTagAsync(int vmid, string tag, CancellationToken cancellationToken = default)
        {
            if (!TaggedVmids.TryGetValue(vmid, out var tags))
            {
                tags = [];
                TaggedVmids[vmid] = tags;
            }

            tags.Add(tag);
            return Task.CompletedTask;
        }
    }
}
