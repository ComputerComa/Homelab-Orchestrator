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
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Output { get; set; } = "";
    public int? ExitCode { get; set; }
    public string? Error { get; set; }
}
