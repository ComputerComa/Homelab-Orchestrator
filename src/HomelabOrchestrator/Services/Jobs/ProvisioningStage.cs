namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Stages the current, Proxmox-only provisioning workflow actually performs. This is a subset
/// of the stages the full product intent describes (see AGENTS.md) — WaitingForSsh and
/// ApplyingBase are not implemented yet and are intentionally absent rather than faked.
/// </summary>
public enum ProvisioningStage
{
    Queued,
    Allocating,
    CreatingContainer,
    WaitingForProxmox,
    Succeeded,
    Failed,
}
