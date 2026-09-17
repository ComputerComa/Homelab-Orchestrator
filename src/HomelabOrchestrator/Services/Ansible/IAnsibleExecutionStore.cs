using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Persists Ansible run history in SQLite (via <see cref="Data.ApplicationDbContext"/>) so it
/// survives a process restart and can be browsed after the fact — the "Job services: ...
/// persistence" boundary. This is separate from <see cref="IAnsibleRunJobStore"/>, which stays
/// the in-memory hot path for the ~1s live-polling loop; a run is only ever saved here at its
/// Queued, Running, and terminal transitions, never per captured output line.
/// </summary>
public interface IAnsibleExecutionStore
{
    /// <summary>Inserts a new execution row, or updates the existing one with the same <see cref="AnsibleRunJob.Id"/>.</summary>
    Task SaveAsync(AnsibleRunJob job, CancellationToken cancellationToken = default);

    /// <summary>The most recently created executions, newest first.</summary>
    Task<IReadOnlyList<AnsibleExecutionRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);

    Task<AnsibleExecutionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}
