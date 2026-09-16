using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Provisioning;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Pages.Provision;

/// <summary>
/// HTTP binding, validation, and view models only — every actual Proxmox call and the
/// long-running creation itself happen behind <see cref="IProvisioningService"/>.
/// </summary>
public class IndexModel(
    IProvisioningService provisioning,
    IOptions<ProxmoxOptions> options,
    IOptions<SshOptions> sshOptions,
    ILogger<IndexModel> logger) : PageModel
{
    private readonly ProxmoxOptions _options = options.Value;

    [BindProperty]
    public ContainerFormModel Form { get; set; } = new();

    public ClusterPlacement Placement { get; set; } = new();

    /// <summary>Display only — the actual keys are read fresh by the worker at creation time.</summary>
    public string AuthorizedKeysPath => sshOptions.Value.AuthorizedKeysPath;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Placement = await provisioning.GetPlacementPreviewAsync(cancellationToken);
            Form = ContainerFormModel.FromDefaults(_options, Placement);
            return Page();
        }
        catch (ProxmoxOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    /// <summary>htmx: refreshes the VMID/address/template panel without resubmitting the form.</summary>
    public async Task<IActionResult> OnGetRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var placement = await provisioning.GetPlacementPreviewAsync(cancellationToken);
            return Partial("Shared/_ConnectionInfo", placement);
        }
        catch (ProxmoxOperationException ex)
        {
            return Partial("Shared/_Error", ex.Message);
        }
    }

    /// <summary>htmx: validates the form and shows a confirmation summary before anything is queued.</summary>
    public IActionResult OnPostReview()
    {
        NormalizeAndValidateHostname();

        return ModelState.IsValid
            ? Partial("Shared/_Review", Form)
            : Partial("Shared/_ValidationErrors", ModelState);
    }

    /// <summary>htmx: queues the provisioning job after the operator confirms, and returns the initial status panel.</summary>
    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        NormalizeAndValidateHostname();

        if (!ModelState.IsValid)
        {
            return Partial("Shared/_ValidationErrors", ModelState);
        }

        var jobId = await provisioning.SubmitAsync(Form.ToProvisioningRequest(), cancellationToken);
        logger.LogInformation("Queued provisioning job {JobId} for hostname {Hostname}", jobId, Form.Hostname);

        return Partial("Shared/_JobStatus", provisioning.GetJob(jobId));
    }

    /// <summary>htmx: polled every second while a job is in flight; stops once the job reaches a terminal stage.</summary>
    public IActionResult OnGetJobStatus(Guid jobId)
    {
        var job = provisioning.GetJob(jobId);
        return job is null
            ? Partial("Shared/_Error", "This provisioning job is no longer available.")
            : Partial("Shared/_JobStatus", job);
    }

    private void NormalizeAndValidateHostname()
    {
        Form.Hostname = HostnamePolicy.Normalize(Form.Hostname);

        if (!HostnamePolicy.IsValid(Form.Hostname))
        {
            ModelState.AddModelError(nameof(Form.Hostname), "Use lowercase letters, numbers, and hyphens.");
        }
    }
}
