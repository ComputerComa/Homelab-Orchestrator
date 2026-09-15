namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Operator-supplied inputs for a provisioning job. Deliberately excludes VMID, address, and
/// template — those are recalculated inside the worker immediately before creation and are
/// never taken from a browser-supplied value.
/// </summary>
public record ProvisioningRequest(
    string Hostname,
    int Cores,
    int MemoryMB,
    int SwapMB,
    int DiskGB,
    string SshPublicKey,
    bool Start,
    bool StartAtBoot);
