using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Provisioning;
using HomelabOrchestrator.Services.Proxmox;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// The single background worker that actually talks to Proxmox for provisioning, then — for a
/// container started at creation — waits for it to accept SSH connections and runs the standard
/// post-creation Ansible configuration (the base role, then an SSH key sync scoped to just this
/// one container) before declaring the job <see cref="ProvisioningStage.Succeeded"/>. Reading the
/// queue with one sequential <c>await foreach</c> loop is what serializes allocation: a job
/// only calls <see cref="IProxmoxService.GetNextVmIdAsync"/> after the previous job's creation
/// has fully finished, so two jobs can never be handed the same VMID or calculated address.
/// VMID/address/template are recalculated here, immediately before creation — a browser
/// preview shown earlier is never trusted or reused. SSH keys are likewise fetched fresh from
/// <see cref="ISshPublicKeyProvider"/> right before creation, never carried on the job itself.
/// A failure while waiting for SSH or running the post-creation playbooks never deletes the LXC —
/// it lands the job in <see cref="ProvisioningStage.Failed"/> with a message pointing at the
/// ordinary Runner/SSH Keys page action that retries just that step.
/// </summary>
public class ProvisioningWorker(
    IProvisioningJobQueue queue,
    IProvisioningJobStore store,
    IProxmoxService proxmox,
    ISshPublicKeyProvider sshPublicKeys,
    ISshReachabilityChecker sshReachability,
    IAnsibleRunnerService runnerService,
    ISshKeyManagementService sshKeyManagement,
    IOptions<ProxmoxOptions> options,
    IOptions<SshOptions> sshOptions,
    IOptions<ProvisioningOptions> provisioningOptions,
    ILogger<ProvisioningWorker> logger) : BackgroundService
{
    private const string SubmittedByProvisioning = "provisioning";
    private static readonly TimeSpan AnsibleJobPollInterval = TimeSpan.FromSeconds(1);

    private readonly ProxmoxOptions _options = options.Value;
    private readonly SshOptions _sshOptions = sshOptions.Value;
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in queue.DequeueAllAsync(stoppingToken))
        {
            await ProcessAsync(jobId, stoppingToken);
        }
    }

    private async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            store.Update(jobId, job => job with { Stage = ProvisioningStage.Allocating });

            var vmid = await proxmox.GetNextVmIdAsync(cancellationToken);
            if (!IpAddressCalculator.TryCalculate(vmid, _options.NetworkPrefix, _options.IpHostMin, _options.IpHostMax, out var ipAddress))
            {
                throw new ProxmoxOperationException(
                    $"VMID {vmid} cannot be mapped safely to {_options.NetworkPrefix}.x; " +
                    $"the last octet must be between {_options.IpHostMin} and {_options.IpHostMax}.");
            }

            var template = await proxmox.FindLatestDebianTemplateAsync(cancellationToken);

            var job = store.Update(jobId, job => job with
            {
                Stage = ProvisioningStage.CreatingContainer,
                Vmid = vmid,
                IpAddress = ipAddress,
                Template = template,
            });

            // Fetched fresh, immediately before creation — never carried on the job record.
            var combinedKeys = await sshPublicKeys.GetCombinedPublicKeysAsync(cancellationToken);

            var containerRequest = new ContainerRequest
            {
                Vmid = vmid,
                Hostname = job.Request.Hostname,
                IpAddress = ipAddress,
                Template = template,
                SshPublicKeys = combinedKeys,
                Cores = job.Request.Cores,
                MemoryMB = job.Request.MemoryMB,
                SwapMB = job.Request.SwapMB,
                DiskGB = job.Request.DiskGB,
                Start = job.Request.Start,
                StartAtBoot = job.Request.StartAtBoot,
            };

            store.Update(jobId, j => j with { Stage = ProvisioningStage.WaitingForProxmox });

            var created = await proxmox.CreateContainerAsync(containerRequest, cancellationToken);

            if (job.Request.Start)
            {
                await ConfigureNewContainerAsync(jobId, created.Hostname, created.IpAddress, cancellationToken);
            }

            store.Update(jobId, j => j with { Stage = ProvisioningStage.Succeeded, CompletedAtUtc = DateTimeOffset.UtcNow });
            logger.LogInformation(
                "Provisioning job {JobId} succeeded: {Hostname} (VMID {Vmid}) at {IpAddress}",
                jobId, created.Hostname, created.Vmid, created.IpAddress);
        }
        catch (Exception ex)
        {
            var sanitized = ex is ProxmoxOperationException or ProvisioningConfigurationException
                ? ex.Message
                : "An unexpected error occurred while provisioning this container. See the server log for details.";

            if (ex is ProxmoxOperationException or ProvisioningConfigurationException)
            {
                logger.LogWarning("Provisioning job {JobId} failed: {Message}", jobId, sanitized);
            }
            else
            {
                logger.LogError(ex, "Provisioning job {JobId} failed unexpectedly", jobId);
            }

            store.Update(jobId, job => job with
            {
                Stage = ProvisioningStage.Failed,
                Error = sanitized,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    /// <summary>
    /// Waits for the newly created container to accept SSH connections, then runs the base
    /// configuration role and synchronizes the current SSH key registry to it — scoped to just
    /// this one container, never the whole fleet. A failure at any point here never touches the
    /// LXC itself; it's surfaced as a <see cref="ProvisioningConfigurationException"/> naming the
    /// ordinary page action ("Runner" or "SSH Keys") that retries just that step.
    /// </summary>
    private async Task ConfigureNewContainerAsync(Guid jobId, string hostname, string ipAddress, CancellationToken cancellationToken)
    {
        store.Update(jobId, j => j with { Stage = ProvisioningStage.WaitingForSsh });

        var reachable = await sshReachability.WaitUntilReachableAsync(
            ipAddress,
            _sshOptions.Port,
            TimeSpan.FromSeconds(_provisioningOptions.SshReadyTimeoutSeconds),
            TimeSpan.FromSeconds(_provisioningOptions.SshPollIntervalSeconds),
            cancellationToken);

        if (!reachable)
        {
            throw new ProvisioningConfigurationException(
                $"The container was created but did not accept SSH connections within " +
                $"{_provisioningOptions.SshReadyTimeoutSeconds} seconds. It has not been deleted — " +
                "check it manually, then use the Runner page to finish configuring it.");
        }

        store.Update(jobId, j => j with { Stage = ProvisioningStage.ApplyingBase });

        var baseRequest = new AnsibleRunRequest("apply-base", AnsibleRunTargetKind.Vm, hostname);
        await RunPlaybookToCompletionAsync(
            baseRequest,
            "The container has not been deleted — retry \"apply-base\" from the Runner page.",
            cancellationToken);

        Guid syncJobId;
        try
        {
            syncJobId = await sshKeyManagement.SyncAsync(SubmittedByProvisioning, hostname, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new ProvisioningConfigurationException(
                $"{ex.Message} The container has not been deleted — use \"Synchronize now\" on the SSH Keys page once this is resolved.");
        }

        await AwaitAnsibleJobAsync(
            syncJobId,
            "sync-ssh-keys",
            "The container has not been deleted — use \"Synchronize now\" on the SSH Keys page to retry.",
            cancellationToken);
    }

    private async Task RunPlaybookToCompletionAsync(AnsibleRunRequest request, string retryHint, CancellationToken cancellationToken)
    {
        var jobId = await runnerService.SubmitAsync(request, SubmittedByProvisioning, cancellationToken);
        await AwaitAnsibleJobAsync(jobId, request.PlaybookName, retryHint, cancellationToken);
    }

    /// <summary>
    /// Polls until the submitted Ansible run finishes. There's no separate timeout here — the run
    /// is already bounded by <c>Ansible:TimeoutSeconds</c>, which the worker enforces on the
    /// process itself.
    /// </summary>
    private async Task AwaitAnsibleJobAsync(Guid ansibleJobId, string playbookName, string retryHint, CancellationToken cancellationToken)
    {
        AnsibleRunJob? job;
        do
        {
            await Task.Delay(AnsibleJobPollInterval, cancellationToken);
            job = await runnerService.GetJobOrHistoryAsync(ansibleJobId, cancellationToken);
        }
        while (job is null || !job.IsFinished);

        if (job.Stage != AnsibleRunStage.Succeeded)
        {
            throw new ProvisioningConfigurationException(
                $"'{playbookName}' failed while configuring the new container: {job.Error ?? $"exit code {job.ExitCode}"}. {retryHint}");
        }
    }
}
