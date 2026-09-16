namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Holds Ansible run job state. In-memory for now, same as <see cref="IProvisioningJobStore"/>:
/// a process restart drops in-flight jobs until persistent job recovery is introduced.
/// </summary>
public interface IAnsibleRunJobStore
{
    AnsibleRunJob Create(AnsibleRunRequest request);

    AnsibleRunJob? Get(Guid id);

    /// <summary>Applies <paramref name="mutate"/> to the current snapshot and stores the result.</summary>
    AnsibleRunJob Update(Guid id, Func<AnsibleRunJob, AnsibleRunJob> mutate);
}
