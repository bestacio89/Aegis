using Aegis.Architecture.Analysis;
using Aegis.Architecture.RuleEngines;
using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Franz.Common.Business.Repositories;
using Franz.Common.EntityFramework.Repositories;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aegis.Sdk;

/// <summary>
/// Executes deterministic Aegis architecture analysis sessions.
/// Handles context detection, rule execution, persistence,
/// comparison and report generation orchestration.
/// </summary>
public sealed class AegisArchitectureAnalysisRunner
{
    private readonly RuleEngine _engine;

    private readonly IEntityRepository<RuleResultEntity, int> _ruleResultRepo;
    private readonly IEntityRepository<ReportEntity, int> _reportRepo;

    private readonly IRuleResultRepository _customRuleResultRepo;
    private readonly IReportRepository _customReportRepo;

    private readonly IReadOnlyDictionary<string, IReportExporter> _exporters;

    private readonly ILogger<AegisArchitectureAnalysisRunner> _logger;


    public AegisArchitectureAnalysisRunner(
        RuleEngine engine,
        IEntityRepository<RuleResultEntity, int> ruleResultRepo,
        IEntityRepository<ReportEntity, int> reportRepo,
        IRuleResultRepository customRuleResultRepo,
        IReportRepository customReportRepo,
        IEnumerable<IReportExporter> exporters,
        ILogger<AegisArchitectureAnalysisRunner> logger)
    {
        _engine = engine;

        _ruleResultRepo = ruleResultRepo;
        _reportRepo = reportRepo;

        _customRuleResultRepo = customRuleResultRepo;
        _customReportRepo = customReportRepo;

        _exporters = exporters.ToDictionary(
            x => x.Format,
            StringComparer.OrdinalIgnoreCase);

        _logger = logger;
    }



    public async Task<AegisAnalysisSessionResult> RunSessionAsync(
        string projectPath,
        string? policyPath = null,
        bool exportJson = false,
        CancellationToken token = default)
    {
        try
        {
            var policy = await LoadPolicyAsync(
                policyPath,
                token);


            _engine.ApplyPolicy(policy);


            _logger.LogInformation(
                "🔍 Detecting project context for {Path}",
                projectPath);


            var context =
                ProjectArchitectureContextDetector.Detect(projectPath);


            _logger.LogInformation(
                "🧭 Context detected: {Language}/{Framework} → {Domain}/{Layer}",
                context.Language,
                context.Framework,
                context.DomainType,
                context.Layer);



            var reportEntity =
                await _customReportRepo.CreateSessionAsync(
                    projectPath,
                    context,
                    token);



            var report =
                await _engine.RunAsync(
                    projectPath,
                    context,
                    token);



            await PersistRuleResultsAsync(
                reportEntity.Id,
                report,
                token);



            await _customReportRepo.FinalizeReportAsync(
                reportEntity.Id,
                report,
                token);



            if (exportJson)
            {
                await ExportReportAsync(
                    "json",
                    report,
                    context,
                    policy.ReportDetailLevel,
                    token);
            }



            await CompareWithPreviousAsync(
                reportEntity,
                token);



            _logger.LogInformation(
                "✅ Audit session {Id} complete for {Project}",
                reportEntity.Id,
                report.ProjectName);



            return new AegisAnalysisSessionResult
            {
                ReportId = reportEntity.Id,
                Report = report,
                Context = context,
                DetailLevel = policy.ReportDetailLevel,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Aegis session failed for {Path}",
                projectPath);

            return null;
        }
    }



    private async Task<AegisArchitecturePolicy> LoadPolicyAsync(
        string? policyPath,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(policyPath) ||
           !File.Exists(policyPath))
        {
            policyPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "config",
                    "aegis.policy.json");
        }


        _logger.LogInformation(
            "📜 Loading Aegis policy from {Path}",
            policyPath);



        if (!File.Exists(policyPath))
        {
            _logger.LogWarning(
                "⚠️ Policy file missing. Using defaults.");

            return new AegisArchitecturePolicy();
        }



        var json =
            await File.ReadAllTextAsync(
                policyPath,
                token);



        return JsonSerializer.Deserialize<AegisArchitecturePolicy>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            })
            ?? new AegisArchitecturePolicy();
    }



    private async Task ExportReportAsync(
        string format,
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        ArchitectureReportDetailLevel detailLevel,
        CancellationToken token)
    {
        if (!_exporters.TryGetValue(format, out var exporter))
        {
            _logger.LogWarning(
                "⚠️ Exporter {Format} not registered.",
                format);

            return;
        }



        var output =
            Path.Combine(
                report.ProjectPath ??
                Directory.GetCurrentDirectory(),
                $"AegisReport.{DateTime.UtcNow:yyyyMMdd_HHmmss}.{format}");



        await exporter.ExportAsync(
            report,
            context,
            output,
            detailLevel,
            token);



        _logger.LogInformation(
            "📄 {Format} report exported → {Path}",
            format,
            output);
    }



    private async Task PersistRuleResultsAsync(
        int reportId,
        AegisArchitectureReport report,
        CancellationToken token)
    {
        _logger.LogInformation(
            "💾 Persisting {Count} rule results for report {Id}",
            report.Results.Count,
            reportId);



        foreach (var result in report.Results)
        {
            await _ruleResultRepo.AddAsync(
                new RuleResultEntity
                {
                    ReportId = reportId,
                    RuleId = result.RuleId,
                    RuleName = result.RuleName,
                    Severity = result.Severity,
                    Category = result.Category.ToString(),
                    Target = result.Target,
                    Message = result.Message,
                    ImpactScore = result.ImpactScore,
                    WeightedImpact = result.WeightedImpact,
                    Domain = result.Domain,
                    AnalyzerVersion = result.AnalyzerVersion,
                    Project = report.ProjectName,
                    DateDetected = DateTime.UtcNow
                });
        }



        _logger.LogInformation(
            "✅ Rule results persisted for report {Id}",
            reportId);
    }



    private async Task CompareWithPreviousAsync(
        ReportEntity currentReport,
        CancellationToken token)
    {
        try
        {
            var previous =
                await _customReportRepo.GetLatestAsync(token);


            if (previous == null ||
               previous.Id == currentReport.Id)
            {
                _logger.LogInformation(
                    "📂 No previous report to compare with.");

                return;
            }



            var previousResults =
                await _customRuleResultRepo
                    .GetViolationsByReportIdAsync(
                        previous.Id,
                        token);



            var currentResults =
                await _customRuleResultRepo
                    .GetViolationsByReportIdAsync(
                        currentReport.Id,
                        token);



            var newViolations =
                currentResults
                .Where(current =>
                    !previousResults.Any(previous =>
                        previous.RuleId == current.RuleId &&
                        previous.Target == current.Target))
                .ToList();



            if (newViolations.Count > 0)
            {
                _logger.LogWarning(
                    "⚠️ {Count} new violations detected.",
                    newViolations.Count);
            }
            else
            {
                _logger.LogInformation(
                    "✅ No new violations introduced.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Failed report comparison.");
        }
    }
}