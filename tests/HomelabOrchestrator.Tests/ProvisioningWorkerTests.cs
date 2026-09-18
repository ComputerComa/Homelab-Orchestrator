using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Proxmox;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomelabOrchestrator.Tests;

/// <summary>Exercises the real BackgroundService lifecycle (StartAsync/StopAsync) against fakes — no live Proxmox server.</summary>
public class ProvisioningWorkerTests
{
    [Fact]
    public async Task Worker_fetches_combined_keys_fresh_and_passes_them_to_the_proxmox_request()
    {
        var (worker, queue, store, proxmox, sshKeys, _, _, _) = Build();

        var request = new ProvisioningRequest("test-host", Cores: 2, MemoryMB: 2048, SwapMB: 512, DiskGB: 8, Start: true, StartAtBoot: true);
        var job = store.Create(request);
        await QueueAndRunAsync(worker, queue, store, job.Id);

        var finished = store.Get(job.Id)!;
        Assert.Equal(ProvisioningStage.Succeeded, finished.Stage);
        Assert.Equal(1, sshKeys.CallCount);
        Assert.NotNull(proxmox.LastRequest);
        Assert.Equal("ssh-ed25519 AAAAtest orchestrator", proxmox.LastRequest!.SshPublicKeys);
        Assert.Equal("test-host", proxmox.LastRequest.Hostname);
        Assert.Equal(100, proxmox.LastRequest.Vmid);
        Assert.Equal("10.0.150.100", proxmox.LastRequest.IpAddress);
    }

    [Fact]
    public async Task Reachable_container_gets_base_applied_and_keys_synced_before_succeeding()
    {
        var (worker, queue, store, _, _, reachability, runner, sshKeyManagement) = Build();

        var request = new ProvisioningRequest("test-host", Cores: 2, MemoryMB: 2048, SwapMB: 512, DiskGB: 8, Start: true, StartAtBoot: true);
        var job = store.Create(request);
        await QueueAndRunAsync(worker, queue, store, job.Id);

        var finished = store.Get(job.Id)!;
        Assert.Equal(ProvisioningStage.Succeeded, finished.Stage);
        Assert.Equal(1, reachability.CallCount);

        var baseRequest = runner.SubmittedRequests[0];
        Assert.Equal("apply-base", baseRequest.PlaybookName);
        Assert.Equal(AnsibleRunTargetKind.Vm, baseRequest.TargetKind);
        Assert.Equal("test-host", baseRequest.TargetValue);

        Assert.Equal(1, sshKeyManagement.SyncCallCount);
        Assert.Equal("test-host", sshKeyManagement.LastTargetHostname);
    }

    [Fact]
    public async Task A_container_not_started_skips_reachability_and_ansible_entirely()
    {
        var (worker, queue, store, _, _, reachability, runner, sshKeyManagement) = Build();

        var request = new ProvisioningRequest("test-host", Cores: 2, MemoryMB: 2048, SwapMB: 512, DiskGB: 8, Start: false, StartAtBoot: false);
        var job = store.Create(request);
        await QueueAndRunAsync(worker, queue, store, job.Id);

        var finished = store.Get(job.Id)!;
        Assert.Equal(ProvisioningStage.Succeeded, finished.Stage);
        Assert.Equal(0, reachability.CallCount);
        Assert.Empty(runner.SubmittedRequests);
        Assert.Equal(0, sshKeyManagement.SyncCallCount);
    }

    [Fact]
    public async Task Timing_out_while_waiting_for_ssh_fails_the_job_without_ever_calling_ansible()
    {
        var (worker, queue, store, proxmox, _, reachability, runner, sshKeyManagement) = Build();
        reachability.Reachable = false;

        var request = new ProvisioningRequest("test-host", Cores: 2, MemoryMB: 2048, SwapMB: 512, DiskGB: 8, Start: true, StartAtBoot: true);
        var job = store.Create(request);
        await QueueAndRunAsync(worker, queue, store, job.Id);

        var finished = store.Get(job.Id)!;
        Assert.Equal(ProvisioningStage.Failed, finished.Stage);
        Assert.Contains("did not accept SSH connections", finished.Error);
        Assert.Empty(runner.SubmittedRequests);
        Assert.Equal(0, sshKeyManagement.SyncCallCount);

        // The container itself was still created — nothing about a post-creation failure rolls it back.
        Assert.NotNull(proxmox.LastRequest);
    }

    [Fact]
    public async Task A_failed_apply_base_run_fails_the_job_and_never_attempts_the_key_sync()
    {
        var (worker, queue, store, _, _, _, runner, sshKeyManagement) = Build();
        runner.FailingPlaybooks.Add("apply-base");

        var request = new ProvisioningRequest("test-host", Cores: 2, MemoryMB: 2048, SwapMB: 512, DiskGB: 8, Start: true, StartAtBoot: true);
        var job = store.Create(request);
        await QueueAndRunAsync(worker, queue, store, job.Id);

        var finished = store.Get(job.Id)!;
        Assert.Equal(ProvisioningStage.Failed, finished.Stage);
        Assert.Contains("apply-base", finished.Error);
        Assert.Contains("Runner page", finished.Error);
        Assert.Equal(0, sshKeyManagement.SyncCallCount);
    }

    [Fact]
    public async Task A_failed_key_sync_fails_the_job_after_apply_base_already_succeeded()
    {
        var (worker, queue, store, _, _, _, runner, sshKeyManagement) = Build();
        sshKeyManagement.ThrowLockoutError = true;

        var request = new ProvisioningRequest("test-host", Cores: 2, MemoryMB: 2048, SwapMB: 512, DiskGB: 8, Start: true, StartAtBoot: true);
        var job = store.Create(request);
        await QueueAndRunAsync(worker, queue, store, job.Id);

        var finished = store.Get(job.Id)!;
        Assert.Equal(ProvisioningStage.Failed, finished.Stage);
        Assert.Contains("SSH Keys page", finished.Error);
        Assert.Single(runner.SubmittedRequests);
        Assert.Equal("apply-base", runner.SubmittedRequests[0].PlaybookName);
    }

    private static (
        ProvisioningWorker Worker,
        IProvisioningJobQueue Queue,
        InMemoryProvisioningJobStore Store,
        CapturingProxmoxService Proxmox,
        FixedSshPublicKeyProvider SshKeys,
        FakeSshReachabilityChecker Reachability,
        FakeAnsibleRunnerService Runner,
        FakeSshKeyManagementService SshKeyManagement) Build()
    {
        var queue = new ProvisioningJobQueue();
        var store = new InMemoryProvisioningJobStore();
        var proxmox = new CapturingProxmoxService();
        var sshKeys = new FixedSshPublicKeyProvider("ssh-ed25519 AAAAtest orchestrator");
        var reachability = new FakeSshReachabilityChecker();
        var runner = new FakeAnsibleRunnerService();
        var sshKeyManagement = new FakeSshKeyManagementService(runner);
        var options = Microsoft.Extensions.Options.Options.Create(new ProxmoxOptions
        {
            NetworkPrefix = "10.0.150",
            IpHostMin = 2,
            IpHostMax = 254,
        });
        var sshOptions = Microsoft.Extensions.Options.Options.Create(new SshOptions());
        var provisioningOptions = Microsoft.Extensions.Options.Options.Create(new ProvisioningOptions());

        var worker = new ProvisioningWorker(
            queue,
            store,
            proxmox,
            sshKeys,
            reachability,
            runner,
            sshKeyManagement,
            options,
            sshOptions,
            provisioningOptions,
            NullLogger<ProvisioningWorker>.Instance);

        return (worker, queue, store, proxmox, sshKeys, reachability, runner, sshKeyManagement);
    }

    private static async Task QueueAndRunAsync(ProvisioningWorker worker, IProvisioningJobQueue queue, IProvisioningJobStore store, Guid jobId)
    {
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.EnqueueAsync(jobId);
            await WaitForCompletionAsync(store, jobId);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<ProvisioningJob> WaitForCompletionAsync(IProvisioningJobStore store, Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var job = store.Get(jobId);
            if (job is { IsFinished: true })
            {
                return job;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Provisioning job did not finish in time.");
    }

    private sealed class CapturingProxmoxService : IProxmoxService
    {
        public ContainerRequest? LastRequest { get; private set; }

        public Task<int> GetNextVmIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(100);

        public Task<string> FindLatestDebianTemplateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("local:vztmpl/debian-13-standard_13.0-1_amd64.tar.zst");

        public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContainerSummary>>([]);

        public Task<CreatedContainer> CreateContainerAsync(ContainerRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new CreatedContainer(request.Vmid, request.Hostname, request.IpAddress, request.Template, TimeSpan.Zero));
        }

        public Task<string?> GetContainerAddressAsync(int vmid, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddTagAsync(int vmid, string tag, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedSshPublicKeyProvider(string keys) : ISshPublicKeyProvider
    {
        public int CallCount { get; private set; }

        public Task<string> GetCombinedPublicKeysAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(keys);
        }

        public Task<string?> GetOrchestratorPublicKeyAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSshReachabilityChecker : ISshReachabilityChecker
    {
        public bool Reachable { get; set; } = true;
        public int CallCount { get; private set; }

        public Task<bool> WaitUntilReachableAsync(string ipAddress, int port, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Reachable);
        }
    }

    /// <summary>Every submitted run "finishes" immediately — Succeeded unless its playbook name is in <see cref="FailingPlaybooks"/>.</summary>
    private sealed class FakeAnsibleRunnerService : IAnsibleRunnerService
    {
        public List<AnsibleRunRequest> SubmittedRequests { get; } = [];
        public HashSet<string> FailingPlaybooks { get; } = [];
        private readonly Dictionary<Guid, AnsibleRunJob> _jobs = [];

        public Task<Guid> SubmitAsync(AnsibleRunRequest request, string? submittedBy = null, CancellationToken cancellationToken = default)
        {
            SubmittedRequests.Add(request);
            var succeeded = !FailingPlaybooks.Contains(request.PlaybookName);
            var id = Guid.NewGuid();
            _jobs[id] = new AnsibleRunJob
            {
                Id = id,
                Request = request,
                Stage = succeeded ? AnsibleRunStage.Succeeded : AnsibleRunStage.Failed,
                ExitCode = succeeded ? 0 : 1,
                Error = succeeded ? null : $"{request.PlaybookName} failed (simulated)",
            };
            return Task.FromResult(id);
        }

        public Task<AnsibleRunJob?> GetJobOrHistoryAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_jobs.GetValueOrDefault(jobId));

        public Task<IReadOnlyList<PlaybookSummary>> ListPlaybooksAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlaybookDetail>> ListPlaybookDetailsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RunTargetOptions> GetRunTargetOptionsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AnsibleExecutionRecord>> ListRecentExecutionsAsync(int limit = 100, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GetReconstructedOutputAsync(Guid executionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AnsibleExecutionLog>> GetLogTailAsync(Guid executionId, long afterSequence, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSshKeyManagementService(FakeAnsibleRunnerService runner) : ISshKeyManagementService
    {
        public int SyncCallCount { get; private set; }
        public string? LastTargetHostname { get; private set; }
        public bool ThrowLockoutError { get; set; }

        public Task<Guid> SyncAsync(string? submittedBy, string? targetHostname = null, CancellationToken cancellationToken = default)
        {
            SyncCallCount++;
            LastTargetHostname = targetHostname;
            if (ThrowLockoutError)
            {
                throw new InvalidOperationException("The orchestrator's own public key could not be read; refusing to synchronize.");
            }

            return runner.SubmitAsync(new AnsibleRunRequest("sync-ssh-keys", AnsibleRunTargetKind.Vm, targetHostname), submittedBy, cancellationToken);
        }

        public Task<IReadOnlyList<SshPublicKey>> ListKeysAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EnrollmentResult> EnrollAsync(string tokenPlaintext, string? deviceName, string? publicKeyLine, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> CreateEnrollmentTokenAsync(string? createdBy, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ApproveAsync(Guid keyId, string? approvedBy, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RevokeAsync(Guid keyId, string? revokedBy, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeletePendingAsync(Guid keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
