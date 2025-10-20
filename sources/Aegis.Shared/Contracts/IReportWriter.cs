using Aegis.Shared.Models;
using Aegis.Shared.Enums;

namespace Aegis.Shared.Contracts;

public interface IReportExporter
{
    string Format { get; }

    Task ExportAsync(
        AegisReport report,
        ProjectContext context,
        string outputPath,
        ReportDetailLevel detailLevel = ReportDetailLevel.FullForensic,
        CancellationToken token = default);
}
