using Aegis.Security.Pipeline;
using Aegis.Security.Reporting.Contracts;
using Aegis.Shared.Security.Models;
using Aegis.Wpf.Models.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Aegis.Wpf.Services.Security;



/// <summary>
/// Runs the security pipeline in its own DI scope (the pipeline and report builders are registered
/// scoped, while WPF view models are long-lived singletons).
/// </summary>
public sealed class SecurityScanService : ISecurityScanService
{
    private static readonly string[] Formats = ["md", "html", "pdf"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecurityScanService> _logger;

    public SecurityScanService(IServiceScopeFactory scopeFactory, ILogger<SecurityScanService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public IReadOnlyList<string> AvailableExportFormats => Formats;

    public async Task<SecurityScanOutcome> RunAsync(string projectPath, string environment, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<IAegisSecurityPipeline>();

            var context = new ProjectSecurityContext
            {
                ProjectPath = projectPath,
                RepositoryRoot = projectPath,
                Environment = string.IsNullOrWhiteSpace(environment) ? "Default" : environment,
                ExecutedAtUtc = DateTimeOffset.UtcNow
            };

            _logger.LogInformation("Security scan started for {Path} ({Env})", projectPath, context.Environment);

            var (report, compliance) = await Task.Run(() => pipeline.RunAsync(context, ct), ct);

            var view = SecurityReportProjector.Project(report, compliance);

            _logger.LogInformation("Security scan finished: {Findings} findings, {Rate:F1}% compliant",
                view.Findings.Count, view.ComplianceRate);

            return new SecurityScanOutcome(true, report, compliance, view, null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Security scan cancelled for {Path}", projectPath);
            return new SecurityScanOutcome(false, null, null, null, "Scan cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Security scan failed for {Path}", projectPath);
            return new SecurityScanOutcome(false, null, null, null, ex.Message);
        }
    }

    public async Task ExportAsync(SecurityScanOutcome outcome, string format, string outputPath, CancellationToken ct)
    {
        if (outcome.Report is null)
            throw new InvalidOperationException("There is no completed scan to export.");

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        switch (format.ToLowerInvariant())
        {
            case "md":
            case "markdown":
                await File.WriteAllTextAsync(outputPath,
                    sp.GetRequiredService<IMarkdownReportBuilder>().Build(outcome.Report), ct);
                break;

            case "html":
                await File.WriteAllTextAsync(outputPath,
                    sp.GetRequiredService<IHtmlReportBuilder>().Build(outcome.Report), ct);
                break;

            case "pdf":
                await File.WriteAllBytesAsync(outputPath,
                    sp.GetRequiredService<IPdfReportBuilder>().Build(outcome.Report), ct);
                break;

            default:
                throw new InvalidOperationException($"Unsupported export format '{format}'.");
        }

        _logger.LogInformation("Security report exported ({Format}) -> {Path}", format, outputPath);
    }
}