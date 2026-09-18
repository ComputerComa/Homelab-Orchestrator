using HomelabOrchestrator.Data;
using HomelabOrchestrator.Models;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// EF Core-backed <see cref="IAnsibleExecutionLogStore"/>, same <see cref="IDbContextFactory{TContext}"/>-per-call
/// pattern as <see cref="EfAnsibleExecutionStore"/> — this is a singleton, so it never holds one
/// long-lived <see cref="ApplicationDbContext"/>.
/// </summary>
public class EfAnsibleExecutionLogStore(IDbContextFactory<ApplicationDbContext> dbContextFactory) : IAnsibleExecutionLogStore
{
    public async Task AppendAsync(Guid executionId, IReadOnlyList<AnsibleExecutionLog> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.AnsibleExecutionLogs.AddRange(entries);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnsibleExecutionLog>> ListAfterAsync(Guid executionId, long afterSequence, int? limit = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.AnsibleExecutionLogs
            .AsNoTracking()
            .Where(l => l.ExecutionId == executionId && l.Sequence > afterSequence)
            .OrderBy(l => l.Sequence)
            .AsQueryable();

        if (limit is { } take)
        {
            query = query.Take(take);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
