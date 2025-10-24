using Aegis.Infrastructure.Data;
using Franz.Common.DependencyInjection;
using Franz.Common.Business.Domain;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Infrastructure.Persistence;

public interface IReportRepository : IScopedDependency
{
    Task<ReportEntity?> GetFullReportAsync(int id, CancellationToken token = default);
    Task<ReportEntity?> GetLatestAsync(CancellationToken token = default);
    Task<List<ReportEntity>> GetAllReportsAsync(int? limit = null, CancellationToken token = default);

    // Session lifecycle
    Task<ReportEntity> CreateSessionAsync(string projectPath, ProjectArchitectureContext context, CancellationToken token = default);
    Task FinalizeReportAsync(int reportId, AegisArchitectureReport report, CancellationToken token = default);
}
