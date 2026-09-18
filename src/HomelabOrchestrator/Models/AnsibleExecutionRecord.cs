using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Models;

/// <summary>
/// The persisted (SQLite, via <see cref="Data.ApplicationDbContext"/>) counterpart of an
/// <see cref="AnsibleRunJob"/> snapshot. A mutable class rather than a record so EF Core's normal
/// change tracking applies on update. <see cref="IAnsibleExecutionStore"/> is the only thing that
/// reads or writes this directly.
/// </summary>
/// <remarks>
/// Timestamps are <see cref="DateTime"/> (always UTC), not <see cref="DateTimeOffset"/> like
/// <see cref="AnsibleRunJob"/>'s — the SQLite EF Core provider can't translate an ORDER BY over a
/// DateTimeOffset column, which the Executions list needs for "most recent first".
/// </remarks>
public class AnsibleExecutionRecord
{
    public Guid Id { get; set; }
    public string PlaybookName { get; set; } = "";
    public AnsibleRunTargetKind TargetKind { get; set; }
    public string? TargetValue { get; set; }
    public AnsibleRunStage Stage { get; set; }

    /// <summary>The signed-in operator's username at submission time — this app has exactly one seeded account, so a display name is enough; no FK to AspNetUsers.</summary>
    public string? SubmittedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the worker actually started running the playbook — distinct from <see cref="CreatedAtUtc"/> (the Queued time), which may lag behind it while another run is in progress.</summary>
    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>The hostnames ansible-playbook was actually limited to, resolved immediately before running — a JSON string array. Null until the worker resolves the target (e.g. still Queued).</summary>
    public string? ResolvedTargetHostnamesJson { get; set; }

    /// <summary>
    /// Legacy raw output, kept only for rows persisted before <see cref="Models.AnsibleExecutionLog"/>
    /// existed. Never written by new code — new runs' output lives entirely in that table instead.
    /// </summary>
    public string? Output { get; set; }

    public int? ExitCode { get; set; }
    public string? Error { get; set; }
}
