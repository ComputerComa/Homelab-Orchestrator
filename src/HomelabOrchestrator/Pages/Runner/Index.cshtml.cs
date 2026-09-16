using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomelabOrchestrator.Pages.Runner;

/// <summary>
/// HTTP binding, validation, and view models only — every playbook run happens behind
/// <see cref="IAnsibleRunnerService"/> and its background worker.
/// </summary>
public class IndexModel(IAnsibleRunnerService runner, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty]
    public RunFormModel Form { get; set; } = new();

    public IReadOnlyList<PlaybookSummary> Playbooks { get; set; } = [];
    public RunTargetOptions Targets { get; set; } = new([], []);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Playbooks = await runner.ListPlaybooksAsync(cancellationToken);
        Targets = await runner.GetRunTargetOptionsAsync(cancellationToken);
    }

    /// <summary>htmx: validates the form and shows a confirmation summary before anything is queued.</summary>
    public IActionResult OnPostReview()
    {
        return ModelState.IsValid
            ? Partial("Shared/_RunReview", Form)
            : Partial("Shared/_ValidationErrors", ModelState);
    }

    /// <summary>htmx: queues the run after the operator confirms, and returns the initial status panel.</summary>
    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Partial("Shared/_ValidationErrors", ModelState);
        }

        var jobId = await runner.SubmitAsync(Form.ToRunRequest(), cancellationToken);
        logger.LogInformation(
            "Queued Ansible run {JobId}: playbook {Playbook}, target {Target}", jobId, Form.PlaybookName, Form.Target);

        return Partial("Shared/_RunJobStatus", runner.GetJob(jobId));
    }

    /// <summary>htmx: polled every second while a run is in flight; stops once it reaches a terminal stage.</summary>
    public IActionResult OnGetJobStatus(Guid jobId)
    {
        var job = runner.GetJob(jobId);
        return job is null
            ? Partial("Shared/_Error", "This Ansible run is no longer available.")
            : Partial("Shared/_RunJobStatus", job);
    }
}
