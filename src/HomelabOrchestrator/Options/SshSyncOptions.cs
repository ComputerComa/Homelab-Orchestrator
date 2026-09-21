namespace HomelabOrchestrator.Options;

/// <summary>Bound from the "SshSync" configuration section — governs how sync-ssh-keys.yml reconciles /root/.ssh/authorized_keys on managed containers.</summary>
public class SshSyncOptions
{
    public const string SectionName = "SshSync";

    /// <summary>
    /// false (default): preserve keys not present in the registry (merge into a managed block).
    /// true: replace the whole file with exactly the registry's enabled keys plus the
    /// orchestrator's own key. Only enable once the registry is confirmed to represent every
    /// key that should have access — this removes anything else.
    /// </summary>
    public bool Exclusive { get; set; }
}
