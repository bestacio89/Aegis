using Aegis.Sdk;
using Microsoft.Extensions.Logging;

namespace Aegis.Cli.Services;

/// <summary>
/// Encapsulates the Aegis CLI analysis workflow.
/// Allows dependency injection and testable orchestration.
/// </summary>
public sealed class AnalyzeCommandService
{
    private readonly AegisArchitectureAnalysisRunner _runner;
    private readonly ILogger<AnalyzeCommandService> _logger;

    public AnalyzeCommandService(
        AegisArchitectureAnalysisRunner runner,
        ILogger<AnalyzeCommandService> logger)
    {
        _runner = runner;
        _logger = logger;
    }


    /// <summary>
    /// Executes the deterministic architecture audit using AegisRunner.
    /// Returns the complete analysis session result.
    /// </summary>
    public async Task<AegisAnalysisSessionResult?> RunAsync(
        string projectPath,
        string? policyPath = null,
        CancellationToken token = default)
    {
        try
        {
            _logger.LogInformation(
                "🧠 Starting Aegis analysis for: {Path}",
                projectPath);


            var result = await _runner.RunSessionAsync(
                projectPath: projectPath,
                policyPath: policyPath,
                token: token);


            if (!result.Success)
            {
                _logger.LogError(
                    "❌ Aegis analysis failed during execution.");

                return null;
            }


            _logger.LogInformation(
                """
                ✅ Aegis analysis completed successfully.

                Report ID      : {ReportId}
                Project        : {Project}
                Files Scanned  : {Files}
                Violations     : {Violations}
                Health Index   : {Health:0.00}%
                """,
                result.ReportId,
                result.Report?.ProjectName,
                result.Report?.TotalFilesScanned,
                result.Report?.TotalViolations,
                result.Report?.Metrics.ProjectHealthIndex);


            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "💥 Unexpected exception during Aegis CLI analysis execution.");

            return null;
        }
    }
}