using IntegrationHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IntegrationHub.Api.Data;

public sealed class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<SalesRecord> SalesRecords => Set<SalesRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Integration>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.BaseUrl).HasMaxLength(500);
            e.Property(x => x.Cron).HasMaxLength(60);
            e.Property(x => x.Erp).HasConversion<string>().HasMaxLength(30);
        });

        model.Entity<JobRun>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Trigger).HasMaxLength(30);
            e.HasIndex(x => new { x.IntegrationId, x.StartedAt });
        });

        model.Entity<SalesRecord>(e =>
        {
            e.Property(x => x.ExternalId).HasMaxLength(80);
            e.Property(x => x.Sku).HasMaxLength(40);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            // Chave natural: garante idempotência quando a mesma janela é sincronizada de novo.
            e.HasIndex(x => new { x.IntegrationId, x.ExternalId }).IsUnique();
        });
    }
}
