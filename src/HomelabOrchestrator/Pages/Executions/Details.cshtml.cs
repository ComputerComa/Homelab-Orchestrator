using System.Text;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomelabOrchestrator.Pages.Executions;

/// <summary>
/// One run's live-or-historical view. <see cref="IAnsibleRunnerService.GetJobOrHistoryAsync"/>
/// transparently resolves either an in-flight job (still in the in-memory store — this page keeps
/// polling) or an already-finished one loaded from persisted SQLite history (renders a terminal
/// state immediately, no polling). The page itself renders once; the Tasks/Hosts panels and the
/// raw-output console each poll their own small fragment independently — see the OnGet* handlers
/// below — instead of the whole page replacing itself every second.
/// </summary>
public class DetailsModel(IAnsibleRunnerService runner) : PageModel
{
    public AnsibleRunJob? Job { get; set; }

    public AnsibleRunParsedState Parsed { get; set; } = new([], [], [], []);

    public IReadOnlyList<AnsibleExecutionLog> InitialLogEntries { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Job = await runner.GetJobOrHistoryAsync(id, cancellationToken);
        if (Job is null)
        {
            return NotFound();
        }

        Parsed = AnsibleOutputParser.Parse(await runner.GetReconstructedOutputAsync(id, cancellationToken));
        InitialLogEntries = await runner.GetLogTailAsync(id, 0, cancellationToken);
        return Page();
    }

    /// <summary>htmx: the header's status badge, timestamps, duration, exit code, and summary counts — polled independently so they update without the whole page (or the Tasks/Hosts/console fragments) re-rendering.</summary>
    public async Task<IActionResult> OnGetHeaderStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        Job = await runner.GetJobOrHistoryAsync(id, cancellationToken);
        if (Job is null)
        {
            return Partial("Shared/_Error", "This Ansible run is no longer available.");
        }

        Parsed = AnsibleOutputParser.Parse(await runner.GetReconstructedOutputAsync(id, cancellationToken));
        return Partial("Shared/_ExecutionHeaderStatus", this);
    }

    /// <summary>htmx: the Tasks tab's own small fragment, polled independently while the run is in flight.</summary>
    public async Task<IActionResult> OnGetTaskPanelAsync(Guid id, CancellationToken cancellationToken)
    {
        Job = await runner.GetJobOrHistoryAsync(id, cancellationToken);
        if (Job is null)
        {
            return Partial("Shared/_Error", "This Ansible run is no longer available.");
        }

        Parsed = AnsibleOutputParser.Parse(await runner.GetReconstructedOutputAsync(id, cancellationToken));
        return Partial("Shared/_ExecutionTaskPanel", this);
    }

    /// <summary>htmx: the Hosts tab's own small fragment, polled independently while the run is in flight.</summary>
    public async Task<IActionResult> OnGetHostPanelAsync(Guid id, CancellationToken cancellationToken)
    {
        Job = await runner.GetJobOrHistoryAsync(id, cancellationToken);
        if (Job is null)
        {
            return Partial("Shared/_Error", "This Ansible run is no longer available.");
        }

        Parsed = AnsibleOutputParser.Parse(await runner.GetReconstructedOutputAsync(id, cancellationToken));
        return Partial("Shared/_ExecutionHostPanel", this);
    }

    /// <summary>
    /// htmx: the raw console's cursor-based tail. Only entries newer than <paramref name="after"/>
    /// come back, and the response appends (its trailing poll trigger swaps itself out with the
    /// new lines plus the next trigger) rather than the console ever being replaced wholesale.
    /// </summary>
    public async Task<IActionResult> OnGetLogTailAsync(Guid id, long after, CancellationToken cancellationToken)
    {
        var job = await runner.GetJobOrHistoryAsync(id, cancellationToken);
        if (job is null)
        {
            return new EmptyResult();
        }

        var entries = await runner.GetLogTailAsync(id, after, cancellationToken);
        return Partial("Shared/_ExecutionLogTail", new ExecutionLogTailViewModel(job, entries, after));
    }

    /// <summary>The full reconstructed log as a plain-text download — never the filtered/searched console view.</summary>
    public async Task<IActionResult> OnGetDownloadLogAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await runner.GetJobOrHistoryAsync(id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var text = await runner.GetReconstructedOutputAsync(id, cancellationToken);
        return File(Encoding.UTF8.GetBytes(text), "text/plain", $"{job.Request.PlaybookName}-{job.Id}.log");
    }

    /// <summary>htmx: resubmits this run's original request as a brand-new execution, then forces navigation to its page.</summary>
    public async Task<IActionResult> OnPostRunAgainAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await runner.GetJobOrHistoryAsync(id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var newId = await runner.SubmitAsync(job.Request, User.Identity?.Name, cancellationToken);
        Response.Headers.Append("HX-Redirect", Url.Page("/Executions/Details", new { id = newId }));
        return new EmptyResult();
    }

    public static string FormatDuration(AnsibleRunJob job)
    {
        if (job.StartedAtUtc is not { } started)
        {
            return "-";
        }

        var elapsed = (job.CompletedAtUtc ?? DateTimeOffset.UtcNow) - started;
        return elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
    }
}

/// <summary>View model for <c>_ExecutionLogTail</c> — the currently-live-log poll's new entries plus the cursor it was fetched with, so the next self-perpetuating poll trigger can compute where to resume if nothing new arrived.</summary>
public record ExecutionLogTailViewModel(AnsibleRunJob Job, IReadOnlyList<AnsibleExecutionLog> Entries, long AfterSequence);
