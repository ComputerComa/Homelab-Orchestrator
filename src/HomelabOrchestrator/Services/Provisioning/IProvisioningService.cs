using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Services.Provisioning;

/// <summary>
/// The application/orchestration boundary Razor Pages call into. Pages never talk to
/// <see cref="Proxmox.IProxmoxService"/> or the job queue/store directly.
/// </summary>
public interface IProvisioningService
{
    /// <summary>
    /// A non-reserving preview of the VMID/address/template a new container would get right
    /// now. Never trust this for the actual creation — see <see cref="ProvisioningWorker"/>.
    /// </summary>
    Task<ClusterPlacement> GetPlacementPreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a provisioning job and returns its ID for status polling.</summary>
    Task<Guid> SubmitAsync(ProvisioningRequest request, CancellationToken cancellationToken = default);

    ProvisioningJob? GetJob(Guid jobId);
}
