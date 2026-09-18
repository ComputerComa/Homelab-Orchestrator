using HomelabOrchestrator.Data;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Exercises the real EF Core mapping for <see cref="AnsibleExecutionLog"/> (the Stream string
/// conversion, the unique ExecutionId+Sequence index the cursor query relies on) against a real
/// in-memory SQLite database — same pattern as <see cref="EfAnsibleExecutionStoreTests"/>.
/// </summary>
public class EfAnsibleExecutionLogStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly EfAnsibleExecutionLogStore _store;

    public EfAnsibleExecutionLogStoreTests()
    {
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var db = new ApplicationDbContext(options))
        {
            db.Database.EnsureCreated();
        }

        _store = new EfAnsibleExecutionLogStore(new FakeDbContextFactory(options));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task AppendAsync_then_ListAfterAsync_round_trips_entries_in_sequence_order()
    {
        var executionId = Guid.NewGuid();
        await _store.AppendAsync(executionId, [
            new AnsibleExecutionLog { ExecutionId = executionId, Sequence = 2, Stream = AnsibleLogStream.Stdout, Text = "second", TimestampUtc = DateTime.UtcNow },
            new AnsibleExecutionLog { ExecutionId = executionId, Sequence = 1, Stream = AnsibleLogStream.Stderr, Text = "first", TimestampUtc = DateTime.UtcNow },
        ]);

        var result = await _store.ListAfterAsync(executionId, 0);

        Assert.Equal(["first", "second"], result.Select(e => e.Text));
        Assert.Equal(AnsibleLogStream.Stderr, result[0].Stream);
        Assert.Equal(AnsibleLogStream.Stdout, result[1].Stream);
    }

    [Fact]
    public async Task ListAfterAsync_only_returns_entries_newer_than_the_cursor()
    {
        var executionId = Guid.NewGuid();
        await _store.AppendAsync(executionId, [
            new AnsibleExecutionLog { ExecutionId = executionId, Sequence = 1, Text = "one", TimestampUtc = DateTime.UtcNow },
            new AnsibleExecutionLog { ExecutionId = executionId, Sequence = 2, Text = "two", TimestampUtc = DateTime.UtcNow },
            new AnsibleExecutionLog { ExecutionId = executionId, Sequence = 3, Text = "three", TimestampUtc = DateTime.UtcNow },
        ]);

        var result = await _store.ListAfterAsync(executionId, 1);

        Assert.Equal(["two", "three"], result.Select(e => e.Text));
    }

    [Fact]
    public async Task ListAfterAsync_never_returns_another_executions_rows()
    {
        var executionId = Guid.NewGuid();
        var otherExecutionId = Guid.NewGuid();
        await _store.AppendAsync(executionId, [new AnsibleExecutionLog { ExecutionId = executionId, Sequence = 1, Text = "mine", TimestampUtc = DateTime.UtcNow }]);
        await _store.AppendAsync(otherExecutionId, [new AnsibleExecutionLog { ExecutionId = otherExecutionId, Sequence = 1, Text = "not mine", TimestampUtc = DateTime.UtcNow }]);

        var result = await _store.ListAfterAsync(executionId, 0);

        Assert.Equal(["mine"], result.Select(e => e.Text));
    }

    [Fact]
    public async Task AppendAsync_with_no_entries_is_a_no_op()
    {
        var executionId = Guid.NewGuid();

        await _store.AppendAsync(executionId, []);
        var result = await _store.ListAfterAsync(executionId, 0);

        Assert.Empty(result);
    }

    private sealed class FakeDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
