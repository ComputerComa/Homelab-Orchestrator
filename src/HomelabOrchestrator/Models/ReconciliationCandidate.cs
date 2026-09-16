namespace HomelabOrchestrator.Models;

/// <summary>
/// A container not currently tagged managed-by-orchestrator, alongside the address the VMID
/// convention expects it to have and the address it's actually configured with in Proxmox.
/// </summary>
public record ReconciliationCandidate(int Vmid, string Hostname, string ExpectedAddress, string? ActualAddress)
{
    public bool IsEligible => ActualAddress == ExpectedAddress;
}
