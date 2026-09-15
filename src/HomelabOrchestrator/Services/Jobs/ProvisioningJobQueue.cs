using System.Threading.Channels;

namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Unbounded-but-simple channel of pending job IDs. A single background worker reads this with
/// one sequential loop, which is what actually serializes VMID/address allocation — see
/// <see cref="ProvisioningWorker"/>.
/// </summary>
public class ProvisioningJobQueue : IProvisioningJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
