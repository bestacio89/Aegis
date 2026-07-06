using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Sdk;

public sealed class AegisAnalysisSessionResult
{
    public required int ReportId { get; init; }

    public required AegisArchitectureReport Report { get; init; }

    public required ProjectArchitectureContext Context { get; init; }

    public required ArchitectureReportDetailLevel DetailLevel { get; init; }

    public bool Success { get; init; }
}