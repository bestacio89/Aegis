using Aegis.Architecture.Analysis;
using Aegis.Architecture.RuleEngines;
using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Franz.Common.Business.Domain;
using Franz.Common.Business.Repositories;
using Franz.Common.EntityFramework.Repositories;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aegis.Sdk;

/// <summary>
/// Executes full deterministic Aegis analyses on projects —
/// from context detection → evaluator execution → rule evaluation → weighted aggregation → persistence.
/// Each run creates a unique ReportEntity session stored in the database, enabling historical comparisons.
/// </summary>
public sealed class AegisArchitectureAnalysisRunner
{
    private readonly RuleEngine _engine;
    private readonly EntityRepository<AegisDbContext, RuleResultEntity> _ruleResultRepo;
    private readonly EntityRepository<AegisDbContext, ReportEntity> _reportRepo;
    private readonly IRuleResultRepository _customRuleResultRepo;
    private readonly IReportRepository _customReportRepo;
    private readonly ILogger<AegisArchitectureAnalysisRunner> _logger;

    public AegisArchitectureAnalysisRunner(
        RuleEngine engine,
        EntityRepository<AegisDbContext, RuleResultEntity> ruleResultRepo,
        EntityRepository<AegisDbContext, ReportEntity> reportRepo,
        IRuleResultRepository customRuleResultRepo,
        IReportRepository customReportRepo,
        ILogger<AegisArchitectureAnalysisRunner> logger)
    {
        _engine = engine;
        _ruleResultRepo = ruleResultRepo;
        _reportRepo = reportRepo;
        _customRuleResultRepo = customRuleResultRepo;
        _customReportRepo = customReportRepo;
        _logger = logger;
    }

    // 🧭───────────────────────────────────────────────
    // HIGH-LEVEL ENTRYPOINT
    // ────────────────────────────────────────────────
    public async Task<int> RunSessionAsync(
        string projectPath,
        string? policyPath = null,
        bool exportJson = true,
        CancellationToken token = default)
    {
        try
        {
            // Load policy
            var policy = await LoadPolicyAsync(policyPath, token);
            _engine.ApplyPolicy(policy);

            // Detect project context
            _logger.LogInformation("🔍 Detecting project context for {Path}", projectPath);
            var context = ProjectArchitectureContextDetector.Detect(projectPath);
            _logger.LogInformation(
                "🧭 Context detected: {Lang}/{Framework} ({Architecture}) → {Domain}/{Layer} [{Nature}]",
                context.Language, context.Framework, context.ArchitectureStyle,
                context.DomainType, context.Layer, context.Nature);

            // Create a new report session
            var reportEntity = await _customReportRepo.CreateSessionAsync(projectPath, context, token);

            // Execute deterministic rule engine
            var report = await _engine.RunAsync(projectPath, context, token);

            // Persist rule results
            await PersistRuleResultsAsync(reportEntity.Id, report, token);

            // Finalize report with aggregated metrics
            await _customReportRepo.FinalizeReportAsync(reportEntity.Id, report, token);

            // Optional export
            if (exportJson)
                await ExportJsonAsync(report, context, policy.ReportDetailLevel, token);

            // Compare with previous
            await CompareWithPreviousAsync(reportEntity, token);

            _logger.LogInformation("✅ Audit session {Id} complete for {Project}.", reportEntity.Id, report.ProjectName);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Aegis session failed for {Path}", projectPath);
            return -1;
        }
    }

    // 🧾───────────────────────────────────────────────
    // POLICY LOADER
    // ────────────────────────────────────────────────
    private async Task<AegisArchitecturePolicy> LoadPolicyAsync(string? policyPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(policyPath) || !File.Exists(policyPath))
        {
            _logger.LogWarning("⚠️ Policy file not found. Using default aegis.policy.json");
            policyPath = Path.Combine(AppContext.BaseDirectory, "config", "aegis.policy.json");
        }

        _logger.LogInformation("📜 Loading Aegis policy from {Path}", policyPath);
        var policyJson = await File.ReadAllTextAsync(policyPath, token);

        var policy = JsonSerializer.Deserialize<AegisArchitecturePolicy>(policyJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new AegisArchitecturePolicy();

        return policy;
    }

    // 💾───────────────────────────────────────────────
    // RESULT PERSISTENCE
    // ────────────────────────────────────────────────
    private async Task PersistRuleResultsAsync(int reportId, AegisArchitectureReport report, CancellationToken token)
    {
        _logger.LogInformation("💾 Persisting {Count} rule results for report {Id}", report.Results.Count, reportId);

        foreach (var r in report.Results)
        {
            var entity = new RuleResultEntity
            {
                ReportId = reportId,
                RuleId = r.RuleId,
                RuleName = r.RuleName,
                Severity = r.Severity,
                Category = r.Category.ToString(),
                Target = r.Target,
                Message = r.Message,
                ImpactScore = r.ImpactScore,
                WeightedImpact = r.WeightedImpact,
                Domain = r.Domain,
                AnalyzerVersion = r.AnalyzerVersion,
                Project = report.ProjectName,
                DateDetected = DateTime.UtcNow
            };

            await _ruleResultRepo.AddAsync(entity);
        }

        _logger.LogInformation("✅ Rule results persisted for report {Id}", reportId);
    }

    // 📊───────────────────────────────────────────────
    // COMPARISON LOGIC
    // ────────────────────────────────────────────────
    private async Task CompareWithPreviousAsync(ReportEntity currentReport, CancellationToken token)
    {
        try
        {
            var lastReport = await _customReportRepo.GetLatestAsync(token);
            if (lastReport is null || lastReport.Id == currentReport.Id)
            {
                _logger.LogInformation("📂 No previous report to compare with.");
                return;
            }

            var lastResults = await _customRuleResultRepo.GetViolationsByReportIdAsync(lastReport.Id, token);
            var currentResults = await _customRuleResultRepo.GetViolationsByReportIdAsync(currentReport.Id, token);

            var newViolations = currentResults
                .Where(cr => !lastResults.Any(lr => lr.RuleId == cr.RuleId && lr.Target == cr.Target))
                .ToList();

            if (newViolations.Any())
            {
                _logger.LogWarning("⚠️ {Count} new violations introduced since last report.", newViolations.Count);
                foreach (var nv in newViolations)
                    _logger.LogWarning("   ➕ New: {RuleId} → {Target}", nv.RuleId, nv.Target);
            }
            else
            {
                _logger.LogInformation("✅ No new violations introduced since last report.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed during report comparison.");
        }
    }

    // 🧾───────────────────────────────────────────────
    // JSON EXPORT
    // ────────────────────────────────────────────────
    private async Task ExportJsonAsync(
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        ArchitectureReportDetailLevel detailLevel,
        CancellationToken token)
    {
        try
        {
            var jsonPath = Path.Combine(
                report.ProjectPath ?? Directory.GetCurrentDirectory(),
                $"AegisReport.{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

            var exporter = _engine.GetExporter("json");
            if (exporter is not null)
            {
                await exporter.ExportAsync(report, context, jsonPath, detailLevel, token);
                _logger.LogInformation("📝 JSON report exported → {Path}", jsonPath);
            }
            else
            {
                _logger.LogWarning("⚠️ JSON exporter not registered — skipping report export.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to export JSON report for {Project}", report.ProjectName);
        }
    }
}
