using System.Threading.Channels;

namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Unbounded-but-simple channel of pending job IDs. A single background worker reads this with
/// one sequential loop, which is what actually serializes execution: only one ansible-playbook
/// invocation runs at a time — see <see cref="AnsibleRunWorker"/>.
/// </summary>
public class AnsibleRunJobQueue : IAnsibleRunJobQueue
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
