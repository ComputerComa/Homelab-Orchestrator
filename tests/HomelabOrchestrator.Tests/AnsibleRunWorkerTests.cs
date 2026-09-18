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
        var (worker, store, queue, process, _, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Equal(AnsibleRunStage.Succeeded, job.Stage);
        Assert.Equal(0, job.ExitCode);
        Assert.Null(process.LastLimit);
    }

    [Fact]
    public async Task Vm_target_resolves_the_hostname_as_the_limit()
    {
        var (worker, store, queue, process, _, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Vm, "web-01"));

        Assert.Equal(AnsibleRunStage.Succeeded, job.Stage);
        Assert.Equal("web-01", process.LastLimit);
    }

    [Fact]
    public async Task Vm_target_fails_without_running_the_process_when_the_container_is_not_currently_running()
    {
        var (worker, store, queue, process, _, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Vm, "db-01"));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Contains("not currently running", job.Error);
        Assert.False(process.WasInvoked);
    }

    [Fact]
    public async Task Vm_target_fails_without_running_the_process_when_the_container_is_the_orchestrators_own()
    {
        var containers = new List<ContainerSummary>(RunningContainers)
        {
            new(150, Environment.MachineName, "running", []),
        };
        var (worker, store, queue, process, _, _) = Build(containers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Vm, Environment.MachineName));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Contains("not currently running", job.Error);
        Assert.False(process.WasInvoked);
    }

    [Fact]
    public async Task TagGroup_target_resolves_to_the_sanitized_group_name()
    {
        var (worker, store, queue, process, _, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.TagGroup, "mqtt"));

        Assert.Equal(AnsibleRunStage.Succeeded, job.Stage);
        Assert.Equal("tag_mqtt", process.LastLimit);
    }

    [Fact]
    public async Task TagGroup_target_fails_without_running_the_process_when_no_running_container_has_the_tag()
    {
        var (worker, store, queue, process, _, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.TagGroup, "nope"));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Contains("no running container currently has the tag", job.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(process.WasInvoked);
    }

    [Fact]
    public async Task Selection_target_resolves_to_a_comma_joined_limit()
    {
        var (worker, store, queue, process, _, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Selection, "web-01"));

        Assert.Equal(AnsibleRunStage.Succeeded, job.Stage);
        Assert.Equal("web-01", process.LastLimit);
    }

    [Fact]
    public async Task Selection_target_fails_without_running_the_process_when_one_host_is_not_currently_running()
    {
        var (worker, store, queue, process, _, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Selection, "web-01,db-01"));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Contains("db-01", job.Error);
        Assert.Contains("not currently running", job.Error);
        Assert.False(process.WasInvoked);
    }

    [Fact]
    public async Task Selection_target_fails_without_running_the_process_when_empty()
    {
        var (worker, store, queue, process, _, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Selection, null));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Contains("No containers were selected", job.Error);
        Assert.False(process.WasInvoked);
    }

    [Fact]
    public async Task Unknown_playbook_fails_without_running_the_process()
    {
        var (worker, store, queue, process, executions, _) = Build(RunningContainers, exitCode: 0, catalogHasPlaybook: false);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("does-not-exist", AnsibleRunTargetKind.All, null));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Contains("no longer available", job.Error);
        Assert.False(process.WasInvoked);
        Assert.Equal([AnsibleRunStage.Running, AnsibleRunStage.Failed], executions.Saved.Select(j => j.Stage));
    }

    [Fact]
    public async Task Nonzero_exit_code_fails_the_job_and_records_the_exit_code()
    {
        var (worker, store, queue, _, executions, _) = Build(RunningContainers, exitCode: 2);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
        Assert.Equal(2, job.ExitCode);
        Assert.Contains("exited with code 2", job.Error);
        Assert.Equal([AnsibleRunStage.Running, AnsibleRunStage.Failed], executions.Saved.Select(j => j.Stage));
    }

    [Fact]
    public async Task Output_lines_land_in_the_durable_log_store_in_order_with_correct_stream_tags()
    {
        var (worker, store, queue, _, _, logs) = Build(RunningContainers, exitCode: 0, outputLines: ["PLAY [x]", "ok: [web-01]"]);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Equal(["PLAY [x]", "ok: [web-01]"], logs.Appended.Select(e => e.Text));
        Assert.All(logs.Appended, e => Assert.Equal(AnsibleLogStream.Stdout, e.Stream));
        Assert.Equal([1, 2], logs.Appended.Select(e => e.Sequence));
        Assert.All(logs.Appended, e => Assert.Equal(job.Id, e.ExecutionId));
    }

    [Fact]
    public async Task Successful_run_persists_a_running_snapshot_then_a_succeeded_terminal_snapshot()
    {
        var (worker, store, queue, _, executions, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Equal([AnsibleRunStage.Running, AnsibleRunStage.Succeeded], executions.Saved.Select(j => j.Stage));
        Assert.All(executions.Saved, saved => Assert.Equal(job.Id, saved.Id));
    }

    [Fact]
    public async Task StartedAtUtc_and_ResolvedTargetHostnames_populate_on_the_running_transition()
    {
        var (worker, store, queue, _, _, _) = Build(RunningContainers, exitCode: 0);

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Vm, "web-01"));

        Assert.NotNull(job.StartedAtUtc);
        Assert.Equal(["web-01"], job.ResolvedTargetHostnames);
    }

    [Fact]
    public async Task A_persistence_failure_on_the_terminal_save_after_a_successful_run_leaves_the_job_Succeeded_not_Failed()
    {
        var (worker, store, queue, _, executions, _) = Build(RunningContainers, exitCode: 0);
        executions.ThrowOnSaveNumber = 2; // 1 = Running transition, 2 = the terminal save

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Equal(AnsibleRunStage.Succeeded, job.Stage);
    }

    [Fact]
    public async Task A_persistence_failure_on_the_terminal_save_after_a_failed_run_still_leaves_the_job_Failed()
    {
        var (worker, store, queue, _, executions, _) = Build(RunningContainers, exitCode: 3);
        executions.ThrowOnSaveNumber = 2;

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Equal(AnsibleRunStage.Failed, job.Stage);
    }

    [Fact]
    public async Task A_persistence_failure_does_not_stop_the_worker_from_processing_the_next_job()
    {
        var (worker, store, queue, _, executions, _) = Build(RunningContainers, exitCode: 0);
        executions.ThrowOnSaveNumber = 2;

        // Deliberately not using RunAsync here — it stops the worker once the first job finishes,
        // which is exactly what this test needs to NOT happen: the same still-running worker loop
        // must pick up a second job enqueued right after the first one's terminal save threw.
        var firstJob = store.Create(new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));
        await queue.EnqueueAsync(firstJob.Id);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForCompletionAsync(store, firstJob.Id);

            var secondJob = store.Create(new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));
            await queue.EnqueueAsync(secondJob.Id);
            var finished = await WaitForCompletionAsync(store, secondJob.Id);

            Assert.Equal(AnsibleRunStage.Succeeded, finished.Stage);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_process_runner_timeout_is_recorded_as_TimedOut()
    {
        var (worker, store, queue, _, _, _) = Build(
            RunningContainers, exitCode: 0, throwException: new TimeoutException("ansible-playbook did not finish within 30s."));

        var job = await RunAsync(worker, store, queue, new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));

        Assert.Equal(AnsibleRunStage.TimedOut, job.Stage);
        Assert.Contains("did not finish within", job.Error);
    }

    [Fact]
    public async Task Stopping_the_worker_mid_run_marks_the_job_Interrupted()
    {
        var (worker, store, queue, process, _, _) = Build(RunningContainers, exitCode: 0, hangUntilCancelled: true);

        var job = store.Create(new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null));
        await queue.EnqueueAsync(job.Id);
        await worker.StartAsync(CancellationToken.None);

        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var finished = store.Get(job.Id)!;
        Assert.Equal(AnsibleRunStage.Interrupted, finished.Stage);
        Assert.Contains("shutting down", finished.Error);
    }

    private static (AnsibleRunWorker Worker, InMemoryAnsibleRunJobStore Store, AnsibleRunJobQueue Queue, FakeProcessRunner Process, FakeAnsibleExecutionStore Executions, FakeAnsibleExecutionLogStore Logs) Build(
        IReadOnlyList<ContainerSummary> containers,
        int exitCode,
        bool catalogHasPlaybook = true,
        IReadOnlyList<string>? outputLines = null,
        Exception? throwException = null,
        bool hangUntilCancelled = false)
    {
        var store = new InMemoryAnsibleRunJobStore();
        var queue = new AnsibleRunJobQueue();
        var catalog = new FakeCatalog(catalogHasPlaybook);
        var proxmox = new FakeProxmoxService(containers);
        var process = new FakeProcessRunner(exitCode, outputLines ?? [], throwException, hangUntilCancelled);
        var executions = new FakeAnsibleExecutionStore();
        var logs = new FakeAnsibleExecutionLogStore();

        var worker = new AnsibleRunWorker(queue, store, catalog, proxmox, process, executions, logs, NullLogger<AnsibleRunWorker>.Instance);
        return (worker, store, queue, process, executions, logs);
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

        public Task<PlaybookDetail?> GetDetailAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

    private sealed class FakeProcessRunner(int exitCode, IReadOnlyList<string> outputLines, Exception? throwException = null, bool hangUntilCancelled = false) : IAnsibleProcessRunner
    {
        public bool WasInvoked { get; private set; }
        public string? LastLimit { get; private set; }

        /// <summary>Signaled once <see cref="RunPlaybookAsync"/> starts — lets a test know it's safe to cancel while <paramref name="hangUntilCancelled"/> keeps it running.</summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<int> RunPlaybookAsync(
            string playbookPath, string? limit, Action<AnsibleLogStream, string> onOutputLine, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            LastLimit = limit;
            Started.TrySetResult();

            if (throwException is not null)
            {
                throw throwException;
            }

            if (hangUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            foreach (var line in outputLines)
            {
                onOutputLine(AnsibleLogStream.Stdout, line);
                await Task.Yield();
            }

            return exitCode;
        }
    }
}
