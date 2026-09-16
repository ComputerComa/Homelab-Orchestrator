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

    /// <summary>Every LXC container on the configured node, as Proxmox currently reports it.</summary>
    Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The container's actual configured IPv4 address, read from its net0 device — not derived
    /// from any convention. Null if net0 has no static address (DHCP/manual) or doesn't exist.
    /// </summary>
    Task<string?> GetContainerAddressAsync(int vmid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a tag to the container, re-reading its current tags immediately before writing so
    /// nothing else's tags are lost to a stale read. A no-op if the tag is already present.
    /// </summary>
    Task AddTagAsync(int vmid, string tag, CancellationToken cancellationToken = default);
}
