using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;

namespace HomelabOrchestrator.Tests;

/// <summary>In-memory stand-in for <see cref="IAnsibleExecutionLogStore"/>, shared by worker/runner-service/log-buffer tests.</summary>
public class FakeAnsibleExecutionLogStore : IAnsibleExecutionLogStore
{
    private readonly List<AnsibleExecutionLog> _entries = [];

    /// <summary>Every entry ever passed to <see cref="AppendAsync"/>, across every call, in call order.</summary>
    public List<AnsibleExecutionLog> Appended { get; } = [];

    /// <summary>How many times <see cref="AppendAsync"/> was called (as opposed to how many entries it received in total).</summary>
    public int AppendCallCount { get; private set; }

    /// <summary>When true, the next <see cref="AppendAsync"/> call throws instead of persisting — for exercising "a log-persistence hiccup must never fail the run" behavior.</summary>
    public bool ThrowOnNextAppend { get; set; }

    public Task AppendAsync(Guid executionId, IReadOnlyList<AnsibleExecutionLog> entries, CancellationToken cancellationToken = default)
    {
        AppendCallCount++;
        if (ThrowOnNextAppend)
        {
            ThrowOnNextAppend = false;
            throw new InvalidOperationException("Simulated log store failure.");
        }

        _entries.AddRange(entries);
        Appended.AddRange(entries);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AnsibleExecutionLog>> ListAfterAsync(Guid executionId, long afterSequence, int? limit = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<AnsibleExecutionLog> results = _entries
            .Where(e => e.ExecutionId == executionId && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence);

        if (limit is { } take)
        {
            results = results.Take(take);
        }

        return Task.FromResult<IReadOnlyList<AnsibleExecutionLog>>(results.ToList());
    }
}
