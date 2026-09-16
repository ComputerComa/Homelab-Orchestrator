namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// A snapshot of one Ansible run's state. Instances are immutable; the store replaces the
/// dictionary entry with a new snapshot on every transition (including each captured output
/// line) rather than mutating one in place.
/// </summary>
public record AnsibleRunJob
{
    public required Guid Id { get; init; }
    public required AnsibleRunRequest Request { get; init; }
    public AnsibleRunStage Stage { get; init; } = AnsibleRunStage.Queued;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>Combined stdout/stderr captured so far, newline-delimited. Never rendered as raw HTML.</summary>
    public string Output { get; init; } = "";
    public int? ExitCode { get; init; }

    /// <summary>Sanitized failure message, safe to render — never the raw exception or a secret.</summary>
    public string? Error { get; init; }

    public bool IsFinished => Stage is AnsibleRunStage.Succeeded or AnsibleRunStage.Failed;
}
