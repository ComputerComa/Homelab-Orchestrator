namespace HomelabOrchestrator.Models;

/// <summary>
/// Bound from the "Proxmox" configuration section (appsettings.json, user-secrets,
/// or Proxmox__* environment variables). Host/TokenId/TokenSecret are required and
/// validated on startup.
/// </summary>
public class ProxmoxOptions
{
    public const string SectionName = "Proxmox";

    public string Host { get; set; } = "";
    public string TokenId { get; set; } = "";
    public string TokenSecret { get; set; } = "";
    public bool ValidateTlsCertificate { get; set; }

    public string Node { get; set; } = "pve";
    public string TemplateStorage { get; set; } = "local";
    public string RootfsStorage { get; set; } = "local-lvm";
    public string Bridge { get; set; } = "vmbr0";
    public string Gateway { get; set; } = "10.0.1.1";
    public string Nameserver { get; set; } = "10.0.200.1 10.0.200.2";
    public int Subnet { get; set; } = 16;

    /// <summary>First three octets of the network used to derive a container's address from its VMID (e.g. "10.0.150").</summary>
    public string IpNetworkPrefix { get; set; } = "10.0.150";
    public int IpHostMin { get; set; } = 2;
    public int IpHostMax { get; set; } = 254;

    public int DefaultCores { get; set; } = 2;
    public int DefaultMemoryMB { get; set; } = 2048;
    public int DefaultSwapMB { get; set; } = 512;
    public int DefaultDiskGB { get; set; } = 8;

    /// <summary>Optional path to a public key file read to pre-fill the form's SSH key field.</summary>
    public string? DefaultSshPublicKeyPath { get; set; }
}
