using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Persists Ansible run history in SQLite (via <see cref="Data.ApplicationDbContext"/>) so it
/// survives a process restart and can be browsed after the fact — the "Job services: ...
/// persistence" boundary. This is separate from <see cref="IAnsibleRunJobStore"/>, which stays
/// the in-memory hot path for the ~1s live-polling loop; the execution row itself is only ever
/// saved here at its Queued, Running, and terminal transitions. Captured output is a separate
/// concern, written incrementally and in batches to <see cref="IAnsibleExecutionLogStore"/>.
/// </summary>
public interface IAnsibleExecutionStore
{
    /// <summary>Inserts a new execution row, or updates the existing one with the same <see cref="AnsibleRunJob.Id"/>.</summary>
    Task SaveAsync(AnsibleRunJob job, CancellationToken cancellationToken = default);

    /// <summary>The most recently created executions, newest first.</summary>
    Task<IReadOnlyList<AnsibleExecutionRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);

    Task<AnsibleExecutionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks every execution still Queued or Running as Interrupted. Call once at startup — after
    /// a process exit, neither state can ever resolve itself, so without this an execution page
    /// left mid-run would poll forever. Returns how many rows were changed.
    /// </summary>
    Task<int> InterruptStuckExecutionsAsync(CancellationToken cancellationToken = default);
}
