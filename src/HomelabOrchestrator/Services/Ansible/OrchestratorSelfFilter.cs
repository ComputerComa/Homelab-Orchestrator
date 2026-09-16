using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Identifies the container the orchestrator itself runs in, so it's never swept into an Ansible
/// target list — the orchestrator typically runs as an LXC on the same Proxmox node it manages,
/// so without this it would show up in "every running container" and every playbook, including
/// the SSH connectivity check, would end up trying to connect back to itself.
/// </summary>
public static class OrchestratorSelfFilter
{
    public static bool IsSelf(ContainerSummary container) =>
        container.Hostname.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
}
