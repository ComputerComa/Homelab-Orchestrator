using System.ComponentModel.DataAnnotations;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Models;

/// <summary>
/// The editable provisioning form. VMID/address/template are carried as hidden fields purely so
/// the review step can redisplay the preview the operator already saw — they are display-only
/// and are never read back into a <see cref="ProvisioningRequest"/>; the worker recalculates
/// them fresh immediately before creation (see AGENTS.md). SSH keys never appear on this model
/// at all: they come from <see cref="Services.Ssh.ISshPublicKeyProvider"/> on the server side only.
/// </summary>
public class ContainerFormModel
{
    public int Vmid { get; set; }
    public string IpAddress { get; set; } = "";
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

    public bool Start { get; set; } = true;
    public bool StartAtBoot { get; set; } = true;

    public static ContainerFormModel FromDefaults(ProxmoxOptions options, ClusterPlacement placement) => new()
    {
        Vmid = placement.Vmid,
        IpAddress = placement.IpAddress,
        Template = placement.Template,
        Cores = options.DefaultCores,
        MemoryMB = options.DefaultMemoryMB,
        SwapMB = options.DefaultSwapMB,
        DiskGB = options.DefaultDiskGB,
        Start = true,
        StartAtBoot = true,
    };

    public ProvisioningRequest ToProvisioningRequest() => new(
        Hostname: Hostname,
        Cores: Cores,
        MemoryMB: MemoryMB,
        SwapMB: SwapMB,
        DiskGB: DiskGB,
        Start: Start,
        StartAtBoot: StartAtBoot);
}
