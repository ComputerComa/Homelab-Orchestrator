namespace HomelabOrchestrator.Models;

/// <summary>One host's steps for the current playbook run, in the order Ansible reported them.</summary>
public record AnsibleHostRun(string Hostname, IReadOnlyList<AnsibleTaskStep> Steps)
{
    public bool HasFailure => Steps.Any(s => s.Status is AnsibleStepStatus.Failed or AnsibleStepStatus.Unreachable);
}
