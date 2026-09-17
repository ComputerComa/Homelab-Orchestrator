using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomelabOrchestrator.Pages.Executions;

/// <summary>The Ansible job history list — HTTP binding and view models only, per AGENTS.md's Boundaries.</summary>
public class IndexModel(IAnsibleRunnerService runner) : PageModel
{
    public const int Limit = 100;

    public IReadOnlyList<AnsibleExecutionRecord> Executions { get; set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Executions = await runner.ListRecentExecutionsAsync(Limit, cancellationToken);
    }
}
