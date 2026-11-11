using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Architecture.Models;
using Franz.Common.EntityFramework.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public async Task<ReportEntity?> GetPreviousForProjectAsync(
        string projectPath,
        int currentReportId,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var projectPathIdentity = NormalizeProjectPath(projectPath);

        return await _db.Reports
            .AsNoTracking()
            .Include(report => report.RuleResults)
            .Where(report =>
                report.ProjectPath == projectPathIdentity
                && report.Id != currentReportId)
            .OrderByDescending(report => report.ScanDate)
            .FirstOrDefaultAsync(token);
    }

    public async Task<List<ReportEntity>> GetAllReportsAsync(int? limit = null, CancellationToken token = default)
    {
        var query = _db.Reports.AsNoTracking().OrderByDescending(r => r.ScanDate);
        if (limit.HasValue)
            query = (IOrderedQueryable<ReportEntity>)query.Take(limit.Value);
        return await query.ToListAsync(token);
    }

    public async Task<ReportEntity> CreateSessionAsync(string projectPath, ProjectArchitectureContext context, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var projectPathIdentity = NormalizeProjectPath(projectPath);
        var entity = new ReportEntity
        {
            ProjectName = Path.GetFileName(projectPathIdentity),
            ProjectPath = projectPathIdentity,
            Language = context.Language,
            Framework = context.DetectedFrameworks.FirstOrDefault() ?? context.Framework ?? "Unknown",



            ScanDate = DateTime.UtcNow,
            RuleResults = new List<RuleResultEntity>()
        };

        _db.Reports.Add(entity);
        await _db.SaveChangesAsync(token);
        _logger.LogInformation("🆕 Created new audit session ID {Id} for {Project}", entity.Id, entity.ProjectName);
        return entity;
    }

    private static string NormalizeProjectPath(string projectPath)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        return OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }

    public async Task FinalizeReportAsync(int reportId, AegisArchitectureReport report, CancellationToken token = default)
    {
        var entity = await _db.Reports.FindAsync(new object[] { reportId }, token);
        if (entity is null) return;

        entity.TotalViolations = report.TotalViolations;
        entity.HealthIndex = report.Metrics.ProjectHealthIndex;
        entity.WeightedCompliance = report.Metrics.WeightedCompliance;
        entity.FileCount = report.TotalFilesScanned;
        entity.DomainCount = report.Domains.Count;

        entity.ReportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        await _db.SaveChangesAsync(token);
        _logger.LogInformation("✅ Finalized report ID {Id} for {Project} (JSON snapshot persisted, {Bytes} bytes)",
            entity.Id, entity.ProjectName, entity.ReportJson.Length);
    }
}