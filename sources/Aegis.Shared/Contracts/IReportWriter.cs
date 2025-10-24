using Aegis.Shared.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Shared.Contracts;

public interface IReportExporter
{
    string Format { get; }

    Task ExportAsync(
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        string outputPath,
        ReportDetailLevel detailLevel = ReportDetailLevel.FullForensic,
        CancellationToken token = default);
}
