using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services;

public interface IProxmoxService
{
    /// <summary>Next free VMID, the address it maps to, and the newest Debian 13 template.</summary>
    Task<ClusterPlacement> GetConnectionInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the LXC container and waits for the Proxmox task to finish.</summary>
    Task<CreatedContainer> CreateContainerAsync(ContainerRequest request, CancellationToken cancellationToken = default);
}
