using System.Text.Json;
using HomelabOrchestrator.Models;
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
    IAnsibleExecutionLogStore logStore,
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

        AnsibleRunJob outcome;
        try
        {
            var running = store.Update(jobId, j => j with { Stage = AnsibleRunStage.Running, StartedAtUtc = DateTimeOffset.UtcNow });
            await SavePersistedSnapshotAsync(running, cancellationToken);

            var playbookPath = await catalog.ResolvePathAsync(job.Request.PlaybookName, cancellationToken);
            if (playbookPath is null)
            {
                throw new AnsibleRunException($"Playbook '{job.Request.PlaybookName}' is no longer available.");
            }

            var (limit, resolvedHosts) = await ResolveLimitAsync(job.Request, cancellationToken);
            store.Update(jobId, j => j with { ResolvedTargetHostnames = resolvedHosts });

            var logBuffer = new AnsibleExecutionLogBuffer(jobId, logStore, logger);
            store.AttachLiveLog(jobId, logBuffer);
            var flushLoop = logBuffer.RunFlushLoopAsync(cancellationToken);

            string? extraVarsFilePath = job.Request.ExtraVars is { Count: > 0 } extraVars
                ? await WriteExtraVarsFileAsync(jobId, extraVars, cancellationToken)
                : null;

            int exitCode;
            try
            {
                exitCode = await processRunner.RunPlaybookAsync(
                    playbookPath,
                    limit,
                    extraVarsFilePath,
                    (stream, line) => logBuffer.Enqueue(stream, line),
                    cancellationToken);
            }
            finally
            {
                logBuffer.Complete();
                await flushLoop;
                store.DetachLiveLog(jobId);

                if (extraVarsFilePath is not null)
                {
                    TryDeleteExtraVarsDirectory(Path.GetDirectoryName(extraVarsFilePath)!, jobId);
                }
            }

            outcome = store.Update(jobId, j => j with
            {
                Stage = exitCode == 0 ? AnsibleRunStage.Succeeded : AnsibleRunStage.Failed,
                ExitCode = exitCode,
                Error = exitCode == 0 ? null : $"ansible-playbook exited with code {exitCode}.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });

            logger.LogInformation("Ansible run {JobId} finished with exit code {ExitCode}", jobId, exitCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = store.Update(jobId, j => j with
            {
                Stage = AnsibleRunStage.Interrupted,
                Error = "The orchestrator is shutting down.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });
            logger.LogWarning("Ansible run {JobId} interrupted by application shutdown", jobId);
        }
        catch (TimeoutException ex)
        {
            outcome = store.Update(jobId, j => j with
            {
                Stage = AnsibleRunStage.TimedOut,
                Error = ex.Message,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });
            logger.LogWarning("Ansible run {JobId} timed out: {Message}", jobId, ex.Message);
        }
        catch (AnsibleRunException ex)
        {
            outcome = store.Update(jobId, j => j with
            {
                Stage = AnsibleRunStage.Failed,
                Error = ex.Message,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });
            logger.LogWarning("Ansible run {JobId} failed: {Message}", jobId, ex.Message);
        }
        catch (Exception ex)
        {
            outcome = store.Update(jobId, j => j with
            {
                Stage = AnsibleRunStage.Failed,
                Error = "An unexpected error occurred while running the playbook. See the server log for details.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            });
            logger.LogError(ex, "Ansible run {JobId} failed unexpectedly", jobId);
        }

        // Deliberately outside every catch above: a persistence failure here can only log, never
        // retroactively change `outcome`'s already-committed in-memory Stage, and can never escape
        // to crash the worker's outer await-foreach loop. CancellationToken.None: a run that
        // finishes right as the app is shutting down should still get its history entry written.
        await SavePersistedSnapshotAsync(outcome, CancellationToken.None);
    }

    private async Task SavePersistedSnapshotAsync(AnsibleRunJob job, CancellationToken cancellationToken)
    {
        try
        {
            await executions.SaveAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist execution history for Ansible run {JobId} (stage {Stage})", job.Id, job.Stage);
        }
    }

    /// <summary>
    /// Writes extra-vars as JSON beneath /run/homelab-orchestrator/&lt;execution-id&gt;/, restricted
    /// to 0700 (directory) / 0600 (file) — never world- or group-readable, since this may contain
    /// key material. Cleaned up by the caller's <c>finally</c> block, never left behind.
    /// </summary>
    private static async Task<string> WriteExtraVarsFileAsync(Guid jobId, IReadOnlyDictionary<string, object?> extraVars, CancellationToken cancellationToken)
    {
        var directory = Path.Combine("/run/homelab-orchestrator", jobId.ToString("N"));
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var path = Path.Combine(directory, "extra-vars.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(extraVars), cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return path;
    }

    private void TryDeleteExtraVarsDirectory(string directory, Guid jobId)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to delete extra-vars temp directory for Ansible run {JobId}", jobId);
        }
    }

    private async Task<(string? Limit, IReadOnlyList<string> ResolvedHosts)> ResolveLimitAsync(AnsibleRunRequest request, CancellationToken cancellationToken)
    {
        if (request.TargetKind == AnsibleRunTargetKind.All)
        {
            var everyone = (await proxmox.ListContainersAsync(cancellationToken))
                .Where(c => c.IsRunning && !OrchestratorSelfFilter.IsSelf(c))
                .Select(c => c.Hostname)
                .ToList();
            return (null, everyone);
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

            return (request.TargetValue, [request.TargetValue!]);
        }

        if (request.TargetKind == AnsibleRunTargetKind.Selection)
        {
            var hostnames = (request.TargetValue ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (hostnames.Length == 0)
            {
                throw new AnsibleRunException("No containers were selected.");
            }

            var runningHostnames = running.Select(c => c.Hostname).ToHashSet(StringComparer.Ordinal);
            var missing = hostnames.Where(h => !runningHostnames.Contains(h)).ToList();
            if (missing.Count > 0)
            {
                var noun = missing.Count == 1 ? "Container" : "Containers";
                var verb = missing.Count == 1 ? "is" : "are";
                throw new AnsibleRunException($"{noun} '{string.Join("', '", missing)}' {verb} not currently running.");
            }

            return (string.Join(',', hostnames), hostnames);
        }

        var tagged = running.Where(c => c.Tags.Contains(request.TargetValue)).ToList();
        if (tagged.Count == 0)
        {
            throw new AnsibleRunException($"No running container currently has the tag '{request.TargetValue}'.");
        }

        return (AnsibleGroupName.ForTag(request.TargetValue!), tagged.Select(c => c.Hostname).ToList());
    }
}
