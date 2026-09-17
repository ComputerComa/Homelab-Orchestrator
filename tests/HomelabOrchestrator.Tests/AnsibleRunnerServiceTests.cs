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
        var service = new AnsibleRunnerService(new FakeCatalog([]), proxmox, new InMemoryAnsibleRunJobStore(), new AnsibleRunJobQueue(), new FakeAnsibleExecutionStore());

        var targets = await service.GetRunTargetOptionsAsync();

        Assert.Equal(["cache-01", "web-01"], targets.RunningContainers.Select(c => c.Hostname));
        Assert.Equal(["base", "mqtt"], targets.Tags);
    }

    [Fact]
    public async Task GetRunTargetOptionsAsync_excludes_the_orchestrators_own_container_and_its_only_tag()
    {
        var proxmox = new FakeProxmoxService([
            new ContainerSummary(100, Environment.MachineName, "running", ["orchestrator-only-tag"]),
            new ContainerSummary(141, "web-01", "running", ["base"]),
        ]);
        var service = new AnsibleRunnerService(new FakeCatalog([]), proxmox, new InMemoryAnsibleRunJobStore(), new AnsibleRunJobQueue(), new FakeAnsibleExecutionStore());

        var targets = await service.GetRunTargetOptionsAsync();

        Assert.Equal(["web-01"], targets.RunningContainers.Select(c => c.Hostname));
        Assert.Equal(["base"], targets.Tags);
    }

    [Fact]
    public async Task SubmitAsync_enqueues_the_job_persists_it_and_GetJobOrHistoryAsync_returns_it()
    {
        var store = new InMemoryAnsibleRunJobStore();
        var queue = new AnsibleRunJobQueue();
        var executions = new FakeAnsibleExecutionStore();
        var service = new AnsibleRunnerService(new FakeCatalog([]), new FakeProxmoxService([]), store, queue, executions);

        var jobId = await service.SubmitAsync(new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        var job = await service.GetJobOrHistoryAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal("ssh-check", job.Request.PlaybookName);
        Assert.Equal(AnsibleRunStage.Queued, job.Stage);
        Assert.Single(executions.Saved);
    }

    [Fact]
    public async Task GetJobOrHistoryAsync_prefers_the_in_memory_job_over_persisted_history()
    {
        var store = new InMemoryAnsibleRunJobStore();
        var executions = new FakeAnsibleExecutionStore();
        var service = new AnsibleRunnerService(new FakeCatalog([]), new FakeProxmoxService([]), store, new AnsibleRunJobQueue(), executions);
        var job = store.Create(new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));
        await executions.SaveAsync(job with { Stage = AnsibleRunStage.Failed, Error = "stale" });

        var result = await service.GetJobOrHistoryAsync(job.Id);

        Assert.NotNull(result);
        Assert.Equal(AnsibleRunStage.Queued, result.Stage);
    }

    [Fact]
    public async Task GetJobOrHistoryAsync_falls_back_to_persisted_history_when_not_in_memory()
    {
        var executions = new FakeAnsibleExecutionStore();
        var jobId = Guid.NewGuid();
        await executions.SaveAsync(new AnsibleRunJob
        {
            Id = jobId,
            Request = new AnsibleRunRequest("apply-base", AnsibleRunTargetKind.All, null),
            Stage = AnsibleRunStage.Succeeded,
            ExitCode = 0,
        });
        var service = new AnsibleRunnerService(new FakeCatalog([]), new FakeProxmoxService([]), new InMemoryAnsibleRunJobStore(), new AnsibleRunJobQueue(), executions);

        var job = await service.GetJobOrHistoryAsync(jobId);

        Assert.NotNull(job);
        Assert.Equal("apply-base", job.Request.PlaybookName);
        Assert.Equal(AnsibleRunStage.Succeeded, job.Stage);
    }

    [Fact]
    public async Task GetJobOrHistoryAsync_returns_null_when_not_found_anywhere()
    {
        var service = new AnsibleRunnerService(new FakeCatalog([]), new FakeProxmoxService([]), new InMemoryAnsibleRunJobStore(), new AnsibleRunJobQueue(), new FakeAnsibleExecutionStore());

        Assert.Null(await service.GetJobOrHistoryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListRecentExecutionsAsync_delegates_to_the_execution_store()
    {
        var executions = new FakeAnsibleExecutionStore();
        await executions.SaveAsync(new AnsibleRunJob { Id = Guid.NewGuid(), Request = new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null) });
        var service = new AnsibleRunnerService(new FakeCatalog([]), new FakeProxmoxService([]), new InMemoryAnsibleRunJobStore(), new AnsibleRunJobQueue(), executions);

        var recent = await service.ListRecentExecutionsAsync(10);

        Assert.Single(recent);
    }

    [Fact]
    public async Task ListPlaybooksAsync_delegates_to_the_catalog()
    {
        var playbooks = new PlaybookSummary[] { new("apply-base", "apply-base.yml") };
        var service = new AnsibleRunnerService(new FakeCatalog(playbooks), new FakeProxmoxService([]), new InMemoryAnsibleRunJobStore(), new AnsibleRunJobQueue(), new FakeAnsibleExecutionStore());

        Assert.Equal(playbooks, await service.ListPlaybooksAsync());
    }

    [Fact]
    public async Task ListPlaybookDetailsAsync_fetches_every_catalog_playbooks_detail()
    {
        var playbooks = new PlaybookSummary[] { new("apply-base", "apply-base.yml"), new("ssh-check", "ssh-check.yml") };
        var service = new AnsibleRunnerService(new FakeCatalog(playbooks), new FakeProxmoxService([]), new InMemoryAnsibleRunJobStore(), new AnsibleRunJobQueue(), new FakeAnsibleExecutionStore());

        var details = await service.ListPlaybookDetailsAsync();

        Assert.Equal(["apply-base", "ssh-check"], details.Select(d => d.Name));
    }

    private sealed class FakeCatalog(IReadOnlyList<PlaybookSummary> playbooks) : IPlaybookCatalog
    {
        public Task<IReadOnlyList<PlaybookSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(playbooks);

        public Task<string?> ResolvePathAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(playbooks.Any(p => p.Name == name) ? $"/fake/{name}.yml" : null);

        public Task<PlaybookDetail?> GetDetailAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(playbooks.Any(p => p.Name == name) ? new PlaybookDetail(name, name, []) : null);
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
