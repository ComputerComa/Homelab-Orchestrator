using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Provisioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomelabOrchestrator.Pages.Reconcile;

/// <summary>
/// HTTP binding and view model only — every lookup and tag write happens behind
/// <see cref="IContainerReconciliationService"/>.
/// </summary>
public class IndexModel(IContainerReconciliationService reconciliation, ILogger<IndexModel> logger) : PageModel
{
    public IReadOnlyList<ReconciliationCandidate> Candidates { get; set; } = [];

    public AdoptionResult? Result { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Candidates = await reconciliation.ListCandidatesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAdoptAsync([FromForm] List<int> selectedVmids, CancellationToken cancellationToken)
    {
        Result = await reconciliation.AdoptAsync(selectedVmids, cancellationToken);
        logger.LogInformation(
            "Adopted containers [{Adopted}]; skipped [{Skipped}]",
            string.Join(", ", Result.Adopted), string.Join(", ", Result.Skipped));

        Candidates = await reconciliation.ListCandidatesAsync(cancellationToken);
        return Page();
    }
}
