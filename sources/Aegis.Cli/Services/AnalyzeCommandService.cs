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

    public AnalyzeCommandService(AegisArchitectureAnalysisRunner runner, ILogger<AnalyzeCommandService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <summary>
    /// Executes the deterministic architecture audit using AegisRunner.
    /// </summary>
    public async Task<int> RunAsync(string projectPath, string? policyPath = null, bool exportJson = true)
    {
        try
        {
            _logger.LogInformation("🧠 Starting Aegis analysis for: {Path}", projectPath);

            var resultCode = await _runner.RunSessionAsync(
                projectPath: projectPath,
                policyPath: policyPath,
                exportJson: exportJson,
                token: default
            );

            switch (resultCode)
            {
                case 0:
                    _logger.LogInformation("✅ Aegis analysis completed successfully. No errors detected.");
                    break;
                case -1:
                    _logger.LogError("❌ Aegis analysis failed during execution. See logs for details.");
                    break;
                default:
                    _logger.LogWarning("⚠️ Aegis analysis completed with non-standard exit code: {Code}", resultCode);
                    break;
            }

            return resultCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Unexpected exception during Aegis CLI analysis execution.");
            return -1;
        }
    }
}
