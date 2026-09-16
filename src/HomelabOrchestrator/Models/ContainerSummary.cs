namespace HomelabOrchestrator.Models;

/// <summary>An existing LXC container as Proxmox reports it — raw facts only, no derived data.</summary>
public record ContainerSummary(int Vmid, string Hostname, string Status)
{
    public bool IsRunning => Status.Equals("running", StringComparison.OrdinalIgnoreCase);
}
