using Aegis.Cli.Services;
using Aegis.Sdk;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Cli.Commands;

#nullable enable

public static class AnalyzeCommand
{
    public static async Task<int> ExecuteAsync(
        string projectPath,
        string? policyPath,
        string? exportFormat = null)
    {
        using var host = CliHostBuilder.Build();

        var logger = host.Services
            .GetRequiredService<ILogger<AegisArchitectureAnalysisRunner>>();

        var runner = host.Services
            .GetRequiredService<AegisArchitectureAnalysisRunner>();

        var exporters = host.Services
            .GetRequiredService<IEnumerable<IReportExporter>>();

        try
        {
            logger.LogInformation(
                "🧠 Initiating Aegis analysis for project at: {Path}",
                projectPath);


            AegisAnalysisSessionResult result =
                await runner.RunSessionAsync(
                    projectPath,
                    policyPath,
                    CancellationToken.None);


            if (!result.Success)
            {
                logger.LogError(
                    "❌ Analysis failed for project {Project}",
                    projectPath);

                return -1;
            }


            logger.LogInformation(
                "✅ Analysis completed successfully — Report ID {ReportId}",
                result.ReportId);


            if (!string.IsNullOrWhiteSpace(exportFormat))
            {
                await ExportReportAsync(
                    exporters,
                    exportFormat,
                    result,
                    projectPath,
                    logger);
            }


            logger.LogInformation(
                """
                📊 Aegis Analysis Summary
                -------------------------
                Project      : {Project}
                Files Scanned: {Files}
                Violations   : {Violations}
                Health Index : {Health:0.00}%
                Detail Level : {Detail}
                """,
                result.Report?.ProjectName ?? "Unknown",
                result.Report?.TotalFilesScanned,
                result.Report?.TotalViolations,
                result.Report?.Metrics?.ProjectHealthIndex ?? 0,
                result.DetailLevel);


            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "💥 Fatal error during Aegis analysis execution.");

            return -1;
        }
    }


    private static async Task ExportReportAsync(
        IEnumerable<IReportExporter> exporters,
        string exportFormat,
        AegisAnalysisSessionResult result,
        string projectPath,
        ILogger logger)
    {
        var exporter = exporters.FirstOrDefault(x =>
            x.Format.Equals(
                exportFormat,
                StringComparison.OrdinalIgnoreCase));


        if (exporter is null)
        {
            logger.LogWarning(
                "⚠️ Export format '{Format}' is not registered.",
                exportFormat);

            return;
        }


        var outputPath = Path.Combine(
            projectPath,
            $"AegisReport.{exporter.Format.ToLowerInvariant()}");


        await exporter.ExportAsync(
            result.Report,
            result.Context,
            outputPath,
            result.DetailLevel,
            CancellationToken.None);


        logger.LogInformation(
            "📄 {Format} report exported → {Path}",
            exporter.Format,
            outputPath);
    }
}