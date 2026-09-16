namespace HomelabOrchestrator.Options;

/// <summary>
/// Bound from the "Ssh" configuration section. The orchestrator runs as root in its own LXC
/// and uses these paths directly — see README.md for how to set up the key material they point at.
/// </summary>
public class SshOptions
{
    public const string SectionName = "Ssh";

    /// <summary>Public keys for the humans who should be able to log into new containers (one per line).</summary>
    public string AuthorizedKeysPath { get; set; } = "/root/.ssh/authorized_keys";

    /// <summary>The orchestrator's own public key, copied into every container it creates.</summary>
    public string OrchestratorPublicKeyPath { get; set; } = "/root/.ssh/id_ed25519.pub";

    /// <summary>
    /// The orchestrator's own private key. Reserved for the future Ansible runner to connect
    /// as <see cref="RemoteUser"/> — nothing in provisioning today reads this path.
    /// </summary>
    public string OrchestratorPrivateKeyPath { get; set; } = "/root/.ssh/id_ed25519";

    /// <summary>Remote user the future Ansible runner will connect as.</summary>
    public string RemoteUser { get; set; } = "root";

    /// <summary>Remote port the future Ansible runner will connect to.</summary>
    public int Port { get; set; } = 22;
}
