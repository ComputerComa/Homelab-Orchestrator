namespace HomelabOrchestrator.Models;

/// <summary>
/// A short-lived, single-use bearer token authorizing exactly one call to
/// <c>POST /api/ssh-keys/enroll</c>. Kept as a separate table from <see cref="SshPublicKey"/>:
/// a token's lifecycle (issued, consumed, dead) and sensitivity (hash-only, never re-displayed)
/// are unrelated to a key's (long-lived, re-displayed as a fingerprint on every page load), and a
/// token is not itself identity — it authorizes one future enrollment, nothing more.
/// </summary>
public class SshEnrollmentToken
{
    public Guid Id { get; set; }

    /// <summary>Hex SHA-256 of the raw random bearer token. The plaintext is never persisted anywhere.</summary>
    public string TokenHash { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set exactly once, atomically, on successful enrollment — makes the token permanently invalid.</summary>
    public DateTime? UsedAtUtc { get; set; }

    public string? CreatedBy { get; set; }
}
