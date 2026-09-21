using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Ssh;

public enum EnrollmentFailureReason
{
    InvalidToken,
    TokenExpiredOrUsed,
    InvalidDeviceName,
    PrivateKeyRejected,
    UnsupportedAlgorithm,
    MalformedKey,
    DuplicateFingerprint,
}

public record EnrollmentResult(bool Succeeded, string? Fingerprint, EnrollmentFailureReason? FailureReason)
{
    public static EnrollmentResult Success(string fingerprint) => new(true, fingerprint, null);

    public static EnrollmentResult Failure(EnrollmentFailureReason reason) => new(false, null, reason);
}

/// <summary>
/// The application/orchestration boundary Pages and the enrollment endpoint call into — never
/// <see cref="ISshKeyStore"/>/<see cref="ISshEnrollmentTokenStore"/> directly. Owns every business
/// rule: key parsing/normalization, algorithm allowlisting, private-key rejection, duplicate
/// rejection, token issuance/consumption, approval-state transitions, and the sync hand-off to the
/// Ansible pipeline.
/// </summary>
public interface ISshKeyManagementService
{
    Task<IReadOnlyList<SshPublicKey>> ListKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The enrollment path. Never throws for "bad input" — callers get a discriminated result so
    /// the endpoint can map failure reasons to safe HTTP responses without leaking internals.
    /// </summary>
    Task<EnrollmentResult> EnrollAsync(string tokenPlaintext, string? deviceName, string? publicKeyLine, CancellationToken cancellationToken = default);

    /// <summary>Creates a new token and returns its plaintext bearer value — shown to the operator exactly once, never persisted anywhere.</summary>
    Task<string> CreateEnrollmentTokenAsync(string? createdBy, CancellationToken cancellationToken = default);

    /// <summary>Only legal from Pending. Throws otherwise.</summary>
    Task ApproveAsync(Guid keyId, string? approvedBy, CancellationToken cancellationToken = default);

    /// <summary>Only legal from Enabled. Throws otherwise.</summary>
    Task RevokeAsync(Guid keyId, string? revokedBy, CancellationToken cancellationToken = default);

    /// <summary>Only legal while Pending — an enabled or revoked key is never hard-deleted. Throws otherwise.</summary>
    Task DeletePendingAsync(Guid keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gathers every Enabled key plus the orchestrator's own key and submits the sync-ssh-keys.yml
    /// run through the normal Ansible execution pipeline. Throws before queuing anything if the
    /// orchestrator's own key can't be read — this is the actual lockout guard.
    /// </summary>
    /// <param name="targetHostname">
    /// When null (the default — every UI call site), synchronizes the whole managed fleet. When
    /// given, scopes the run to just that one container instead — used right after provisioning a
    /// new container, where pushing an exclusive-mode replace to every other managed container on
    /// every single provision would be needless blast radius.
    /// </param>
    Task<Guid> SyncAsync(string? submittedBy, string? targetHostname = null, CancellationToken cancellationToken = default);
}
