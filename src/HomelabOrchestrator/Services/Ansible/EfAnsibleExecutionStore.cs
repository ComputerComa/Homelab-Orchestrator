using System.Text.Json;
using HomelabOrchestrator.Data;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// EF Core-backed <see cref="IAnsibleExecutionStore"/>. Takes an <see cref="IDbContextFactory{TContext}"/>
/// rather than <see cref="ApplicationDbContext"/> directly, opening one short-lived context per
/// call — the standard pattern for a singleton service (this is registered alongside the other
/// Ansible job services) that needs a normally-scoped DbContext.
/// </summary>
public class EfAnsibleExecutionStore(IDbContextFactory<ApplicationDbContext> dbContextFactory) : IAnsibleExecutionStore
{
    public async Task SaveAsync(AnsibleRunJob job, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.AnsibleExecutions.FindAsync([job.Id], cancellationToken);
        if (existing is null)
        {
            db.AnsibleExecutions.Add(ToRecord(job));
        }
        else
        {
            CopyInto(existing, job);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnsibleExecutionRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.AnsibleExecutions
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<AnsibleExecutionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.AnsibleExecutions.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<int> InterruptStuckExecutionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stuck = await db.AnsibleExecutions
            .Where(e => e.Stage == AnsibleRunStage.Queued || e.Stage == AnsibleRunStage.Running)
            .ToListAsync(cancellationToken);

        foreach (var execution in stuck)
        {
            execution.Stage = AnsibleRunStage.Interrupted;
            execution.CompletedAtUtc ??= DateTime.UtcNow;
            execution.Error = "The orchestrator restarted while this run was in progress.";
        }

        if (stuck.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return stuck.Count;
    }

    private static AnsibleExecutionRecord ToRecord(AnsibleRunJob job)
    {
        var record = new AnsibleExecutionRecord { Id = job.Id };
        CopyInto(record, job);
        return record;
    }

    private static void CopyInto(AnsibleExecutionRecord record, AnsibleRunJob job)
    {
        record.PlaybookName = job.Request.PlaybookName;
        record.TargetKind = job.Request.TargetKind;
        record.TargetValue = job.Request.TargetValue;
        record.Stage = job.Stage;
        record.SubmittedBy = job.SubmittedBy;
        record.CreatedAtUtc = job.CreatedAtUtc.UtcDateTime;
        record.StartedAtUtc = job.StartedAtUtc?.UtcDateTime;
        record.CompletedAtUtc = job.CompletedAtUtc?.UtcDateTime;
        record.ResolvedTargetHostnamesJson = job.ResolvedTargetHostnames is { } hosts ? JsonSerializer.Serialize(hosts) : null;
        record.ExitCode = job.ExitCode;
        record.Error = job.Error;
    }
}
