using Aegis.Sdk;
using Microsoft.Extensions.Logging;

namespace Aegis.Cli.Services;

/// <summary>
/// Encapsulates the Aegis CLI analysis workflow.
/// This allows dependency injection and testable orchestration.
/// </summary>
public sealed class AnalyzeCommandService
{
    private readonly AegisRunner _runner;
    private readonly ILogger<AnalyzeCommandService> _logger;

    public AnalyzeCommandService(AegisRunner runner, ILogger<AnalyzeCommandService> logger)
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
            _logger.LogInformation("🧠 Starting Aegis analysis for path: {Path}", projectPath);

            var policyFile = policyPath ?? "aegis.policy.json";
            var resultCode = await _runner.RunAsync(projectPath, policyFile, exportJson);

            switch (resultCode)
            {
                case 0:
                    _logger.LogInformation("✅ Aegis analysis completed successfully. No violations found.");
                    break;
                case 1:
                    _logger.LogWarning("⚠️ Analysis completed with rule violations.");
                    break;
                default:
                    _logger.LogError("❌ Analysis failed during execution.");
                    break;
            }

            return resultCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Unexpected failure during Aegis analysis execution.");
            return -1;
        }
    }
}
