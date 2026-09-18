using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Durable, append-only storage for one execution's captured output lines, keyed by a strictly
/// increasing per-execution <see cref="AnsibleExecutionLog.Sequence"/>. Written in batches by
/// <see cref="Jobs.AnsibleExecutionLogBuffer"/>, never one row per line. <see cref="ListAfterAsync"/>
/// is the cursor query the live-log endpoint polls with — it only ever returns rows newer than the
/// caller's cursor, never the whole run's output.
/// </summary>
public interface IAnsibleExecutionLogStore
{
    Task AppendAsync(Guid executionId, IReadOnlyList<AnsibleExecutionLog> entries, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnsibleExecutionLog>> ListAfterAsync(Guid executionId, long afterSequence, int? limit = null, CancellationToken cancellationToken = default);
}
