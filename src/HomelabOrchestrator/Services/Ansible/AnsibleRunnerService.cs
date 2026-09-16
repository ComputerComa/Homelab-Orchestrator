using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Proxmox;

namespace HomelabOrchestrator.Services.Ansible;

public class AnsibleRunnerService(
    IPlaybookCatalog catalog,
    IProxmoxService proxmox,
    IAnsibleRunJobStore store,
    IAnsibleRunJobQueue queue) : IAnsibleRunnerService
{
    public Task<IReadOnlyList<PlaybookSummary>> ListPlaybooksAsync(CancellationToken cancellationToken = default) =>
        catalog.ListAsync(cancellationToken);

    public async Task<RunTargetOptions> GetRunTargetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var running = (await proxmox.ListContainersAsync(cancellationToken))
            .Where(c => c.IsRunning && !OrchestratorSelfFilter.IsSelf(c))
            .ToList();

        var hostnames = running
            .Select(c => c.Hostname)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();

        var tags = running
            .SelectMany(c => c.Tags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return new RunTargetOptions(hostnames, tags);
    }

    public async Task<Guid> SubmitAsync(AnsibleRunRequest request, CancellationToken cancellationToken = default)
    {
        var job = store.Create(request);
        await queue.EnqueueAsync(job.Id, cancellationToken);
        return job.Id;
    }

    public AnsibleRunJob? GetJob(Guid jobId) => store.Get(jobId);
}
