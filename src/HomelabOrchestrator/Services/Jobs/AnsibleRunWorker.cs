using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Proxmox;

namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// The single background worker that actually runs ansible-playbook. Reading the queue with one
/// sequential <c>await foreach</c> loop serializes execution — only one playbook run happens at a
/// time, so two runs can never step on the same hosts concurrently. The playbook and target are
/// re-resolved here, immediately before running: a browser selection is never trusted as-is.
/// </summary>
public class AnsibleRunWorker(
    IAnsibleRunJobQueue queue,
    IAnsibleRunJobStore store,
    IPlaybookCatalog catalog,
    IProxmoxService proxmox,
    IAnsibleProcessRunner processRunner,
    IAnsibleExecutionStore executions,
    ILogger<AnsibleRunWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in queue.DequeueAllAsync(stoppingToken))
        {
            await ProcessAsync(jobId, stoppingToken);
        }
    }

    private async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = store.Get(jobId);
        if (job is null)
        {
            return;
        }

        try
        {
            var running = store.Update(jobId, j => j with { Stage = AnsibleRunStage.Running });
            await executions.SaveAsync(running, cancellationToken);

            var playbookPath = await catalog.ResolvePathAsync(job.Request.PlaybookName, cancellationToken);
            if (playbookPath is null)
            {
                throw new AnsibleRunException($"Playbook '{job.Request.PlaybookName}' is no longer available.");
            }

            var limit = await ResolveLimitAsync(job.Request, cancellationToken);

            var exitCode = await processRunner.RunPlaybookAsync(
                playbookPath,
                limit,
                line => store.Update(jobId, j => j with { Output = j.Output + line + "\n" }),
                cancellationToken);

            var finished = store.Update(jobId, j => j with
            {
                Stage = exitCode == 0 ? AnsibleRunStage.Succeeded : AnsibleRunStage.Failed,
                ExitCode = exitCode,
                Error = exitCode == 0 ? null : $"ansible-playbook exited with code {exitCode}.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });

            // CancellationToken.None: a run that finishes right as the app is shutting down should
            // still get its history entry written, not lose it to the worker's stopping token.
            await executions.SaveAsync(finished, CancellationToken.None);

            logger.LogInformation("Ansible run {JobId} finished with exit code {ExitCode}", jobId, exitCode);
        }
        catch (Exception ex)
        {
            var sanitized = ex is AnsibleRunException or TimeoutException
                ? ex.Message
                : "An unexpected error occurred while running the playbook. See the server log for details.";

            if (ex is AnsibleRunException or TimeoutException)
            {
                logger.LogWarning("Ansible run {JobId} failed: {Message}", jobId, sanitized);
            }
            else
            {
                logger.LogError(ex, "Ansible run {JobId} failed unexpectedly", jobId);
            }

            var failed = store.Update(jobId, j => j with
            {
                Stage = AnsibleRunStage.Failed,
                Error = sanitized,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });

            await executions.SaveAsync(failed, CancellationToken.None);
        }
    }

    private async Task<string?> ResolveLimitAsync(AnsibleRunRequest request, CancellationToken cancellationToken)
    {
        if (request.TargetKind == AnsibleRunTargetKind.All)
        {
            return null;
        }

        var running = (await proxmox.ListContainersAsync(cancellationToken))
            .Where(c => c.IsRunning && !OrchestratorSelfFilter.IsSelf(c))
            .ToList();

        if (request.TargetKind == AnsibleRunTargetKind.Vm)
        {
            if (running.All(c => c.Hostname != request.TargetValue))
            {
                throw new AnsibleRunException($"Container '{request.TargetValue}' is not currently running.");
            }

            return request.TargetValue;
        }

        if (running.All(c => !c.Tags.Contains(request.TargetValue)))
        {
            throw new AnsibleRunException($"No running container currently has the tag '{request.TargetValue}'.");
        }

        return AnsibleGroupName.ForTag(request.TargetValue!);
    }
}
