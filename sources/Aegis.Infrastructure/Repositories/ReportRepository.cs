using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Models;
using Franz.Common.EntityFramework.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Repositories;

public sealed class ReportRepository : IReportRepository
{
    private readonly AegisDbContext _db;
    private readonly ILogger<ReportRepository> _logger;

    public ReportRepository(AegisDbContext db, ILogger<ReportRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ReportEntity?> GetFullReportAsync(int id, CancellationToken token = default)
        => await _db.Reports.Include(r => r.RuleResults).AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, token);

    public async Task<ReportEntity?> GetLatestAsync(CancellationToken token = default)
        => await _db.Reports.Include(r => r.RuleResults).OrderByDescending(r => r.ScanDate).FirstOrDefaultAsync(token);

    public async Task<List<ReportEntity>> GetAllReportsAsync(int? limit = null, CancellationToken token = default)
    {
        var query = _db.Reports.AsNoTracking().OrderByDescending(r => r.ScanDate);
        if (limit.HasValue)
            query = (IOrderedQueryable<ReportEntity>)query.Take(limit.Value);
        return await query.ToListAsync(token);
    }

    public async Task<ReportEntity> CreateSessionAsync(string projectPath, ProjectContext context, CancellationToken token = default)
    {
        var entity = new ReportEntity
        {
            ProjectName = Path.GetFileName(projectPath),
            Language = context.Language,
           Framework = context.Metadata?.Framework ?? context.Framework ?? "Unknown",
 
            ScanDate = DateTime.UtcNow,
            RuleResults = new List<RuleResultEntity>()
        };

        _db.Reports.Add(entity);
        await _db.SaveChangesAsync(token);
        _logger.LogInformation("🆕 Created new audit session ID {Id} for {Project}", entity.Id, entity.ProjectName);
        return entity;
    }

    public async Task FinalizeReportAsync(int reportId, AegisReport report, CancellationToken token = default)
    {
        var entity = await _db.Reports.FindAsync(new object[] { reportId }, token);
        if (entity is null) return;

        entity.TotalViolations = report.TotalViolations;
        entity.HealthIndex = report.Metrics.ProjectHealthIndex;
        entity.WeightedCompliance = report.Metrics.WeightedCompliance;
        entity.FileCount = report.TotalFilesScanned;
        entity.DomainCount = report.Domains.Count;

        await _db.SaveChangesAsync(token);
        _logger.LogInformation("✅ Finalized report ID {Id} for {Project}", entity.Id, entity.ProjectName);
    }
}
