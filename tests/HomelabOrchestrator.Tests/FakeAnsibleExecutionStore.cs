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

    public Task SaveAsync(AnsibleRunJob job, CancellationToken cancellationToken = default)
    {
        Saved.Add(job);
        _records[job.Id] = new AnsibleExecutionRecord
        {
            Id = job.Id,
            PlaybookName = job.Request.PlaybookName,
            TargetKind = job.Request.TargetKind,
            TargetValue = job.Request.TargetValue,
            Stage = job.Stage,
            CreatedAtUtc = job.CreatedAtUtc.UtcDateTime,
            CompletedAtUtc = job.CompletedAtUtc?.UtcDateTime,
            Output = job.Output,
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
}
