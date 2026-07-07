using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Enums;
using Franz.Common.DependencyInjection;

namespace Aegis.Shared.Contracts;

public interface IReportExporter: IScopedDependency
{
    string Format { get; }

    Task ExportAsync(
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        string outputPath,
        ArchitectureReportDetailLevel detailLevel = ArchitectureReportDetailLevel.FullForensic,
        CancellationToken token = default);
}
