using Aegis.Cli.Services;
using Aegis.Sdk;
using Franz.Common.Logging.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Cli.Commands;

public static class AnalyzeCommand
{
    public static async Task<int> ExecuteAsync(string projectPath, string? policyPath, bool exportJson)
    {
        // 🏗️ Build the CLI host with Franz logging and DI
        using var host = CliHostBuilder.Build();

        var logger = host.Services.GetRequiredService<ILogger<AegisRunner>>();
        var runner = host.Services.GetRequiredService<AegisRunner>();

        try
        {
            logger.LogInformation("🧠 Initiating Aegis analysis for project at: {Path}", projectPath);

            // ✅ Call the modern orchestration method (creates report session, persists results, etc.)
            var exitCode = await runner.RunSessionAsync(
                projectPath: projectPath,
                policyPath: policyPath,
                exportJson: exportJson,
                token: default
            );

            // 🧾 Display final outcome clearly in CLI
            switch (exitCode)
            {
                case 0:
                    logger.LogInformation("✅ Analysis completed successfully — no violations found.");
                    break;
                case -1:
                    logger.LogError("❌ Analysis failed — check logs for diagnostic information.");
                    break;
                default:
                    logger.LogWarning("⚠️ Analysis completed with non-zero code ({Code}). See report for details.", exitCode);
                    break;
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Fatal error during Aegis analysis execution.");
            return -1;
        }
    }
}
