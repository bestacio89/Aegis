using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Sdk;

public sealed class AegisAnalysisSessionResult
{
    public bool Success { get; init; }


    public int ReportId { get; init; }


    public AegisArchitectureReport? Report { get; init; }


    public ProjectArchitectureContext? Context { get; init; }


    public ArchitectureReportDetailLevel DetailLevel { get; init; }


    public string? ErrorMessage { get; init; }



    /// <summary>
    /// Indicates whether the session completed successfully
    /// and produced all required data.
    /// </summary>
    public bool HasReport =>
        Success &&
        Report is not null &&
        Context is not null;



    public static AegisAnalysisSessionResult Completed(
        int reportId,
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        ArchitectureReportDetailLevel detailLevel)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(context);


        return new AegisAnalysisSessionResult
        {
            Success = true,

            ReportId = reportId,

            Report = report,

            Context = context,

            DetailLevel = detailLevel
        };
    }



    public static AegisAnalysisSessionResult Failed(
        string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            errorMessage);


        return new AegisAnalysisSessionResult
        {
            Success = false,

            ErrorMessage = errorMessage
        };
    }
}