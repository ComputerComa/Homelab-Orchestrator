namespace HomelabOrchestrator.Options;

/// <summary>
/// Bound from the "Ansible" configuration section. Defaults assume the local development layout
/// (the ansible/ directory as a sibling of src/ and tests/) — override RepositoryRoot for
/// deployments where that isn't true.
/// </summary>
public class AnsibleOptions
{
    public const string SectionName = "Ansible";

    /// <summary>Root of the ansible/ directory (ansible.cfg, playbooks/, inventory/, roles/). Computed at startup if left blank.</summary>
    public string RepositoryRoot { get; set; } = "";

    public string ExecutablePath { get; set; } = "ansible-playbook";

    /// <summary>Relative to RepositoryRoot — the inventory source every run uses; targeting is done with --limit.</summary>
    public string InventoryFile { get; set; } = "inventory/orchestrator.yml";

    public int TimeoutSeconds { get; set; } = 600;
}
