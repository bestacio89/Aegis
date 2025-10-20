using Aegis.Infrastructure.Data;
using Franz.Common.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Franz.Common.Business.Domain;

namespace Aegis.Infrastructure.Persistence;

public interface IReportRepository : IScopedDependency
{
    Task<ReportEntity?> GetFullReportAsync(int id, CancellationToken token = default);
}


