namespace HomelabOrchestrator.Models;

/// <summary>
/// One TASK's results aggregated across every host that reported for it, in the order Ansible ran
/// tasks — the task-centric projection of the same data <see cref="AnsibleHostRun"/> presents
/// host-first. A host with more than one entry in <see cref="AnsibleHostTaskResult.Results"/> is a
/// looped task; see <see cref="AnsibleTaskStep.LoopItem"/>.
/// </summary>
public record AnsibleTaskSummary(string TaskName, int Order, IReadOnlyList<AnsibleHostTaskResult> HostResults)
{
    public bool HasFailure => HostResults.Any(h => h.Results.Any(r => r.Status is AnsibleStepStatus.Failed or AnsibleStepStatus.Unreachable));
}

/// <summary>One host's result(s) for one task — more than one entry means the task looped for this host.</summary>
public record AnsibleHostTaskResult(string Hostname, IReadOnlyList<AnsibleTaskStep> Results);
