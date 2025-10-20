using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Persistence.Repositories;
using Aegis.Infrastructure.Repositories;
using Franz.Common.Business.Repositories;
using Franz.Common.EntityFramework.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class RuleResultRepository
    : IRuleResultRepository
{
    private readonly AegisDbContext _db;
    private readonly ILogger<ReportRepository> _logger;

    public RuleResultRepository(AegisDbContext db, ILogger<ReportRepository> logger)
        : base()
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IEnumerable<RuleResultEntity>> GetViolationsBySeverityAsync(string severity, CancellationToken token = default)
      => await _db.RuleResults
          .AsNoTracking()
          .Where(r => r.Severity.ToString() == severity)
          .ToListAsync(token);
}
