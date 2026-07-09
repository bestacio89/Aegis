using Aegis.Infrastructure.Data;
using Franz.Common.DependencyInjection;
using Franz.Common.Business.Domain;

namespace Aegis.Infrastructure.Persistence;

public interface IRuleResultRepository : IScopedDependency
{
    Task<IEnumerable<RuleResultEntity>> GetViolationsBySeverityAsync(string severity, CancellationToken token = default);
    Task<IEnumerable<RuleResultEntity>> GetViolationsByReportIdAsync(int reportId, CancellationToken token = default);
    Task<IEnumerable<RuleResultEntity>> GetCriticalViolationsAsync(int? limit = null, CancellationToken token = default);
    Task AddBatchAsync(
        IEnumerable<RuleResultEntity> results,
        CancellationToken token = default);
    Task<List<RuleResultEntity>> GetPreviousReportAsync(
        string project,
        DateTimeOffset currentReportDate,
        CancellationToken token);
}
