using HomelabOrchestrator.Data;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Exercises the real EF Core mapping (string-converted Status, the unique Fingerprint index)
/// against a real in-memory SQLite database, mirroring EfAnsibleExecutionStoreTests's pattern.
/// </summary>
public class EfSshKeyStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly EfSshKeyStore _store;

    public EfSshKeyStoreTests()
    {
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new ApplicationDbContext(options))
        {
            db.Database.EnsureCreated();
        }

        _store = new EfSshKeyStore(new FakeDbContextFactory(options));
    }

    public void Dispose() => _connection.Dispose();

    private static SshPublicKey NewKey(string fingerprint, SshKeyStatus status = SshKeyStatus.Pending) => new()
    {
        Id = Guid.NewGuid(),
        DeviceName = "my-laptop",
        Algorithm = "ssh-ed25519",
        KeyDataBase64 = "AAAAC3NzaC1lZDI1NTE5AAAA",
        Fingerprint = fingerprint,
        Status = status,
        CreatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task AddAsync_inserts_a_row_that_GetAsync_and_FindByFingerprintAsync_can_find()
    {
        var key = NewKey("SHA256:abc123");

        await _store.AddAsync(key);

        Assert.NotNull(await _store.GetAsync(key.Id));
        Assert.NotNull(await _store.FindByFingerprintAsync("SHA256:abc123"));
    }

    [Fact]
    public async Task AddAsync_throws_on_a_duplicate_fingerprint()
    {
        await _store.AddAsync(NewKey("SHA256:dup"));

        await Assert.ThrowsAsync<DbUpdateException>(() => _store.AddAsync(NewKey("SHA256:dup")));
    }

    [Fact]
    public async Task ListEnabledAsync_only_returns_Enabled_keys()
    {
        await _store.AddAsync(NewKey("SHA256:pending", SshKeyStatus.Pending));
        await _store.AddAsync(NewKey("SHA256:enabled", SshKeyStatus.Enabled));
        await _store.AddAsync(NewKey("SHA256:revoked", SshKeyStatus.Revoked));

        var enabled = await _store.ListEnabledAsync();

        Assert.Single(enabled);
        Assert.Equal("SHA256:enabled", enabled[0].Fingerprint);
    }

    [Fact]
    public async Task ListAsync_returns_every_key_regardless_of_status()
    {
        await _store.AddAsync(NewKey("SHA256:a", SshKeyStatus.Pending));
        await _store.AddAsync(NewKey("SHA256:b", SshKeyStatus.Enabled));
        await _store.AddAsync(NewKey("SHA256:c", SshKeyStatus.Revoked));

        Assert.Equal(3, (await _store.ListAsync()).Count);
    }

    [Fact]
    public async Task UpdateAsync_applies_the_mutation_and_persists_it()
    {
        var key = NewKey("SHA256:approve-me");
        await _store.AddAsync(key);

        await _store.UpdateAsync(key.Id, k =>
        {
            k.Status = SshKeyStatus.Enabled;
            k.ApprovedBy = "alice";
            k.ApprovedAtUtc = DateTime.UtcNow;
        });

        var found = await _store.GetAsync(key.Id);
        Assert.NotNull(found);
        Assert.Equal(SshKeyStatus.Enabled, found.Status);
        Assert.Equal("alice", found.ApprovedBy);
    }

    [Fact]
    public async Task UpdateAsync_throws_for_an_unknown_id()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.UpdateAsync(Guid.NewGuid(), _ => { }));
    }

    [Fact]
    public async Task DeleteAsync_removes_the_row()
    {
        var key = NewKey("SHA256:delete-me");
        await _store.AddAsync(key);

        await _store.DeleteAsync(key.Id);

        Assert.Null(await _store.GetAsync(key.Id));
    }

    [Fact]
    public async Task DeleteAsync_is_a_no_op_for_an_unknown_id()
    {
        await _store.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task FindByFingerprintAsync_returns_null_when_not_found()
    {
        Assert.Null(await _store.FindByFingerprintAsync("SHA256:does-not-exist"));
    }

    private sealed class FakeDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
