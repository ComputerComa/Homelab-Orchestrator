namespace HomelabOrchestrator.Services.Jobs;

public enum AnsibleRunStage
{
    Queued,
    Running,
    Succeeded,
    Failed,

    /// <summary>ansible-playbook was killed for exceeding its configured timeout.</summary>
    TimedOut,

    /// <summary>
    /// Reserved for an operator-initiated cancellation. Nothing in this application can set this
    /// stage yet — there is no cancel action — but it's modeled and handled everywhere a stage is
    /// switched on so a future cancel feature has nothing left to wire up here.
    /// </summary>
    Cancelled,

    /// <summary>Left Queued or Running when the orchestrator process stopped — either app shutdown mid-run, or a run recovered as stuck at the next startup.</summary>
    Interrupted,
}
