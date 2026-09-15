namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// A snapshot of one provisioning job's state. Instances are immutable; the store replaces
/// the dictionary entry with a new snapshot on every transition rather than mutating one in place.
/// </summary>
public record ProvisioningJob
{
    public required Guid Id { get; init; }
    public required ProvisioningRequest Request { get; init; }
    public ProvisioningStage Stage { get; init; } = ProvisioningStage.Queued;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; init; }

    public int? Vmid { get; init; }
    public string? IpAddress { get; init; }
    public string? Template { get; init; }

    /// <summary>Sanitized failure message, safe to render — never the raw exception or a secret.</summary>
    public string? Error { get; init; }

    public bool IsFinished => Stage is ProvisioningStage.Succeeded or ProvisioningStage.Failed;
}
