using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Proxmox;

namespace HomelabOrchestrator.Services.Ansible;

public class AnsibleRunnerService(
    IPlaybookCatalog catalog,
    IProxmoxService proxmox,
    IAnsibleRunJobStore store,
    IAnsibleRunJobQueue queue,
    IAnsibleExecutionStore executions) : IAnsibleRunnerService
{
    public Task<IReadOnlyList<PlaybookSummary>> ListPlaybooksAsync(CancellationToken cancellationToken = default) =>
        catalog.ListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlaybookDetail>> ListPlaybookDetailsAsync(CancellationToken cancellationToken = default)
    {
        var playbooks = await catalog.ListAsync(cancellationToken);
        var details = new List<PlaybookDetail>();
        foreach (var playbook in playbooks)
        {
            var detail = await catalog.GetDetailAsync(playbook.Name, cancellationToken);
            if (detail is not null)
            {
                details.Add(detail);
            }
        }

        return details;
    }

    public async Task<RunTargetOptions> GetRunTargetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var running = (await proxmox.ListContainersAsync(cancellationToken))
            .Where(c => c.IsRunning && !OrchestratorSelfFilter.IsSelf(c) && !string.IsNullOrWhiteSpace(c.Hostname))
            .OrderBy(c => c.Hostname, StringComparer.Ordinal)
            .ToList();

        var tags = running
            .SelectMany(c => c.Tags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return new RunTargetOptions(running, tags);
    }

    public async Task<Guid> SubmitAsync(AnsibleRunRequest request, CancellationToken cancellationToken = default)
    {
        var job = store.Create(request);
        await executions.SaveAsync(job, cancellationToken);
        await queue.EnqueueAsync(job.Id, cancellationToken);
        return job.Id;
    }

    public async Task<AnsibleRunJob?> GetJobOrHistoryAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var live = store.Get(jobId);
        if (live is not null)
        {
            return live;
        }

        var record = await executions.GetAsync(jobId, cancellationToken);
        return record is null ? null : ToJob(record);
    }

    public Task<IReadOnlyList<AnsibleExecutionRecord>> ListRecentExecutionsAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        executions.ListRecentAsync(limit, cancellationToken);

    private static AnsibleRunJob ToJob(AnsibleExecutionRecord record) => new()
    {
        Id = record.Id,
        Request = new AnsibleRunRequest(record.PlaybookName, record.TargetKind, record.TargetValue),
        Stage = record.Stage,
        CreatedAtUtc = new DateTimeOffset(record.CreatedAtUtc, TimeSpan.Zero),
        CompletedAtUtc = record.CompletedAtUtc is { } completed ? new DateTimeOffset(completed, TimeSpan.Zero) : null,
        Output = record.Output,
        ExitCode = record.ExitCode,
        Error = record.Error,
    };
}
