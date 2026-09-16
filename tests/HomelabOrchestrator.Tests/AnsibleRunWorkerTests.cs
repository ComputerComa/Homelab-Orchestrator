using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomelabOrchestrator.Tests;

/// <summary>Exercises the real BackgroundService lifecycle (StartAsync/StopAsync) against fakes — no real ansible-playbook process.</summary>
public class AnsibleRunWorkerTests
{
    private static readonly IReadOnlyList<ContainerSummary> RunningContainers =
    [
        new ContainerSummary(141, "web-01", "running", ["base", "mqtt"]),
        new ContainerSummary(142, "db-01", "stopped", ["base"]),
    ];

    [Fact]
    public async Task All_target_runs_with_no_limit_and_succeeds()
    {
        var (worker, store, queue, process) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Equal(AnsibleRunStage.Succeeded, job.Stage);
        Assert.Equal(0, job.ExitCode);
        Assert.Null(process.LastLimit);
    }

    [Fact]
    public async Task Vm_target_resolves_the_hostname_as_the_limit()
    {
        var (worker, store, queue, process) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Vm, "web-01"));

        Assert.Equal(AnsibleRunStage.Succeeded, job.Stage);
        Assert.Equal("web-01", process.LastLimit);
    }

    [Fact]
    public async Task Vm_target_fails_without_running_the_process_when_the_container_is_not_currently_running()
    {
        var (worker, store, queue, process) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Vm, "db-01"));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Contains("not currently running", job.Error);
        Assert.False(process.WasInvoked);
    }

    [Fact]
    public async Task TagGroup_target_resolves_to_the_sanitized_group_name()
    {
        var (worker, store, queue, process) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.TagGroup, "mqtt"));

        Assert.Equal(AnsibleRunStage.Succeeded, job.Stage);
        Assert.Equal("tag_mqtt", process.LastLimit);
    }

    [Fact]
    public async Task TagGroup_target_fails_without_running_the_process_when_no_running_container_has_the_tag()
    {
        var (worker, store, queue, process) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.TagGroup, "nope"));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Contains("no running container currently has the tag", job.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(process.WasInvoked);
    }

    [Fact]
    public async Task Unknown_playbook_fails_without_running_the_process()
    {
        var (worker, store, queue, process) = Build(RunningContainers, exitCode: 0, catalogHasPlaybook: false);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("does-not-exist", AnsibleRunTargetKind.All, null));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Contains("no longer available", job.Error);
        Assert.False(process.WasInvoked);
    }

    [Fact]
    public async Task Nonzero_exit_code_fails_the_job_and_records_the_exit_code()
    {
        var (worker, store, queue, _) = Build(RunningContainers, exitCode: 2);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Equal(2, job.ExitCode);
        Assert.Contains("exited with code 2", job.Error);
    }

    [Fact]
    public async Task Output_lines_accumulate_on_the_job_as_they_are_produced()
    {
        var (worker, store, queue, _) = Build(RunningContainers, exitCode: 0, outputLines: ["PLAY [x]", "ok: [web-01]"]);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Contains("PLAY [x]", job.Output);
        Assert.Contains("ok: [web-01]", job.Output);
    }

    private static (AnsibleRunWorker Worker, InMemoryAnsibleRunJobStore Store, AnsibleRunJobQueue Queue, FakeProcessRunner Process) Build(
        IReadOnlyList<ContainerSummary> containers, int exitCode, bool catalogHasPlaybook = true, IReadOnlyList<string>? outputLines = null)
    {
        var store = new InMemoryAnsibleRunJobStore();
        var queue = new AnsibleRunJobQueue();
        var catalog = new FakeCatalog(catalogHasPlaybook);
        var proxmox = new FakeProxmoxService(containers);
        var process = new FakeProcessRunner(exitCode, outputLines ?? []);

        var worker = new AnsibleRunWorker(queue, store, catalog, proxmox, process, NullLogger<AnsibleRunWorker>.Instance);
        return (worker, store, queue, process);
    }

    private static async Task<AnsibleRunJob> RunAsync(
        AnsibleRunWorker worker, InMemoryAnsibleRunJobStore store, AnsibleRunJobQueue queue, AnsibleRunRequest request)
    {
        var job = store.Create(request);
        await queue.EnqueueAsync(job.Id);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            return await WaitForCompletionAsync(store, job.Id);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<AnsibleRunJob> WaitForCompletionAsync(IAnsibleRunJobStore store, Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var job = store.Get(jobId);
            if (job is { IsFinished: true })
            {
                return job;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Ansible run did not finish in time.");
    }

    private sealed class FakeCatalog(bool hasPlaybook) : IPlaybookCatalog
    {
        public Task<IReadOnlyList<PlaybookSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaybookSummary>>(hasPlaybook ? [new PlaybookSummary("ssh-check", "ssh-check.yml")] : []);

        public Task<string?> ResolvePathAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(hasPlaybook && name == "ssh-check" ? "/fake/ansible/playbooks/ssh-check.yml" : null);
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

    private sealed class FakeProcessRunner(int exitCode, IReadOnlyList<string> outputLines) : IAnsibleProcessRunner
    {
        public bool WasInvoked { get; private set; }
        public string? LastLimit { get; private set; }

        public async Task<int> RunPlaybookAsync(
            string playbookPath, string? limit, Action<string> onOutputLine, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            LastLimit = limit;

            foreach (var line in outputLines)
            {
                onOutputLine(line);
                await Task.Yield();
            }

            return exitCode;
        }
    }
}
