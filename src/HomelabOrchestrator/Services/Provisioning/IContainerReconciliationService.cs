using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Provisioning;

/// <summary>
/// Finds containers Proxmox already knows about but that predate the orchestrator (not tagged
/// managed-by-orchestrator), and lets an operator adopt the ones whose actual configured address
/// already matches what the VMID convention expects.
/// </summary>
public interface IContainerReconciliationService
{
    Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-verifies each container's eligibility against live Proxmox state immediately before
    /// tagging it — the caller's selection is never trusted as still accurate.
    /// </summary>
    Task<AdoptionResult> AdoptAsync(IReadOnlyList<int> vmids, CancellationToken cancellationToken = default);
}
