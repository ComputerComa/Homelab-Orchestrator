using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Proxmox;

/// <summary>
/// The only application boundary that calls Corsinvest.ProxmoxVE.Api. Every member returns a
/// strongly typed application record — no dynamic Proxmox response ever crosses this interface.
/// </summary>
public interface IProxmoxService
{
    Task<int> GetNextVmIdAsync(CancellationToken cancellationToken = default);

    Task<string> FindLatestDebianTemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the LXC container and waits for the Proxmox task to finish.</summary>
    Task<CreatedContainer> CreateContainerAsync(ContainerRequest request, CancellationToken cancellationToken = default);
}
