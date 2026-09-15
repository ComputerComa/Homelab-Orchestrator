namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Holds provisioning job state. The in-memory implementation is intentional for now: a process
/// restart drops in-flight jobs (see AGENTS.md "Jobs and concurrency") until persistent job
/// recovery is introduced.
/// </summary>
public interface IProvisioningJobStore
{
    ProvisioningJob Create(ProvisioningRequest request);

    ProvisioningJob? Get(Guid id);

    /// <summary>Applies <paramref name="mutate"/> to the current snapshot and stores the result.</summary>
    ProvisioningJob Update(Guid id, Func<ProvisioningJob, ProvisioningJob> mutate);
}
