using HomelabOrchestrator.Data;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Exercises the real EF Core mapping (string-converted enums, the CreatedAtUtc index) against a
/// real SQLite database, not a fake — an in-memory one, kept alive for the test's lifetime by one
/// open connection (SQLite's ":memory:" database is dropped the moment its last connection closes).
/// </summary>
public class EfAnsibleExecutionStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly EfAnsibleExecutionStore _store;

    public EfAnsibleExecutionStoreTests()
    {
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new ApplicationDbContext(options))
        {
            db.Database.EnsureCreated();
        }

        _dbContextFactory = new FakeDbContextFactory(options);
        _store = new EfAnsibleExecutionStore(_dbContextFactory);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task SaveAsync_inserts_a_new_row_that_GetAsync_can_find()
    {
        var job = new AnsibleRunJob
        {
            Id = Guid.NewGuid(),
            Request = new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.TagGroup, "mqtt"),
            Stage = AnsibleRunStage.Queued,
        };

        await _store.SaveAsync(job);
        var found = await _store.GetAsync(job.Id);

        Assert.NotNull(found);
        Assert.Equal("ssh-check", found.PlaybookName);
        Assert.Equal(AnsibleRunTargetKind.TagGroup, found.TargetKind);
        Assert.Equal("mqtt", found.TargetValue);
        Assert.Equal(AnsibleRunStage.Queued, found.Stage);
    }

    [Fact]
    public async Task SaveAsync_updates_the_existing_row_for_the_same_id_instead_of_inserting_a_second_one()
    {
        var job = new AnsibleRunJob
        {
            Id = Guid.NewGuid(),
            Request = new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.All, null),
            Stage = AnsibleRunStage.Queued,
        };
        await _store.SaveAsync(job);

        var finished = job with { Stage = AnsibleRunStage.Succeeded, ExitCode = 0, CompletedAtUtc = DateTimeOffset.UtcNow };
        await _store.SaveAsync(finished);

        var found = await _store.GetAsync(job.Id);
        Assert.NotNull(found);
        Assert.Equal(AnsibleRunStage.Succeeded, found.Stage);
        Assert.Equal(0, found.ExitCode);

        var recent = await _store.ListRecentAsync(10);
        Assert.Single(recent);
    }

    [Fact]
    public async Task SaveAsync_round_trips_SubmittedBy_StartedAtUtc_and_ResolvedTargetHostnames()
    {
        var job = new AnsibleRunJob
        {
            Id = Guid.NewGuid(),
            Request = new AnsibleRunRequest("ssh-check", AnsibleRunTargetKind.Selection, "web-01,db-01"),
            Stage = AnsibleRunStage.Running,
            SubmittedBy = "alice",
            StartedAtUtc = DateTimeOffset.UtcNow,
            ResolvedTargetHostnames = ["web-01", "db-01"],
        };

        await _store.SaveAsync(job);
        var found = await _store.GetAsync(job.Id);

        Assert.NotNull(found);
        Assert.Equal("alice", found.SubmittedBy);
        Assert.NotNull(found.StartedAtUtc);
        Assert.Equal("[\"web-01\",\"db-01\"]", found.ResolvedTargetHostnamesJson);
    }

    [Fact]
    public async Task InterruptStuckExecutionsAsync_only_touches_Queued_and_Running_rows()
    {
        var queued = new AnsibleRunJob { Id = Guid.NewGuid(), Request = new AnsibleRunRequest("a", AnsibleRunTargetKind.All, null), Stage = AnsibleRunStage.Queued };
        var running = new AnsibleRunJob { Id = Guid.NewGuid(), Request = new AnsibleRunRequest("b", AnsibleRunTargetKind.All, null), Stage = AnsibleRunStage.Running };
        var succeeded = new AnsibleRunJob { Id = Guid.NewGuid(), Request = new AnsibleRunRequest("c", AnsibleRunTargetKind.All, null), Stage = AnsibleRunStage.Succeeded };
        await _store.SaveAsync(queued);
        await _store.SaveAsync(running);
        await _store.SaveAsync(succeeded);

        var count = await _store.InterruptStuckExecutionsAsync();

        Assert.Equal(2, count);
        Assert.Equal(AnsibleRunStage.Interrupted, (await _store.GetAsync(queued.Id))!.Stage);
        Assert.Equal(AnsibleRunStage.Interrupted, (await _store.GetAsync(running.Id))!.Stage);
        Assert.Equal(AnsibleRunStage.Succeeded, (await _store.GetAsync(succeeded.Id))!.Stage);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_id()
    {
        Assert.Null(await _store.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListRecentAsync_orders_newest_first_and_respects_the_limit()
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            await _store.SaveAsync(new AnsibleRunJob
            {
                Id = Guid.NewGuid(),
                Request = new AnsibleRunRequest($"playbook-{i}", AnsibleRunTargetKind.All, null),
                CreatedAtUtc = now.AddMinutes(i),
            });
        }

        var recent = await _store.ListRecentAsync(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("playbook-2", recent[0].PlaybookName);
        Assert.Equal("playbook-1", recent[1].PlaybookName);
    }

    private sealed class FakeDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
