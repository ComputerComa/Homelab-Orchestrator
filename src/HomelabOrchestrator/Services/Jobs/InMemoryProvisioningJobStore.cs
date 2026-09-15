using System.Collections.Concurrent;

namespace HomelabOrchestrator.Services.Jobs;

public class InMemoryProvisioningJobStore : IProvisioningJobStore
{
    private readonly ConcurrentDictionary<Guid, ProvisioningJob> _jobs = new();

    public ProvisioningJob Create(ProvisioningRequest request)
    {
        var job = new ProvisioningJob { Id = Guid.NewGuid(), Request = request };
        _jobs[job.Id] = job;
        return job;
    }

    public ProvisioningJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

    public ProvisioningJob Update(Guid id, Func<ProvisioningJob, ProvisioningJob> mutate) =>
        _jobs.AddOrUpdate(
            id,
            _ => throw new InvalidOperationException($"Provisioning job {id} does not exist."),
            (_, current) => mutate(current));
}
