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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AnsibleExecutionRecord>(entity =>
        {
            entity.Property(e => e.TargetKind).HasConversion<string>();
            entity.Property(e => e.Stage).HasConversion<string>();

            // SQLite doesn't track DateTime.Kind — force Utc back on read, since these are always
            // stored as UTC (AnsibleRunJob.CreatedAtUtc/CompletedAtUtc are always UtcNow-derived).
            entity.Property(e => e.CreatedAtUtc).HasConversion(
                v => v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            entity.Property(e => e.CompletedAtUtc).HasConversion(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            entity.HasIndex(e => e.CreatedAtUtc);
        });
    }
}
