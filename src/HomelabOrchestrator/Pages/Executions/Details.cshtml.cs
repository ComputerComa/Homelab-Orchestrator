using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomelabOrchestrator.Pages.Executions;

/// <summary>
/// One run's live-or-historical view. <see cref="IAnsibleRunnerService.GetJobOrHistoryAsync"/>
/// transparently resolves either an in-flight job (still in the in-memory store — this page keeps
/// polling) or an already-finished one loaded from persisted SQLite history (renders the terminal
/// banner immediately; <c>_RunJobStatus</c> only polls in the non-terminal case).
/// </summary>
public class DetailsModel(IAnsibleRunnerService runner) : PageModel
{
    public AnsibleRunJob? Job { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Job = await runner.GetJobOrHistoryAsync(id, cancellationToken);
        return Job is null ? NotFound() : Page();
    }

    /// <summary>htmx: polled every second by `_RunJobStatus` while a run is in flight.</summary>
    public async Task<IActionResult> OnGetJobStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await runner.GetJobOrHistoryAsync(id, cancellationToken);
        return job is null
            ? Partial("Shared/_Error", "This Ansible run is no longer available.")
            : Partial("Shared/_RunJobStatus", job);
    }
}
