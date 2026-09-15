namespace HomelabOrchestrator.Services.Jobs;

/// <summary>Hands job IDs from the web request that created them to the background worker that executes them.</summary>
public interface IProvisioningJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}
