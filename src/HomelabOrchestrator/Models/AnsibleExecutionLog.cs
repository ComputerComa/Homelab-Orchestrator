namespace HomelabOrchestrator.Models;

/// <summary>
/// One line of an Ansible run's captured stdout/stderr, durably persisted (via
/// <see cref="Data.ApplicationDbContext"/>) so a page can tail it incrementally by
/// <see cref="Sequence"/> instead of re-loading the whole run's output on every poll. A mutable
/// class rather than a record so EF Core's normal change tracking applies, matching
/// <see cref="AnsibleExecutionRecord"/>'s style. No navigation property to
/// <see cref="AnsibleExecutionRecord"/> — just the <see cref="ExecutionId"/> foreign key, matching
/// how the rest of this codebase models relationships.
/// </summary>
public class AnsibleExecutionLog
{
    public long Id { get; set; }
    public Guid ExecutionId { get; set; }

    /// <summary>Strictly increasing per execution, starting at 1 — what the live-log cursor filters on.</summary>
    public long Sequence { get; set; }

    public DateTime TimestampUtc { get; set; }
    public AnsibleLogStream Stream { get; set; }
    public string Text { get; set; } = "";
}

public enum AnsibleLogStream
{
    Stdout,
    Stderr,
}
