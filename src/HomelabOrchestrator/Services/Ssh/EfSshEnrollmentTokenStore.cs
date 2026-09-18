using HomelabOrchestrator.Data;
using HomelabOrchestrator.Models;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Services.Ssh;

/// <summary>EF Core-backed <see cref="ISshEnrollmentTokenStore"/>, same singleton-with-per-call-context pattern as <see cref="EfSshKeyStore"/>.</summary>
public class EfSshEnrollmentTokenStore(IDbContextFactory<ApplicationDbContext> dbContextFactory) : ISshEnrollmentTokenStore
{
    public async Task AddAsync(SshEnrollmentToken token, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.SshEnrollmentTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsValidAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SshEnrollmentTokens.AsNoTracking()
            .AnyAsync(t => t.TokenHash == tokenHash && t.UsedAtUtc == null && t.ExpiresAtUtc > nowUtc, cancellationToken);
    }

    public async Task<bool> TryConsumeAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SshEnrollmentTokens
            .Where(t => t.TokenHash == tokenHash && t.UsedAtUtc == null && t.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, nowUtc), cancellationToken);
        return rows == 1;
    }
}
