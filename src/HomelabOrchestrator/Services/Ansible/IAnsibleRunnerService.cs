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

    /// <summary>Queues a run and returns its job ID for status polling.</summary>
    Task<Guid> SubmitAsync(AnsibleRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a run by ID for the Executions detail page: the in-memory, still-running-process
    /// job store first (cheap, and holds every run from this process's lifetime), falling back to
    /// persisted SQLite history only when not found there — e.g. after a restart.
    /// </summary>
    Task<AnsibleRunJob?> GetJobOrHistoryAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>The most recent persisted runs, newest first, for the Executions list page.</summary>
    Task<IReadOnlyList<AnsibleExecutionRecord>> ListRecentExecutionsAsync(int limit = 100, CancellationToken cancellationToken = default);
}
