using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomelabOrchestrator.Pages.Runner;

/// <summary>
/// HTTP binding, validation, and view models only — every playbook run happens behind
/// <see cref="IAnsibleRunnerService"/> and its background worker. This page only launches a run;
/// watching it live and browsing past runs both live under <c>/Executions</c>.
/// </summary>
public class IndexModel(IAnsibleRunnerService runner, ILogger<IndexModel> logger) : PageModel
{
    private const int RecentRunsCount = 5;

    [BindProperty]
    public RunFormModel Form { get; set; } = new();

    public IReadOnlyList<PlaybookSummary> Playbooks { get; set; } = [];
    public RunTargetOptions Targets { get; set; } = new([], []);
    public IReadOnlyList<AnsibleExecutionRecord> RecentRuns { get; set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Playbooks = await runner.ListPlaybooksAsync(cancellationToken);
        Targets = await runner.GetRunTargetOptionsAsync(cancellationToken);
        RecentRuns = await runner.ListRecentExecutionsAsync(RecentRunsCount, cancellationToken);
    }

    /// <summary>htmx: validates the form and shows a confirmation summary before anything is queued.</summary>
    public IActionResult OnPostReview()
    {
        return ModelState.IsValid
            ? Partial("Shared/_RunReview", Form)
            : Partial("Shared/_ValidationErrors", ModelState);
    }

    /// <summary>htmx: queues the run after the operator confirms, then forces a real navigation to its execution page.</summary>
    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Partial("Shared/_ValidationErrors", ModelState);
        }

        var jobId = await runner.SubmitAsync(Form.ToRunRequest(), cancellationToken);
        logger.LogInformation(
            "Queued Ansible run {JobId}: playbook {Playbook}, target {Target}", jobId, Form.PlaybookName, Form.Target);

        // An ordinary htmx response swaps content into #result; HX-Redirect instead makes the
        // browser navigate for real, since watching a run now lives on its own Executions page
        // rather than growing underneath this form.
        Response.Headers.Append("HX-Redirect", Url.Page("/Executions/Details", new { id = jobId }));
        return new EmptyResult();
    }
}
