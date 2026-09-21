namespace HomelabOrchestrator.Pages.SshKeys;

/// <summary>
/// View model for the shared Approve/Revoke/Synchronize confirmation partial — modeled on
/// <c>Shared/_RunReview.cshtml</c>'s own two-step review-then-confirm pattern.
/// <paramref name="KeyId"/>/<paramref name="DeviceName"/>/<paramref name="Fingerprint"/> are null
/// for the Synchronize action, which isn't about any one key.
/// </summary>
public record SshKeyReviewViewModel(
    string Title,
    string WarningText,
    string ConfirmHandler,
    Guid? KeyId,
    string? DeviceName,
    string? Fingerprint);
