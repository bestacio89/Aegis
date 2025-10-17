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

        logger.LogInformation("🧠 Initiating Aegis analysis at {Path}", projectPath);

        // ✅ Instance call (AegisRunner is not static anymore)
        var exitCode = await runner.RunAsync(projectPath, policyPath ?? "aegis.policy.json", exportJson);

        if (exitCode == 0)
            logger.LogInformation("✅ Aegis analysis completed successfully. No violations found.");
        else if (exitCode == 1)
            logger.LogWarning("⚠️ Analysis completed with rule violations.");
        else
            logger.LogError("❌ Analysis failed during execution.");

        return exitCode;
    }
}
