namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Stages the provisioning workflow performs, in order. <see cref="WaitingForSsh"/> and
/// <see cref="ApplyingBase"/> only run when the job requested the container be started
/// (<c>ProvisioningRequest.Start</c>) — a container left stopped goes straight from
/// <see cref="WaitingForProxmox"/> to <see cref="Succeeded"/>, since there's nothing reachable to
/// configure yet. <see cref="ApplyingBase"/> covers both running <c>apply-base.yml</c> and
/// synchronizing the current SSH key registry to the new container — a failure at either point,
/// or during <see cref="WaitingForSsh"/>, lands the job in <see cref="Failed"/> without ever
/// deleting the underlying LXC; re-running the base playbook or a key sync against it afterward
/// is an ordinary Runner/SSH Keys page action, not a special retry path.
/// </summary>
public enum ProvisioningStage
{
    Queued,
    Allocating,
    CreatingContainer,
    WaitingForProxmox,
    WaitingForSsh,
    ApplyingBase,
    Succeeded,
    Failed,
}
