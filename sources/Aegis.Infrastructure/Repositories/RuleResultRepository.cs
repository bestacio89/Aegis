using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

        if (!Enum.TryParse<RuleSeverity>(severity, true, out var parsedSeverity))
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
            .Where(r => r.Severity == RuleSeverity.Critical)
            .OrderByDescending(r => r.DateDetected);

        if (limit.HasValue)
            query = (IOrderedQueryable<RuleResultEntity>)query.Take(limit.Value);

        return await query.ToListAsync(token);
    }

}
