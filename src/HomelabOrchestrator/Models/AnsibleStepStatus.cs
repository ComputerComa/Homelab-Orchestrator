namespace HomelabOrchestrator.Models;

/// <summary>
/// A host's status for one Ansible task. <see cref="Running"/> is synthetic — Ansible's own
/// output never says a task is "running"; <see cref="Services.Ansible.AnsibleOutputParser"/>
/// adds it as a placeholder for a host that's known to still be in this play but hasn't reported
/// a result for the current task yet. Every other value maps 1:1 to Ansible's own result lines.
/// </summary>
public enum AnsibleStepStatus
{
    Running,
    Ok,
    Changed,
    Skipped,
    Failed,
    Unreachable,
}
