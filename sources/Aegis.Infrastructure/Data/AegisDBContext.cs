using Aegis.Infrastructure.Data;
using Franz.Common.Business.Domain;
using Franz.Common.EntityFramework;
using Franz.Common.EntityFramework.Auditing;
using Franz.Common.Mediator.Dispatchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Data;

/// <summary>
/// 💾 Central persistence context for Aegis.
/// Handles reports, rule results, and future audit or configuration entities.
/// Inherits Franz DbContextBase to get auditing and mediator integration.
/// </summary>
public sealed class AegisDbContext : DbContextBase
{
    private readonly ILogger<AegisDbContext> _logger;

    public DbSet<ReportEntity> Reports => Set<ReportEntity>();
    public DbSet<RuleResultEntity> RuleResults => Set<RuleResultEntity>();

    public AegisDbContext(
        DbContextOptions<AegisDbContext> options,
        IDispatcher dispatcher,
        ILogger<AegisDbContext> logger,
        ICurrentUserService? currentUser = null)
        : base(options, dispatcher, currentUser)
    {
        _logger = logger;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 🧩 Reports table
        modelBuilder.Entity<ReportEntity>(entity =>
        {
            entity.ToTable("Reports");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ProjectName).HasMaxLength(250);
           
            entity.Property(x => x.Language).HasMaxLength(100);
            entity.Property(x => x.Framework).HasMaxLength(100);
            entity.Property(x => x.HealthIndex).HasPrecision(5, 2);
        });

        // 🧠 Rule results table
        modelBuilder.Entity<RuleResultEntity>(entity =>
        {
            entity.ToTable("RuleResults");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.RuleId).HasMaxLength(100);
            entity.Property(x => x.RuleName).HasMaxLength(200);
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.Severity).HasMaxLength(50);
            entity.Property(x => x.Message).HasMaxLength(2000);

            entity.HasOne(x => x.Report)
                  .WithMany(x => x.RuleResults)
                  .HasForeignKey(x => x.Id)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// 🧾 Optional helper for automatic database initialization.
    /// Creates or migrates the schema if needed.
    /// </summary>
    public static async Task EnsureDatabaseAsync(AegisDbContext db, CancellationToken token = default)
    {
        var dbPath = db.Database.GetDbConnection().DataSource;
        db.GetService<ILogger<AegisDbContext>>().LogInformation("🧱 Ensuring Aegis database at {Path}", dbPath);

        await db.Database.MigrateAsync(token);
    }
}
