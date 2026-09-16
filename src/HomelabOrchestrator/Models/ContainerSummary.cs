namespace HomelabOrchestrator.Models;

/// <summary>
/// An existing LXC container as Proxmox reports it — raw facts only, no derived data.
/// <see cref="Tags"/> is already split from Proxmox's own semicolon-separated wire format
/// (e.g. "base;managed-by-orchestrator;mqtt") into a plain list — that's a format translation
/// at the Proxmox boundary, not a derived business rule.
/// </summary>
public record ContainerSummary(int Vmid, string Hostname, string Status, IReadOnlyList<string> Tags)
{
    public bool IsRunning => Status.Equals("running", StringComparison.OrdinalIgnoreCase);
}
