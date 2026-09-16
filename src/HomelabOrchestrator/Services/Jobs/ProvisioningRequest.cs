namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Operator-supplied inputs for a provisioning job. Deliberately excludes VMID, address, and
/// template — those are recalculated inside the worker immediately before creation and are
/// never taken from a browser-supplied value. SSH keys are excluded too: they never come from
/// the browser at all — see <see cref="Ssh.ISshPublicKeyProvider"/>.
/// </summary>
public record ProvisioningRequest(
    string Hostname,
    int Cores,
    int MemoryMB,
    int SwapMB,
    int DiskGB,
    bool Start,
    bool StartAtBoot);
