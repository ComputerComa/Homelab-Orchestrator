using System.Collections.Concurrent;

namespace HomelabOrchestrator.Services.Jobs;

public class InMemoryAnsibleRunJobStore : IAnsibleRunJobStore
{
    private readonly ConcurrentDictionary<Guid, AnsibleRunJob> _jobs = new();

    public AnsibleRunJob Create(AnsibleRunRequest request)
    {
        var job = new AnsibleRunJob { Id = Guid.NewGuid(), Request = request };
        _jobs[job.Id] = job;
        return job;
    }

    public AnsibleRunJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

    public AnsibleRunJob Update(Guid id, Func<AnsibleRunJob, AnsibleRunJob> mutate) =>
        _jobs.AddOrUpdate(
            id,
            _ => throw new InvalidOperationException($"Ansible run job {id} does not exist."),
            (_, current) => mutate(current));
}
