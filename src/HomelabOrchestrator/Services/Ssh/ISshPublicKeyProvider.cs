namespace HomelabOrchestrator.Services.Ssh;

/// <summary>
/// Supplies the public keys a newly created container should trust, combined from the
/// operator's workstations and the orchestrator's own keypair. Never touches the private key.
/// </summary>
public interface ISshPublicKeyProvider
{
    /// <summary>Deterministic, newline-delimited, deduplicated OpenSSH public-key records.</summary>
    Task<string> GetCombinedPublicKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The orchestrator's own public key line only (never the operator workstation keys, never
    /// the private key) — the one guaranteed to be included in every SSH key synchronization so
    /// the orchestrator can never lock its own automation out. Null if it can't be read.
    /// </summary>
    Task<string?> GetOrchestratorPublicKeyAsync(CancellationToken cancellationToken = default);
}
