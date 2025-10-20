using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Franz.Common.EntityFramework.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Repositories;

/// <summary>
/// Repository providing access to Aegis report data and their related rule results.
/// </summary>
public sealed class ReportRepository
    :  IReportRepository
{
    private readonly AegisDbContext _db;
    private readonly ILogger<ReportRepository> _logger;

    public ReportRepository(AegisDbContext db, ILogger<ReportRepository> logger)
        : base()
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves a full report including all associated rule results.
    /// </summary>
    public async Task<ReportEntity?> GetFullReportAsync(int id, CancellationToken token = default)
    {
        _logger.LogDebug("Fetching report with details for ID {ReportId}", id);

        return await _db.Reports
            .Include(r => r.RuleResults)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, token);
    }
}
