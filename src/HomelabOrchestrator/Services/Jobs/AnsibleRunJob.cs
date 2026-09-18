namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// A snapshot of one Ansible run's state. Instances are immutable; the store replaces the
/// dictionary entry with a new snapshot on every transition rather than mutating one in place.
/// Captured output is no longer carried here — it grew this record via <c>Output + line + "\n"</c>
/// on every single line, an O(n) copy per line. Output now lives in durable, sequenced
/// <see cref="Models.AnsibleExecutionLog"/> rows and a live in-memory buffer (see
/// <see cref="AnsibleExecutionLogBuffer"/> and <see cref="IAnsibleRunJobStore.GetLiveLogSnapshot"/>);
/// <see cref="Ansible.IAnsibleRunnerService.GetReconstructedOutputAsync"/> is how a page gets the
/// full text for either a live or a finished run.
/// </summary>
public record AnsibleRunJob
{
    public required Guid Id { get; init; }
    public required AnsibleRunRequest Request { get; init; }
    public AnsibleRunStage Stage { get; init; } = AnsibleRunStage.Queued;

    /// <summary>The signed-in operator's username at submission time — see <see cref="Models.AnsibleExecutionRecord.SubmittedBy"/>.</summary>
    public string? SubmittedBy { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the worker actually started running the playbook — see <see cref="Models.AnsibleExecutionRecord.StartedAtUtc"/>.</summary>
    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>The hostnames actually resolved for this run's target, in resolution order. Null until the worker resolves it.</summary>
    public IReadOnlyList<string>? ResolvedTargetHostnames { get; init; }

    public int? ExitCode { get; init; }

    /// <summary>Sanitized failure message, safe to render — never the raw exception or a secret.</summary>
    public string? Error { get; init; }

    public bool IsFinished => Stage is AnsibleRunStage.Succeeded or AnsibleRunStage.Failed
        or AnsibleRunStage.TimedOut or AnsibleRunStage.Cancelled or AnsibleRunStage.Interrupted;
}
