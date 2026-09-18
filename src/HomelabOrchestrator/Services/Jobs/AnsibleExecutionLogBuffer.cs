using System.Text;
using System.Threading.Channels;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;

namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Buffers one run's captured output in memory and flushes it to <see cref="IAnsibleExecutionLogStore"/>
/// in batches — every <paramref name="flushBatchSize"/> lines or <paramref name="flushInterval"/>,
/// whichever comes first — instead of writing a row per line. Scoped to a single run: since
/// <see cref="AnsibleRunWorker"/>'s queue is read by one sequential loop, only one execution is
/// ever live at a time, so this is a plain object <c>new</c>'d for the duration of one
/// <c>ProcessAsync</c> call, not a DI singleton or a per-execution registry.
/// </summary>
public sealed class AnsibleExecutionLogBuffer(
    Guid executionId,
    IAnsibleExecutionLogStore store,
    ILogger logger,
    int flushBatchSize = 40,
    TimeSpan? flushInterval = null)
{
    private readonly TimeSpan _flushInterval = flushInterval ?? TimeSpan.FromMilliseconds(300);
    private readonly Channel<AnsibleExecutionLog> _channel = Channel.CreateUnbounded<AnsibleExecutionLog>(new UnboundedChannelOptions { SingleReader = true });
    private readonly StringBuilder _liveText = new();
    private readonly Lock _textLock = new();
    private long _nextSequence = 1;

    /// <summary>
    /// Called synchronously from the process runner's stdout/stderr callbacks, which can fire from
    /// either pipe-reader thread concurrently — hence the lock around the shared <see cref="StringBuilder"/>.
    /// </summary>
    public void Enqueue(AnsibleLogStream stream, string text)
    {
        var entry = new AnsibleExecutionLog
        {
            ExecutionId = executionId,
            Sequence = Interlocked.Increment(ref _nextSequence) - 1,
            TimestampUtc = DateTime.UtcNow,
            Stream = stream,
            Text = text,
        };

        lock (_textLock)
        {
            _liveText.Append(text).Append('\n');
        }

        // Unbounded channel: TryWrite never fails except after Complete(), which this type never
        // calls before the process has finished producing output.
        _channel.Writer.TryWrite(entry);
    }

    /// <summary>The full text captured so far — lets a live poll build the Tasks/Hosts views without a DB round trip.</summary>
    public string SnapshotText()
    {
        lock (_textLock)
        {
            return _liveText.ToString();
        }
    }

    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>
    /// Drains the channel, flushing whenever <paramref name="flushBatchSize"/> lines accumulate or
    /// <paramref name="flushInterval"/> elapses, whichever first. Always awaited by the caller
    /// (never fire-and-forgotten). Termination is driven solely by <see cref="Complete"/> — the
    /// reads below deliberately never observe <paramref name="cancellationToken"/>, so an
    /// application-shutdown cancellation can never cut this loop short before its own
    /// <c>finally</c> guarantees every remaining entry is flushed (with
    /// <see cref="CancellationToken.None"/>) no matter how the loop exits.
    /// </summary>
    public async Task RunFlushLoopAsync(CancellationToken cancellationToken)
    {
        var pending = new List<AnsibleExecutionLog>(flushBatchSize);
        var reader = _channel.Reader;
        try
        {
            while (true)
            {
                var waitTask = reader.WaitToReadAsync(CancellationToken.None).AsTask();

                // A pending partial batch races the interval deadline against more data arriving
                // — if data wins, fall through and drain it (below) before ever flushing, so a
                // burst of writes that lands just after this wait started still gets batched
                // instead of each arrival triggering its own tiny flush.
                if (pending.Count > 0)
                {
                    using var deadlineCts = new CancellationTokenSource();
                    var deadlineTask = Task.Delay(_flushInterval, deadlineCts.Token);
                    var completed = await Task.WhenAny(waitTask, deadlineTask);
                    if (completed == deadlineTask)
                    {
                        await FlushAsync(pending, cancellationToken);
                        continue;
                    }

                    deadlineCts.Cancel(); // waitTask won — the delay timer is no longer needed
                }

                bool more;
                try
                {
                    more = await waitTask;
                }
                catch (OperationCanceledException)
                {
                    more = false;
                }

                if (!more)
                {
                    break;
                }

                while (reader.TryRead(out var entry))
                {
                    pending.Add(entry);
                    if (pending.Count >= flushBatchSize)
                    {
                        await FlushAsync(pending, cancellationToken);
                    }
                }
            }
        }
        finally
        {
            while (reader.TryRead(out var entry))
            {
                pending.Add(entry);
            }

            if (pending.Count > 0)
            {
                await FlushAsync(pending, CancellationToken.None);
            }
        }
    }

    private async Task FlushAsync(List<AnsibleExecutionLog> pending, CancellationToken cancellationToken)
    {
        var batch = pending.ToArray();
        pending.Clear();

        try
        {
            await store.AppendAsync(executionId, batch, cancellationToken);
        }
        catch (Exception ex)
        {
            // A log-persistence hiccup must never fail the run itself — see AnsibleRunWorker.
            logger.LogError(ex, "Failed to persist {Count} log lines for execution {ExecutionId}", batch.Length, executionId);
        }
    }
}
