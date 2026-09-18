using HomelabOrchestrator.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HomelabOrchestrator.Data;

/// <summary>
/// Backs ASP.NET Core Identity's user store and, now, persisted Ansible execution history — one
/// DbContext for both, never a second store per concern. This app expects exactly one operator
/// account, seeded once at startup from <see cref="Options.AdminOptions"/> — there is no
/// registration page.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<AnsibleExecutionRecord> AnsibleExecutions => Set<AnsibleExecutionRecord>();

    public DbSet<AnsibleExecutionLog> AnsibleExecutionLogs => Set<AnsibleExecutionLog>();

    public DbSet<SshPublicKey> SshPublicKeys => Set<SshPublicKey>();

    public DbSet<SshEnrollmentToken> SshEnrollmentTokens => Set<SshEnrollmentToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SQLite doesn't track DateTime.Kind — force Utc back on read, since every DateTime here
        // is always stored as UTC (AnsibleRunJob's *AtUtc properties are always UtcNow-derived).
        // Inlined as lambdas (not a shared local function) because HasConversion needs an
        // Expression<Func<...>>, which can't reference a local function.
        modelBuilder.Entity<AnsibleExecutionRecord>(entity =>
        {
            entity.Property(e => e.TargetKind).HasConversion<string>();
            entity.Property(e => e.Stage).HasConversion<string>();

            entity.Property(e => e.CreatedAtUtc).HasConversion(
                v => v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            entity.Property(e => e.StartedAtUtc).HasConversion(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
            entity.Property(e => e.CompletedAtUtc).HasConversion(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<AnsibleExecutionLog>(entity =>
        {
            entity.Property(e => e.Stream).HasConversion<string>();
            entity.Property(e => e.TimestampUtc).HasConversion(
                v => v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            // What the live-log cursor query (ExecutionId + Sequence > afterSequence, ordered by
            // Sequence) and the batched writer both rely on; unique also rejects an accidental
            // duplicate write for the same execution/sequence pair.
            entity.HasIndex(e => new { e.ExecutionId, e.Sequence }).IsUnique();
        });

        modelBuilder.Entity<SshPublicKey>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>();

            entity.Property(e => e.CreatedAtUtc).HasConversion(
                v => v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            entity.Property(e => e.ApprovedAtUtc).HasConversion(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
            entity.Property(e => e.RevokedAtUtc).HasConversion(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            // The actual duplicate-key guarantee — EnrollAsync's own FindByFingerprintAsync check
            // is only the common-case fast path, this index is what makes it race-safe.
            entity.HasIndex(e => e.Fingerprint).IsUnique();
        });

        modelBuilder.Entity<SshEnrollmentToken>(entity =>
        {
            entity.Property(e => e.CreatedAtUtc).HasConversion(
                v => v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            entity.Property(e => e.ExpiresAtUtc).HasConversion(
                v => v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            entity.Property(e => e.UsedAtUtc).HasConversion(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            entity.HasIndex(e => e.TokenHash);
        });
    }
}
