namespace HomelabOrchestrator.Services.Ansible;

/// <summary>The only application boundary that spawns ansible-playbook.</summary>
public interface IAnsibleProcessRunner
{
    /// <summary>
    /// Runs ansible-playbook against <paramref name="playbookPath"/>, restricted to
    /// <paramref name="limit"/> when given (an Ansible host pattern — a hostname or group name;
    /// null runs against the whole inventory). Streams each combined stdout/stderr line to
    /// <paramref name="onOutputLine"/> as it's produced and returns the process exit code.
    /// </summary>
    Task<int> RunPlaybookAsync(
        string playbookPath,
        string? limit,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default);
}
