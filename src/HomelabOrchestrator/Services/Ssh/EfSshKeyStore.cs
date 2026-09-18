using HomelabOrchestrator.Data;
using HomelabOrchestrator.Models;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Services.Ssh;

/// <summary>
/// EF Core-backed <see cref="ISshKeyStore"/>. Takes <see cref="IDbContextFactory{TContext}"/>,
/// not <see cref="ApplicationDbContext"/> directly, since this is registered as a singleton
/// (mirrors <c>EfAnsibleExecutionStore</c>) and opens a short-lived context per call.
/// </summary>
public class EfSshKeyStore(IDbContextFactory<ApplicationDbContext> dbContextFactory) : ISshKeyStore
{
    public async Task<SshPublicKey?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SshPublicKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Fingerprint == fingerprint, cancellationToken);
    }

    public async Task AddAsync(SshPublicKey key, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.SshPublicKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SshPublicKey?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SshPublicKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<SshPublicKey>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SshPublicKeys.AsNoTracking().OrderByDescending(k => k.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SshPublicKey>> ListEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SshPublicKeys.AsNoTracking()
            .Where(k => k.Status == SshKeyStatus.Enabled)
            .OrderBy(k => k.DeviceName)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(Guid id, Action<SshPublicKey> mutate, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var key = await db.SshPublicKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"SSH key {id} does not exist.");
        mutate(key);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var key = await db.SshPublicKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
        if (key is not null)
        {
            db.SshPublicKeys.Remove(key);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
