namespace HomelabOrchestrator.Options;

/// <summary>
/// Bound from the "Proxmox" configuration section (appsettings.json, user-secrets,
/// or Proxmox__* environment variables). Host and ApiToken are required and validated
/// on startup; everything else is an infrastructure default that must stay overridable
/// through configuration rather than hard-coded.
/// </summary>
public class ProxmoxOptions
{
    public const string SectionName = "Proxmox";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 8006;

    /// <summary>Combined token in Proxmox's own format: "user@realm!tokenid=secret".</summary>
    public string ApiToken { get; set; } = "";
    public bool ValidateCertificate { get; set; }

    public string Node { get; set; } = "pve";
    public string TemplateStorage { get; set; } = "local";
    public string RootfsStorage { get; set; } = "local-lvm";
    public string Bridge { get; set; } = "vmbr0";
    public string Gateway { get; set; } = "10.0.1.1";
    public string[] NameServers { get; set; } = [];
    public int Subnet { get; set; } = 16;

    /// <summary>First three octets of the network used to derive a container's address from its VMID (e.g. "10.0.150").</summary>
    public string NetworkPrefix { get; set; } = "10.0.150";
    public int IpHostMin { get; set; } = 2;
    public int IpHostMax { get; set; } = 254;

    public int DefaultCores { get; set; } = 2;
    public int DefaultMemoryMB { get; set; } = 2048;
    public int DefaultSwapMB { get; set; } = 512;
    public int DefaultDiskGB { get; set; } = 8;
}
