using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
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
    public IReadOnlyList<PlaybookDetail> PlaybookDetails { get; set; } = [];
    public RunTargetOptions Targets { get; set; } = new([], []);
    public IReadOnlyList<AnsibleExecutionRecord> RecentRuns { get; set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Playbooks = await runner.ListPlaybooksAsync(cancellationToken);
        PlaybookDetails = await runner.ListPlaybookDetailsAsync(cancellationToken);
        Targets = await runner.GetRunTargetOptionsAsync(cancellationToken);
        RecentRuns = await runner.ListRecentExecutionsAsync(RecentRunsCount, cancellationToken);
    }

    /// <summary>htmx: validates the form and shows a confirmation summary before anything is queued.</summary>
    public IActionResult OnPostReview()
    {
        ValidateSelectionTarget();
        return ModelState.IsValid
            ? Partial("Shared/_RunReview", Form)
            : Partial("Shared/_ValidationErrors", ModelState);
    }

    /// <summary>htmx: queues the run after the operator confirms, then forces a real navigation to its execution page.</summary>
    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        ValidateSelectionTarget();
        if (!ModelState.IsValid)
        {
            return Partial("Shared/_ValidationErrors", ModelState);
        }

        var jobId = await runner.SubmitAsync(Form.ToRunRequest(), User.Identity?.Name, cancellationToken);
        logger.LogInformation(
            "Queued Ansible run {JobId}: playbook {Playbook}, target {Target}", jobId, Form.PlaybookName, Form.Target);

        // An ordinary htmx response swaps content into #result; HX-Redirect instead makes the
        // browser navigate for real, since watching a run now lives on its own Executions page
        // rather than growing underneath this form.
        Response.Headers.Append("HX-Redirect", Url.Page("/Executions/Details", new { id = jobId }));
        return new EmptyResult();
    }

    /// <summary>
    /// [Required] on Form.Target only rejects an empty string — a "selection:" encoding with no
    /// hostnames after the colon still passes that check, so this catches it explicitly. The
    /// client-side checkbox panel already keeps the submit button disabled in this case; this is
    /// the server-side backstop for a request that bypasses that (a stale page, a crafted POST).
    /// </summary>
    private void ValidateSelectionTarget()
    {
        var (kind, value) = Form.ParseTarget();
        if (kind == AnsibleRunTargetKind.Selection && string.IsNullOrWhiteSpace(value))
        {
            ModelState.AddModelError(nameof(Form.Target), "Select at least one container.");
        }
    }
}
