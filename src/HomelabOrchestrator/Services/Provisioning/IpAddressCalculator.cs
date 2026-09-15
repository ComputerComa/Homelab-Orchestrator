namespace HomelabOrchestrator.Services.Provisioning;

/// <summary>
/// Derives a container's address from its VMID under the homelab's current convention
/// (VMID becomes the last octet of a configured /16, e.g. VMID 141 -> 10.0.150.141).
/// Pure and unit-testable; carries no Proxmox dependency.
/// </summary>
public static class IpAddressCalculator
{
    public static bool TryCalculate(int vmid, string networkPrefix, int hostMin, int hostMax, out string ipAddress)
    {
        if (vmid < hostMin || vmid > hostMax)
        {
            ipAddress = "";
            return false;
        }

        ipAddress = $"{networkPrefix}.{vmid}";
        return true;
    }
}
