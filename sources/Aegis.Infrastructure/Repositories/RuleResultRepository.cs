using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Elastic.Apm.Api;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.RecordIO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core.Tokens;

namespace Aegis.Infrastructure.Repositories;

public sealed class RuleResultRepository : IRuleResultRepository
{
    private readonly AegisDbContext _db;
    private readonly ILogger<RuleResultRepository> _logger;

    public RuleResultRepository(AegisDbContext db, ILogger<RuleResultRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IEnumerable<RuleResultEntity>> GetViolationsBySeverityAsync(string severity, CancellationToken token = default)
    {
        _logger.LogDebug("Fetching rule results with severity {Severity}", severity);

        if (!Enum.TryParse<ArchitectureRuleSeverity>(severity, true, out var parsedSeverity))
        {
            _logger.LogWarning("Invalid severity string '{Severity}' provided. Returning empty set.", severity);
            return Enumerable.Empty<RuleResultEntity>();
        }

        return await _db.RuleResults
            .AsNoTracking()
            .Where(r => r.Severity == parsedSeverity)
            .OrderByDescending(r => r.DateDetected)
            .ToListAsync(token);
    }


    public async Task<IEnumerable<RuleResultEntity>> GetViolationsByReportIdAsync(int reportId, CancellationToken token = default)
    {
        _logger.LogDebug("Fetching violations for report {ReportId}", reportId);
        return await _db.RuleResults
            .AsNoTracking()
            .Where(r => r.ReportId == reportId)
            .OrderByDescending(r => r.DateDetected)
            .ToListAsync(token);
    }

    public async Task<IEnumerable<RuleResultEntity>> GetCriticalViolationsAsync(int? limit = null, CancellationToken token = default)
    {
        _logger.LogDebug("Fetching critical violations (limit={Limit})", limit);

        var query = _db.RuleResults
            .AsNoTracking()
            .Where(r => r.Severity == ArchitectureRuleSeverity.Critical)
            .OrderByDescending(r => r.DateDetected);

        if (limit.HasValue)
            query = (IOrderedQueryable<RuleResultEntity>)query.Take(limit.Value);

        return await query.ToListAsync(token);

    }

    public async Task AddBatchAsync(
     IEnumerable<RuleResultEntity> results,
     CancellationToken token = default)
    {
        await _db.RuleResults.AddRangeAsync(
            results,
            token);

        await _db.SaveChangesAsync(token);
    }

    public async Task<List<RuleResultEntity>> GetPreviousReportAsync(
    string project,
    DateTimeOffset currentReportDate,
    CancellationToken token)
    {
        var previousReportId = await _db.RuleResults
            .AsNoTracking()
            .Where(r =>
            r.Project == project &&
            r.DateCreated < currentReportDate)
            .Select(r => r.ReportId)
            .Distinct()
            .OrderByDescending(id => id)
            .FirstOrDefaultAsync(token);
        
        var previousScan = await _db.RuleResults
            .AsNoTracking()
            .Where(r =>
             r.Project == project &&
             r.DateCreated < currentReportDate)
            .GroupBy(r => r.DateCreated)
            .OrderByDescending(g => g.Key)
            .FirstOrDefaultAsync(token);

        return previousScan?.ToList() ?? [];


    }
}




