using System.Text.Json;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Tests;

/// <summary>In-memory stand-in for <see cref="IAnsibleExecutionStore"/>, shared by AnsibleRunnerService/AnsibleRunWorker tests.</summary>
public class FakeAnsibleExecutionStore : IAnsibleExecutionStore
{
    private readonly Dictionary<Guid, AnsibleExecutionRecord> _records = [];

    /// <summary>Every job passed to <see cref="SaveAsync"/>, in call order — lets a test assert exactly when saves happen.</summary>
    public List<AnsibleRunJob> Saved { get; } = [];

    /// <summary>
    /// When set, the <see cref="SaveAsync"/> call at this 1-based call number throws instead of
    /// persisting (e.g. 2 = the terminal save, since a run's first save is always the Running
    /// transition) — for exercising "a persistence failure never corrupts an already-determined
    /// outcome" behavior.
    /// </summary>
    public int? ThrowOnSaveNumber { get; set; }

    private int _saveCallCount;

    public Task SaveAsync(AnsibleRunJob job, CancellationToken cancellationToken = default)
    {
        _saveCallCount++;
        if (_saveCallCount == ThrowOnSaveNumber)
        {
            throw new InvalidOperationException("Simulated execution store failure.");
        }

        Saved.Add(job);
        _records[job.Id] = new AnsibleExecutionRecord
        {
            Id = job.Id,
            PlaybookName = job.Request.PlaybookName,
            TargetKind = job.Request.TargetKind,
            TargetValue = job.Request.TargetValue,
            Stage = job.Stage,
            SubmittedBy = job.SubmittedBy,
            CreatedAtUtc = job.CreatedAtUtc.UtcDateTime,
            StartedAtUtc = job.StartedAtUtc?.UtcDateTime,
            CompletedAtUtc = job.CompletedAtUtc?.UtcDateTime,
            ResolvedTargetHostnamesJson = job.ResolvedTargetHostnames is { } hosts ? JsonSerializer.Serialize(hosts) : null,
            ExitCode = job.ExitCode,
            Error = job.Error,
        };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AnsibleExecutionRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AnsibleExecutionRecord>>(
            _records.Values.OrderByDescending(r => r.CreatedAtUtc).Take(limit).ToList());

    public Task<AnsibleExecutionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.GetValueOrDefault(id));

    public Task<int> InterruptStuckExecutionsAsync(CancellationToken cancellationToken = default)
    {
        var stuck = _records.Values.Where(r => r.Stage is AnsibleRunStage.Queued or AnsibleRunStage.Running).ToList();
        foreach (var record in stuck)
        {
            record.Stage = AnsibleRunStage.Interrupted;
            record.CompletedAtUtc ??= DateTime.UtcNow;
            record.Error = "The orchestrator restarted while this run was in progress.";
        }

        return Task.FromResult(stuck.Count);
    }
}
