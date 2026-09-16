using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Proxmox;

namespace HomelabOrchestrator.Tests;

public class AnsibleRunnerServiceTests
{
    [Fact]
    public async Task GetRunTargetOptionsAsync_only_lists_running_hostnames_and_their_distinct_tags()
    {
        var proxmox = new FakeProxmoxService([
            new ContainerSummary(141, "web-01", "running", ["base", "mqtt"]),
            new ContainerSummary(142, "db-01", "stopped", ["base"]),
            new ContainerSummary(143, "cache-01", "running", ["base"]),
        ]);
        var service = new AnsibleRunnerService(new FakeCatalog([]), proxmox, new InMemoryAnsibleRunJobStore(), new AnsibleRunJobQueue());

        var targets = await service.GetRunTargetOptionsAsync();

        Assert.Equal(["cache-01", "web-01"], targets.RunningHostnames);
        Assert.Equal(["base", "mqtt"], targets.Tags);
    }

    [Fact]
    public async Task SubmitAsync_enqueues_the_job_and_GetJob_returns_it()
    {
        var store = new InMemoryAnsibleRunJobStore();
        var queue = new AnsibleRunJobQueue();
        var service = new AnsibleRunnerService(new FakeCatalog([]), new FakeProxmoxService([]), store, queue);

        var jobId = await service.SubmitAsync(new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        var job = service.GetJob(jobId);
        Assert.NotNull(job);
        Assert.Equal("ssh-check", job.Request.PlaybookName);
        Assert.Equal(AnsibleRunStage.Queued, job.Stage);
    }

    [Fact]
    public async Task ListPlaybooksAsync_delegates_to_the_catalog()
    {
        var playbooks = new PlaybookSummary[] { new("apply-base", "apply-base.yml") };
        var service = new AnsibleRunnerService(new FakeCatalog(playbooks), new FakeProxmoxService([]), new InMemoryAnsibleRunJobStore(), new AnsibleRunJobQueue());

        Assert.Equal(playbooks, await service.ListPlaybooksAsync());
    }

    private sealed class FakeCatalog(IReadOnlyList<PlaybookSummary> playbooks) : IPlaybookCatalog
    {
        public Task<IReadOnlyList<PlaybookSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(playbooks);

        public Task<string?> ResolvePathAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(playbooks.Any(p => p.Name == name) ? $"/fake/{name}.yml" : null);
    }

    private sealed class FakeProxmoxService(IReadOnlyList<ContainerSummary> containers) : IProxmoxService
    {
        public Task<int> GetNextVmIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> FindLatestDebianTemplateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CreatedContainer> CreateContainerAsync(ContainerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(containers);

        public Task<string?> GetContainerAddressAsync(int vmid, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddTagAsync(int vmid, string tag, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
