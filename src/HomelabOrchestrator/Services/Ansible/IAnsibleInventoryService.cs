namespace HomelabOrchestrator.Services.Ansible;

/// <summary>The application boundary Razor Pages/endpoints call for Ansible inventory — never Proxmox directly.</summary>
public interface IAnsibleInventoryService
{
    /// <summary>Inventory containing just this one container, or null if no such container exists.</summary>
    Task<AnsibleInventory?> GetContainerInventoryAsync(int vmid, CancellationToken cancellationToken = default);

    /// <summary>Inventory containing every container Proxmox currently reports as running.</summary>
    Task<AnsibleInventory> GetRunningContainersInventoryAsync(CancellationToken cancellationToken = default);
}
