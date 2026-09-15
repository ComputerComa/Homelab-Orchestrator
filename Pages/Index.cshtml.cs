using System.Text.RegularExpressions;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Pages;

public partial class IndexModel(IProxmoxService proxmox, IOptions<ProxmoxOptions> options, ILogger<IndexModel> logger) : PageModel
{
    // Lowercase letters, digits, and hyphens; must start and end with a letter or digit.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex HostnamePattern();

    private readonly ProxmoxOptions _options = options.Value;

    [BindProperty]
    public ContainerFormModel Form { get; set; } = new();

    public ClusterPlacement Connection { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            Connection = await proxmox.GetConnectionInfoAsync();
            Form = ContainerFormModel.FromDefaults(_options, Connection, ReadDefaultSshKey());
            return Page();
        }
        catch (ProxmoxOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    /// <summary>htmx: refreshes the VMID/address/template panel without resubmitting the form.</summary>
    public async Task<IActionResult> OnGetRefreshAsync()
    {
        var connection = await proxmox.GetConnectionInfoAsync();
        return Partial("Shared/_ConnectionInfo", connection);
    }

    /// <summary>htmx: validates the form and shows a confirmation summary before creating anything.</summary>
    public IActionResult OnPostReview()
    {
        NormalizeAndValidateHostname();

        return ModelState.IsValid
            ? Partial("Shared/_Review", Form)
            : Partial("Shared/_ValidationErrors", ModelState);
    }

    /// <summary>htmx: creates the container after the user confirms the review step.</summary>
    public async Task<IActionResult> OnPostCreateAsync()
    {
        NormalizeAndValidateHostname();

        if (!ModelState.IsValid)
        {
            return Partial("Shared/_ValidationErrors", ModelState);
        }

        try
        {
            var created = await proxmox.CreateContainerAsync(Form.ToContainerRequest());
            return Partial("Shared/_Result", created);
        }
        catch (ProxmoxOperationException ex)
        {
            logger.LogWarning(ex, "Failed to create container {Vmid}", Form.Vmid);
            return Partial("Shared/_Error", ex.Message);
        }
    }

    private void NormalizeAndValidateHostname()
    {
        Form.Hostname = Form.Hostname.Trim().ToLowerInvariant();

        if (!HostnamePattern().IsMatch(Form.Hostname))
        {
            ModelState.AddModelError(nameof(Form.Hostname), "Use lowercase letters, numbers, and hyphens.");
        }
    }

    private string ReadDefaultSshKey()
    {
        var path = _options.DefaultSshPublicKeyPath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return "";
        }

        try
        {
            return System.IO.File.ReadAllText(path).Trim();
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not read default SSH key at {Path}", path);
            return "";
        }
    }
}
