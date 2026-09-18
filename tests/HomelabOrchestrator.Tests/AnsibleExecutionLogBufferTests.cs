using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Exercises <see cref="AnsibleExecutionLogBuffer"/> in isolation — the batching/flush-timing
/// logic that replaces the old "write once per line" design.
/// </summary>
public class AnsibleExecutionLogBufferTests
{
    private static readonly Guid ExecutionId = Guid.NewGuid();

    [Fact]
    public async Task Flushes_as_soon_as_the_batch_size_is_reached_without_waiting_for_the_interval()
    {
        var logs = new FakeAnsibleExecutionLogStore();
        var buffer = new AnsibleExecutionLogBuffer(ExecutionId, logs, NullLogger.Instance, flushBatchSize: 3, flushInterval: TimeSpan.FromMinutes(10));
        var flushLoop = buffer.RunFlushLoopAsync(CancellationToken.None);

        buffer.Enqueue(AnsibleLogStream.Stdout, "one");
        buffer.Enqueue(AnsibleLogStream.Stdout, "two");
        buffer.Enqueue(AnsibleLogStream.Stdout, "three");

        await WaitUntilAsync(() => logs.AppendCallCount >= 1);

        Assert.Equal(["one", "two", "three"], logs.Appended.Select(e => e.Text));

        buffer.Complete();
        await flushLoop;
    }

    [Fact]
    public async Task Flushes_a_partial_batch_once_the_interval_elapses()
    {
        var logs = new FakeAnsibleExecutionLogStore();
        var buffer = new AnsibleExecutionLogBuffer(ExecutionId, logs, NullLogger.Instance, flushBatchSize: 40, flushInterval: TimeSpan.FromMilliseconds(50));
        var flushLoop = buffer.RunFlushLoopAsync(CancellationToken.None);

        buffer.Enqueue(AnsibleLogStream.Stdout, "lonely line");

        await WaitUntilAsync(() => logs.AppendCallCount >= 1);

        Assert.Equal(["lonely line"], logs.Appended.Select(e => e.Text));

        buffer.Complete();
        await flushLoop;
    }

    [Fact]
    public async Task Complete_guarantees_a_final_flush_of_whatever_is_still_pending()
    {
        var logs = new FakeAnsibleExecutionLogStore();
        var buffer = new AnsibleExecutionLogBuffer(ExecutionId, logs, NullLogger.Instance, flushBatchSize: 40, flushInterval: TimeSpan.FromMinutes(10));
        var flushLoop = buffer.RunFlushLoopAsync(CancellationToken.None);

        buffer.Enqueue(AnsibleLogStream.Stdout, "never reaches the batch threshold");
        buffer.Complete();
        await flushLoop;

        Assert.Equal(["never reaches the batch threshold"], logs.Appended.Select(e => e.Text));
    }

    [Fact]
    public async Task Sequence_numbers_start_at_one_and_strictly_increase()
    {
        var logs = new FakeAnsibleExecutionLogStore();
        var buffer = new AnsibleExecutionLogBuffer(ExecutionId, logs, NullLogger.Instance, flushBatchSize: 40, flushInterval: TimeSpan.FromMinutes(10));
        var flushLoop = buffer.RunFlushLoopAsync(CancellationToken.None);

        buffer.Enqueue(AnsibleLogStream.Stdout, "a");
        buffer.Enqueue(AnsibleLogStream.Stderr, "b");
        buffer.Enqueue(AnsibleLogStream.Stdout, "c");
        buffer.Complete();
        await flushLoop;

        Assert.Equal([1, 2, 3], logs.Appended.Select(e => e.Sequence));
    }

    [Fact]
    public async Task A_throwing_store_does_not_fault_the_loop_or_stop_subsequent_flushes()
    {
        var logs = new FakeAnsibleExecutionLogStore { ThrowOnNextAppend = true };
        var buffer = new AnsibleExecutionLogBuffer(ExecutionId, logs, NullLogger.Instance, flushBatchSize: 1, flushInterval: TimeSpan.FromMinutes(10));
        var flushLoop = buffer.RunFlushLoopAsync(CancellationToken.None);

        buffer.Enqueue(AnsibleLogStream.Stdout, "dropped by the simulated failure");
        await WaitUntilAsync(() => logs.AppendCallCount >= 1);

        buffer.Enqueue(AnsibleLogStream.Stdout, "survives");
        buffer.Complete();

        await flushLoop; // must not throw

        Assert.Equal(["survives"], logs.Appended.Select(e => e.Text));
    }

    [Fact]
    public void SnapshotText_reflects_every_enqueued_line_even_before_a_flush()
    {
        var logs = new FakeAnsibleExecutionLogStore();
        var buffer = new AnsibleExecutionLogBuffer(ExecutionId, logs, NullLogger.Instance, flushBatchSize: 40, flushInterval: TimeSpan.FromMinutes(10));

        buffer.Enqueue(AnsibleLogStream.Stdout, "first");
        buffer.Enqueue(AnsibleLogStream.Stdout, "second");

        Assert.Equal("first\nsecond\n", buffer.SnapshotText());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met in time.");
    }
}
