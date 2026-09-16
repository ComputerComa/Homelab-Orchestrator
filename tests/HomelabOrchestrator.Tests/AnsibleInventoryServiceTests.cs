using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomelabOrchestrator.Tests;

public class AnsibleInventoryServiceTests
{
    private static AnsibleInventoryService BuildService(IReadOnlyList<ContainerSummary> containers, SshOptions? sshOptions = null)
    {
        var proxmoxOptions = Microsoft.Extensions.Options.Options.Create(new ProxmoxOptions
        {
            NetworkPrefix = "10.0.150",
            IpHostMin = 2,
            IpHostMax = 254,
        });
        var ssh = Microsoft.Extensions.Options.Options.Create(sshOptions ?? new SshOptions());

        return new AnsibleInventoryService(
            new FakeProxmoxService(containers),
            proxmoxOptions,
            ssh,
            NullLogger<AnsibleInventoryService>.Instance);
    }

    [Fact]
    public async Task GetContainerInventoryAsync_returns_an_inventory_with_just_that_host()
    {
        var service = BuildService([
            new ContainerSummary(141, "web-01", "running", []),
            new ContainerSummary(142, "db-01", "stopped", []),
        ]);

        var inventory = await service.GetContainerInventoryAsync(141);

        Assert.NotNull(inventory);
        Assert.Equal(["web-01"], inventory.All.Hosts);
        Assert.Equal("10.0.150.141", inventory.Meta.Hostvars["web-01"].AnsibleHost);
    }

    [Fact]
    public async Task GetContainerInventoryAsync_returns_null_for_an_unknown_vmid()
    {
        var service = BuildService([new ContainerSummary(141, "web-01", "running", [])]);

        Assert.Null(await service.GetContainerInventoryAsync(999));
    }

    [Fact]
    public async Task GetContainerInventoryAsync_includes_connection_details_from_ssh_options()
    {
        var service = BuildService(
            [new ContainerSummary(141, "web-01", "running", [])],
            new SshOptions { RemoteUser = "root", Port = 2222, OrchestratorPrivateKeyPath = "/root/.ssh/id_ed25519" });

        var inventory = await service.GetContainerInventoryAsync(141);

        var vars = inventory!.Meta.Hostvars["web-01"];
        Assert.Equal("root", vars.AnsibleUser);
        Assert.Equal(2222, vars.AnsiblePort);
        Assert.Equal("/root/.ssh/id_ed25519", vars.AnsibleSshPrivateKeyFile);
    }

    [Fact]
    public async Task GetContainerInventoryAsync_includes_tags_when_the_container_has_several()
    {
        var service = BuildService([
            new ContainerSummary(141, "web-01", "running", ["base", "managed-by-orchestrator", "mqtt"]),
        ]);

        var inventory = await service.GetContainerInventoryAsync(141);

        Assert.Equal(["base", "managed-by-orchestrator", "mqtt"], inventory!.Meta.Hostvars["web-01"].Tags);
    }

    [Fact]
    public async Task GetContainerInventoryAsync_produces_an_empty_tags_array_when_the_container_has_none()
    {
        var service = BuildService([new ContainerSummary(141, "web-01", "running", [])]);

        var inventory = await service.GetContainerInventoryAsync(141);

        var tags = inventory!.Meta.Hostvars["web-01"].Tags;
        Assert.NotNull(tags);
        Assert.Empty(tags);
    }

    [Fact]
    public async Task GetContainerInventoryAsync_throws_when_the_vmid_cannot_be_mapped_to_an_address()
    {
        var service = BuildService([new ContainerSummary(1, "out-of-range", "running", [])]);

        await Assert.ThrowsAsync<ProxmoxOperationException>(() => service.GetContainerInventoryAsync(1));
    }

    [Fact]
    public async Task GetRunningContainersInventoryAsync_excludes_stopped_containers()
    {
        var service = BuildService([
            new ContainerSummary(141, "web-01", "running", []),
            new ContainerSummary(142, "db-01", "stopped", []),
            new ContainerSummary(143, "cache-01", "running", []),
        ]);

        var inventory = await service.GetRunningContainersInventoryAsync();

        Assert.Equal(["web-01", "cache-01"], inventory.All.Hosts);
        Assert.DoesNotContain("db-01", inventory.Meta.Hostvars.Keys);
    }

    [Fact]
    public async Task GetRunningContainersInventoryAsync_returns_an_empty_inventory_when_nothing_is_running()
    {
        var service = BuildService([new ContainerSummary(141, "web-01", "stopped", [])]);

        var inventory = await service.GetRunningContainersInventoryAsync();

        Assert.Empty(inventory.All.Hosts);
        Assert.Empty(inventory.Meta.Hostvars);
    }

    [Fact]
    public async Task GetRunningContainersInventoryAsync_skips_unmappable_containers_instead_of_failing_the_whole_list()
    {
        var service = BuildService([
            new ContainerSummary(1, "out-of-range", "running", []),
            new ContainerSummary(141, "web-01", "running", []),
        ]);

        var inventory = await service.GetRunningContainersInventoryAsync();

        Assert.Equal(["web-01"], inventory.All.Hosts);
    }

    private sealed class FakeProxmoxService(IReadOnlyList<ContainerSummary> containers) : IProxmoxService
    {
        public Task<int> GetNextVmIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> FindLatestDebianTemplateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CreatedContainer> CreateContainerAsync(ContainerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(containers);
    }
}
