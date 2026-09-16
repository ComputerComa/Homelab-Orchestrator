namespace HomelabOrchestrator.Services.Ssh;

/// <summary>
/// Supplies the public keys a newly created container should trust, combined from the
/// operator's workstations and the orchestrator's own keypair. Never touches the private key.
/// </summary>
public interface ISshPublicKeyProvider
{
    /// <summary>Deterministic, newline-delimited, deduplicated OpenSSH public-key records.</summary>
    Task<string> GetCombinedPublicKeysAsync(CancellationToken cancellationToken = default);
}
