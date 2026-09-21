using HomelabOrchestrator.Data;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Tests;

/// <summary>Exercises the real EF Core mapping and, most importantly, TryConsumeAsync's atomic single-use guarantee against a real in-memory SQLite database.</summary>
public class EfSshEnrollmentTokenStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly EfSshEnrollmentTokenStore _store;

    public EfSshEnrollmentTokenStoreTests()
    {
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new ApplicationDbContext(options))
        {
            db.Database.EnsureCreated();
        }

        _store = new EfSshEnrollmentTokenStore(new FakeDbContextFactory(options));
    }

    public void Dispose() => _connection.Dispose();

    private static SshEnrollmentToken NewToken(string hash, DateTime now, TimeSpan? lifetime = null, DateTime? usedAtUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        TokenHash = hash,
        CreatedAtUtc = now,
        ExpiresAtUtc = now + (lifetime ?? TimeSpan.FromMinutes(10)),
        UsedAtUtc = usedAtUtc,
    };

    [Fact]
    public async Task IsValidAsync_is_true_for_an_unused_unexpired_token()
    {
        var now = DateTime.UtcNow;
        await _store.AddAsync(NewToken("hash-1", now));

        Assert.True(await _store.IsValidAsync("hash-1", now));
    }

    [Fact]
    public async Task IsValidAsync_is_false_for_an_unknown_hash()
    {
        Assert.False(await _store.IsValidAsync("does-not-exist", DateTime.UtcNow));
    }

    [Fact]
    public async Task IsValidAsync_is_false_once_expired()
    {
        var now = DateTime.UtcNow;
        await _store.AddAsync(NewToken("hash-expired", now, lifetime: TimeSpan.FromMinutes(10)));

        Assert.False(await _store.IsValidAsync("hash-expired", now.AddMinutes(11)));
    }

    [Fact]
    public async Task IsValidAsync_does_not_consume_the_token()
    {
        var now = DateTime.UtcNow;
        await _store.AddAsync(NewToken("hash-peek", now));

        await _store.IsValidAsync("hash-peek", now);

        Assert.True(await _store.IsValidAsync("hash-peek", now));
    }

    [Fact]
    public async Task TryConsumeAsync_succeeds_exactly_once_for_a_valid_token()
    {
        var now = DateTime.UtcNow;
        await _store.AddAsync(NewToken("hash-once", now));

        Assert.True(await _store.TryConsumeAsync("hash-once", now));
        Assert.False(await _store.TryConsumeAsync("hash-once", now));
    }

    [Fact]
    public async Task TryConsumeAsync_fails_for_an_expired_token_that_was_never_used()
    {
        var now = DateTime.UtcNow;
        await _store.AddAsync(NewToken("hash-expired-unused", now, lifetime: TimeSpan.FromMinutes(10)));

        Assert.False(await _store.TryConsumeAsync("hash-expired-unused", now.AddMinutes(11)));
    }

    [Fact]
    public async Task TryConsumeAsync_fails_for_an_unknown_hash()
    {
        Assert.False(await _store.TryConsumeAsync("unknown", DateTime.UtcNow));
    }

    private sealed class FakeDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
