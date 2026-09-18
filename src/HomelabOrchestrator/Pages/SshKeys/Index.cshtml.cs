using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Pages.SshKeys;

/// <summary>
/// HTTP binding, validation, and view models only — every rule (approval-state transitions,
/// token issuance, algorithm allowlisting, the sync hand-off) lives behind
/// <see cref="ISshKeyManagementService"/>. Approve, revoke, and synchronize all use the same
/// review-then-confirm pattern as the Ansible Runner page: the operator sees an explicit warning
/// and the exact key/target before anything actually changes.
/// </summary>
public class IndexModel(ISshKeyManagementService keyManagement, IOptions<SshSyncOptions> syncOptions, ILogger<IndexModel> logger) : PageModel
{
    public IReadOnlyList<SshPublicKey> PendingKeys { get; set; } = [];
    public IReadOnlyList<SshPublicKey> EnabledKeys { get; set; } = [];
    public IReadOnlyList<SshPublicKey> RevokedKeys { get; set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadKeysAsync(cancellationToken);

    public async Task<IActionResult> OnPostApproveReviewAsync(Guid keyId, CancellationToken cancellationToken)
    {
        var key = await FindKeyAsync(keyId, cancellationToken);
        if (key is null)
        {
            return KeyNotFound();
        }

        return Partial("Shared/_SshKeyReview", new SshKeyReviewViewModel(
            "Approve this key?",
            "Approving this key grants SSH access to this device once you synchronize. Confirm the fingerprint above matches what the device reported before approving.",
            "ApproveConfirm",
            key.Id,
            key.DeviceName,
            key.Fingerprint));
    }

    public async Task<IActionResult> OnPostApproveConfirmAsync(Guid keyId, CancellationToken cancellationToken)
    {
        await keyManagement.ApproveAsync(keyId, User.Identity?.Name, cancellationToken);
        logger.LogInformation("Approved SSH key {KeyId}", keyId);
        return RedirectAfterMutation();
    }

    public async Task<IActionResult> OnPostRevokeReviewAsync(Guid keyId, CancellationToken cancellationToken)
    {
        var key = await FindKeyAsync(keyId, cancellationToken);
        if (key is null)
        {
            return KeyNotFound();
        }

        return Partial("Shared/_SshKeyReview", new SshKeyReviewViewModel(
            "Revoke this key?",
            "Revoking removes this key from the next synchronization. Access is not removed until you synchronize.",
            "RevokeConfirm",
            key.Id,
            key.DeviceName,
            key.Fingerprint));
    }

    public async Task<IActionResult> OnPostRevokeConfirmAsync(Guid keyId, CancellationToken cancellationToken)
    {
        await keyManagement.RevokeAsync(keyId, User.Identity?.Name, cancellationToken);
        logger.LogInformation("Revoked SSH key {KeyId}", keyId);
        return RedirectAfterMutation();
    }

    /// <summary>Never had access, so unlike approve/revoke/sync this needs no confirmation step.</summary>
    public async Task<IActionResult> OnPostDeletePendingAsync(Guid keyId, CancellationToken cancellationToken)
    {
        await keyManagement.DeletePendingAsync(keyId, cancellationToken);
        logger.LogInformation("Deleted pending SSH key {KeyId}", keyId);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSyncReviewAsync(CancellationToken cancellationToken)
    {
        await LoadKeysAsync(cancellationToken);
        var warning = syncOptions.Value.Exclusive
            ? $"Synchronizing pushes the current {EnabledKeys.Count} enabled key(s) to every managed container now. Exclusive mode is on: this also removes any key on each container that is not present in this registry."
            : $"Synchronizing pushes the current {EnabledKeys.Count} enabled key(s) to every managed container now. Keys already on a container that aren't in this registry are left in place.";

        return Partial("Shared/_SshKeyReview", new SshKeyReviewViewModel(
            "Synchronize now?", warning, "SyncConfirm", null, null, null));
    }

    public async Task<IActionResult> OnPostSyncConfirmAsync(CancellationToken cancellationToken)
    {
        Guid executionId;
        try
        {
            executionId = await keyManagement.SyncAsync(User.Identity?.Name, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // The mandatory lockout guard: SyncAsync refuses to submit anything if the
            // orchestrator's own key can't be read or parsed. Surface that as a clear banner
            // instead of a raw 500 — this is an expected, actionable operator-facing condition.
            logger.LogWarning(ex, "Refused to queue SSH key synchronization");
            ModelState.AddModelError(string.Empty, ex.Message);
            return Partial("Shared/_ValidationErrors", ModelState);
        }

        logger.LogInformation("Queued SSH key synchronization {ExecutionId}", executionId);

        // Same "force a real navigation" trick Runner's OnPostRunAsync uses — a sync is a normal,
        // persisted Ansible execution with live output, so it belongs on its own Executions page.
        Response.Headers.Append("HX-Redirect", Url.Page("/Executions/Details", new { id = executionId }));
        return new EmptyResult();
    }

    public async Task<IActionResult> OnPostCreateTokenAsync(CancellationToken cancellationToken)
    {
        var token = await keyManagement.CreateEnrollmentTokenAsync(User.Identity?.Name, cancellationToken);
        logger.LogInformation("Created a new SSH enrollment token");
        return Partial("Shared/_SshEnrollmentToken", token);
    }

    private async Task LoadKeysAsync(CancellationToken cancellationToken)
    {
        var keys = await keyManagement.ListKeysAsync(cancellationToken);
        PendingKeys = keys.Where(k => k.Status == SshKeyStatus.Pending).OrderByDescending(k => k.CreatedAtUtc).ToList();
        EnabledKeys = keys.Where(k => k.Status == SshKeyStatus.Enabled).OrderBy(k => k.DeviceName, StringComparer.Ordinal).ToList();
        RevokedKeys = keys.Where(k => k.Status == SshKeyStatus.Revoked).OrderByDescending(k => k.RevokedAtUtc).ToList();
    }

    private async Task<SshPublicKey?> FindKeyAsync(Guid keyId, CancellationToken cancellationToken) =>
        (await keyManagement.ListKeysAsync(cancellationToken)).FirstOrDefault(k => k.Id == keyId);

    private IActionResult KeyNotFound()
    {
        ModelState.AddModelError(string.Empty, "That key no longer exists — the page may be out of date. Refresh and try again.");
        return Partial("Shared/_ValidationErrors", ModelState);
    }

    /// <summary>Forces a real navigation back to this page so the freshly mutated Pending/Enabled/Revoked tables render — an htmx partial swap can't refresh all three at once.</summary>
    private IActionResult RedirectAfterMutation()
    {
        Response.Headers.Append("HX-Redirect", Url.Page("/SshKeys/Index")!);
        return new EmptyResult();
    }
}
