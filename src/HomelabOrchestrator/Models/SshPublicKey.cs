namespace HomelabOrchestrator.Models;

public enum SshKeyStatus
{
    Pending,
    Enabled,
    Revoked,
}

/// <summary>
/// A device's enrolled SSH public key, tracked through an approval lifecycle before it's ever
/// synchronized to a container. Never stores private key material. <see cref="Fingerprint"/> is
/// the identity used for deduplication (enforced by a unique index) — the device-supplied
/// <see cref="DeviceName"/> and the original key comment (never stored — see
/// <see cref="Services.Ssh.SshKeyFingerprint"/>/enrollment flow) are untrusted display metadata
/// only, never used as identity and never deployed to a container.
/// </summary>
public class SshPublicKey
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; } = "";

    /// <summary>Normalized OpenSSH key type, e.g. "ssh-ed25519".</summary>
    public string Algorithm { get; set; } = "";

    /// <summary>The key's base64-encoded wire data only — no comment, no type prefix.</summary>
    public string KeyDataBase64 { get; set; } = "";

    /// <summary>OpenSSH's own "SHA256:&lt;base64-no-padding&gt;" format — matches `ssh-keygen -lf` output verbatim.</summary>
    public string Fingerprint { get; set; } = "";

    public SshKeyStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Signed-in operator's username — no FK, mirrors AnsibleExecutionRecord.SubmittedBy (this app has exactly one operator account).</summary>
    public string? ApprovedBy { get; set; }

    public string? RevokedBy { get; set; }
}
