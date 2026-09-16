using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>The application/orchestration boundary the Runner page calls into — never IPlaybookCatalog/IProxmoxService/the job queue directly.</summary>
public interface IAnsibleRunnerService
{
    Task<IReadOnlyList<PlaybookSummary>> ListPlaybooksAsync(CancellationToken cancellationToken = default);

    /// <summary>A non-reserving preview of currently running containers/tags for the picker. The worker re-validates before running.</summary>
    Task<RunTargetOptions> GetRunTargetOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a run and returns its job ID for status polling.</summary>
    Task<Guid> SubmitAsync(AnsibleRunRequest request, CancellationToken cancellationToken = default);

    AnsibleRunJob? GetJob(Guid jobId);
}
