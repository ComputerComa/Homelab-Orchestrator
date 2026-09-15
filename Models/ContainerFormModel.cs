using System.ComponentModel.DataAnnotations;

namespace HomelabOrchestrator.Models;

/// <summary>
/// The editable form plus the read-only connection fields, which round-trip through the
/// page as hidden inputs so the "review" and "create" handlers see the same values the
/// user was shown.
/// </summary>
public class ContainerFormModel
{
    public int Vmid { get; set; }

    [Required]
    public string IpAddress { get; set; } = "";

    [Required]
    public string Template { get; set; } = "";

    [Required(ErrorMessage = "Hostname is required.")]
    [StringLength(63)]
    public string Hostname { get; set; } = "";

    [Range(1, 32, ErrorMessage = "Cores must be between 1 and 32.")]
    public int Cores { get; set; }

    [Range(128, 1_048_576, ErrorMessage = "Memory must be at least 128 MB.")]
    public int MemoryMB { get; set; }

    [Range(0, 1_048_576, ErrorMessage = "Swap cannot be negative.")]
    public int SwapMB { get; set; }

    [Range(1, 2048, ErrorMessage = "Disk size must be at least 1 GB.")]
    public int DiskGB { get; set; }

    [Required(ErrorMessage = "An SSH public key is required.")]
    public string SshPublicKey { get; set; } = "";

    public bool Start { get; set; } = true;
    public bool StartAtBoot { get; set; } = true;

    public static ContainerFormModel FromDefaults(ProxmoxOptions options, ClusterPlacement connection, string sshPublicKey) => new()
    {
        Vmid = connection.Vmid,
        IpAddress = connection.IpAddress,
        Template = connection.Template,
        Cores = options.DefaultCores,
        MemoryMB = options.DefaultMemoryMB,
        SwapMB = options.DefaultSwapMB,
        DiskGB = options.DefaultDiskGB,
        SshPublicKey = sshPublicKey,
        Start = true,
        StartAtBoot = true,
    };

    public ContainerRequest ToContainerRequest() => new()
    {
        Vmid = Vmid,
        Hostname = Hostname,
        IpAddress = IpAddress,
        Template = Template,
        SshPublicKey = SshPublicKey,
        Cores = Cores,
        MemoryMB = MemoryMB,
        SwapMB = SwapMB,
        DiskGB = DiskGB,
        Start = Start,
        StartAtBoot = StartAtBoot,
    };
}
