namespace HomelabOrchestrator.Models;

/// <summary>Everything needed to create one LXC container, handed to <see cref="Services.IProxmoxService"/>.</summary>
public class ContainerRequest
{
    public required int Vmid { get; init; }
    public required string Hostname { get; init; }
    public required string IpAddress { get; init; }
    public required string Template { get; init; }
    public required string SshPublicKey { get; init; }
    public int Cores { get; init; }
    public int MemoryMB { get; init; }
    public int SwapMB { get; init; }
    public int DiskGB { get; init; }
    public bool Start { get; init; }
    public bool StartAtBoot { get; init; }
}

/// <summary>Returned once Proxmox reports the create task finished.</summary>
public record CreatedContainer(int Vmid, string Hostname, string IpAddress, string Template, TimeSpan Duration);
