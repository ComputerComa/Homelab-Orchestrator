using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>The application/orchestration boundary the Runner page calls into — never IPlaybookCatalog/IProxmoxService/the job queue directly.</summary>
public interface IAnsibleRunnerService
{
    Task<IReadOnlyList<PlaybookSummary>> ListPlaybooksAsync(CancellationToken cancellationToken = default);

    /// <summary>Every playbook's parsed name/description/steps, for the playbook-picker modal — loaded up front since there are only ever a handful.</summary>
    Task<IReadOnlyList<PlaybookDetail>> ListPlaybookDetailsAsync(CancellationToken cancellationToken = default);

    /// <summary>A non-reserving preview of currently running containers/tags for the picker. The worker re-validates before running.</summary>
    Task<RunTargetOptions> GetRunTargetOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a run and returns its job ID for status polling. <paramref name="submittedBy"/> is the signed-in operator's username, read at the Page layer — this service stays HTTP-agnostic.</summary>
    Task<Guid> SubmitAsync(AnsibleRunRequest request, string? submittedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a run by ID for the Executions detail page: the in-memory, still-running-process
    /// job store first (cheap, and holds every run from this process's lifetime), falling back to
    /// persisted SQLite history only when not found there — e.g. after a restart.
    /// </summary>
    Task<AnsibleRunJob?> GetJobOrHistoryAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>The most recent persisted runs, newest first, for the Executions list page.</summary>
    Task<IReadOnlyList<AnsibleExecutionRecord>> ListRecentExecutionsAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// The full captured text for one run, for <see cref="AnsibleOutputParser"/> and the raw
    /// output/download views — whichever of these has it: the live in-memory buffer (if the run is
    /// still in flight), the durable per-line log table, or (for a run persisted before that table
    /// existed) the legacy <see cref="AnsibleExecutionRecord.Output"/> column, in that order.
    /// </summary>
    Task<string> GetReconstructedOutputAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The console's cursor-based incremental fetch: only <see cref="AnsibleExecutionLog"/> rows
    /// with <see cref="AnsibleExecutionLog.Sequence"/> greater than <paramref name="afterSequence"/>
    /// — never the whole run's output. Always reads the durable log table, even for a live run, so
    /// the console can lag the in-memory Tasks/Hosts views by up to one flush interval.
    /// </summary>
    Task<IReadOnlyList<AnsibleExecutionLog>> GetLogTailAsync(Guid executionId, long afterSequence, CancellationToken cancellationToken = default);
}
