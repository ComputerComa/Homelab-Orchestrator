using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Provisioning;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// The single background worker that actually talks to Proxmox for provisioning. Reading the
/// queue with one sequential <c>await foreach</c> loop is what serializes allocation: a job
/// only calls <see cref="IProxmoxService.GetNextVmIdAsync"/> after the previous job's creation
/// has fully finished, so two jobs can never be handed the same VMID or calculated address.
/// VMID/address/template are recalculated here, immediately before creation — a browser
/// preview shown earlier is never trusted or reused.
/// </summary>
public class ProvisioningWorker(
    IProvisioningJobQueue queue,
    IProvisioningJobStore store,
    IProxmoxService proxmox,
    IOptions<ProxmoxOptions> options,
    ILogger<ProvisioningWorker> logger) : BackgroundService
{
    private readonly ProxmoxOptions _options = options.Value;

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

            var containerRequest = new ContainerRequest
            {
                Vmid = vmid,
                Hostname = job.Request.Hostname,
                IpAddress = ipAddress,
                Template = template,
                SshPublicKey = job.Request.SshPublicKey,
                Cores = job.Request.Cores,
                MemoryMB = job.Request.MemoryMB,
                SwapMB = job.Request.SwapMB,
                DiskGB = job.Request.DiskGB,
                Start = job.Request.Start,
                StartAtBoot = job.Request.StartAtBoot,
            };

            store.Update(jobId, j => j with { Stage = ProvisioningStage.WaitingForProxmox });

            var created = await proxmox.CreateContainerAsync(containerRequest, cancellationToken);

            store.Update(jobId, j => j with { Stage = ProvisioningStage.Succeeded, CompletedAtUtc = DateTimeOffset.UtcNow });
            logger.LogInformation(
                "Provisioning job {JobId} succeeded: {Hostname} (VMID {Vmid}) at {IpAddress}",
                jobId, created.Hostname, created.Vmid, created.IpAddress);
        }
        catch (Exception ex)
        {
            var sanitized = ex is ProxmoxOperationException
                ? ex.Message
                : "An unexpected error occurred while provisioning this container. See the server log for details.";

            if (ex is ProxmoxOperationException)
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
}
