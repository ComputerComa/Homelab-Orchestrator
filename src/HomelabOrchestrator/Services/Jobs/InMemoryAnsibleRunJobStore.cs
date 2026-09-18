using System.Collections.Concurrent;

namespace HomelabOrchestrator.Services.Jobs;

public class InMemoryAnsibleRunJobStore : IAnsibleRunJobStore
{
    private readonly ConcurrentDictionary<Guid, AnsibleRunJob> _jobs = new();
    private readonly Lock _liveLogLock = new();
    private (Guid Id, AnsibleExecutionLogBuffer Buffer)? _liveLog;

    public AnsibleRunJob Create(AnsibleRunRequest request, string? submittedBy = null)
    {
        var job = new AnsibleRunJob { Id = Guid.NewGuid(), Request = request, SubmittedBy = submittedBy };
        _jobs[job.Id] = job;
        return job;
    }

    public AnsibleRunJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

    public AnsibleRunJob Update(Guid id, Func<AnsibleRunJob, AnsibleRunJob> mutate) =>
        _jobs.AddOrUpdate(
            id,
            _ => throw new InvalidOperationException($"Ansible run job {id} does not exist."),
            (_, current) => mutate(current));

    public void AttachLiveLog(Guid id, AnsibleExecutionLogBuffer buffer)
    {
        lock (_liveLogLock)
        {
            _liveLog = (id, buffer);
        }
    }

    public void DetachLiveLog(Guid id)
    {
        lock (_liveLogLock)
        {
            if (_liveLog?.Id == id)
            {
                _liveLog = null;
            }
        }
    }

    public string? GetLiveLogSnapshot(Guid id)
    {
        lock (_liveLogLock)
        {
            return _liveLog?.Id == id ? _liveLog.Value.Buffer.SnapshotText() : null;
        }
    }
}
