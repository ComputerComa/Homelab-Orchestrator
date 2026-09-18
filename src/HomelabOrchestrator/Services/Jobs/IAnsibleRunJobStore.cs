namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Holds Ansible run job state. In-memory for now, same as <see cref="IProvisioningJobStore"/>:
/// a process restart drops in-flight jobs until persistent job recovery is introduced.
/// </summary>
public interface IAnsibleRunJobStore
{
    AnsibleRunJob Create(AnsibleRunRequest request, string? submittedBy = null);

    AnsibleRunJob? Get(Guid id);

    /// <summary>Applies <paramref name="mutate"/> to the current snapshot and stores the result.</summary>
    AnsibleRunJob Update(Guid id, Func<AnsibleRunJob, AnsibleRunJob> mutate);

    /// <summary>
    /// Registers the currently-live run's output buffer so <see cref="GetLiveLogSnapshot"/> can
    /// serve its text without a DB round trip. Only one execution is ever live at a time (the
    /// worker's queue is read by a single sequential loop), so this holds at most one buffer, not
    /// a per-execution registry.
    /// </summary>
    void AttachLiveLog(Guid id, AnsibleExecutionLogBuffer buffer);

    /// <summary>Clears the live buffer registered for <paramref name="id"/>, if it's still the current one.</summary>
    void DetachLiveLog(Guid id);

    /// <summary>The live buffer's captured text so far for <paramref name="id"/>, or null if that run isn't currently live.</summary>
    string? GetLiveLogSnapshot(Guid id);
}
