using Aegis.Core.Analysis;
using Aegis.Core.RuleEngines;
using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Persistence.Repositories;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Aegis.Shared.Models.Policies;
using Elastic.Apm.Api;
using Franz.Common.Business.Domain;
using Franz.Common.Business.Repositories;
using Franz.Common.EntityFramework.Repositories;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aegis.Sdk;

/// <summary>
/// Executes full deterministic Aegis analyses on projects —
/// from context detection → evaluator execution → rule evaluation → weighted aggregation → persistence.
/// Supports read/write + specialized repository operations.
/// </summary>
public sealed class AegisRunner
{
    private readonly RuleEngine _engine;

    // 🧱 Generic repositories
    private readonly EntityRepository<AegisDbContext, RuleResultEntity> _ruleResultRepo;
    private readonly EntityRepository<AegisDbContext, ReportEntity> _reportRepo;

    private readonly IReadRepository<RuleResultEntity> _readRuleResultRepo;
    private readonly IReadRepository<ReportEntity> _readReportRepo;

    // 🎯 Specialized repositories
    private readonly IRuleResultRepository _customRuleResultRepo;
    private readonly IReportRepository _customReportRepo;

    private readonly ILogger<AegisRunner> _logger;

    public AegisRunner(
        RuleEngine engine,
        EntityRepository<AegisDbContext, RuleResultEntity> ruleResultRepo,
        EntityRepository<AegisDbContext, ReportEntity> reportRepo,
        IReadRepository<RuleResultEntity> readRuleResultRepo,
        IReadRepository<ReportEntity> readReportRepo,
        IRuleResultRepository customRuleResultRepo,
        IReportRepository customReportRepo,
        ILogger<AegisRunner> logger)
    {
        _engine = engine;
        _ruleResultRepo = ruleResultRepo;
        _reportRepo = reportRepo;
        _readRuleResultRepo = readRuleResultRepo;
        _readReportRepo = readReportRepo;
        _customRuleResultRepo = customRuleResultRepo;
        _customReportRepo = customReportRepo;
        _logger = logger;
    }

    // 🧭───────────────────────────────────────────────
    // POLICY LOADER
    // ────────────────────────────────────────────────
    public async Task<int> RunAsync(string projectPath, string policyPath, bool exportJson = true, CancellationToken token = default)
    {
        try
        {
            if (!File.Exists(policyPath))
            {
                _logger.LogWarning("⚠️ Policy file not found at {Path}, using default aegis.policy.json", policyPath);
                policyPath = Path.Combine(AppContext.BaseDirectory, "config", "aegis.policy.json");
            }

            _logger.LogInformation("📜 Loading Aegis policy from {Path}", policyPath);
            var policyJson = await File.ReadAllTextAsync(policyPath, token);

            var policy = JsonSerializer.Deserialize<AegisPolicy>(policyJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (policy is null)
            {
                _logger.LogError("❌ Failed to parse the Aegis policy file.");
                return -1;
            }

            _engine.ApplyPolicy(policy);
            return await RunAsync(projectPath, policy.ReportDetailLevel, exportJson, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed while applying policy to Aegis engine");
            return -1;
        }
    }

    // ⚙️───────────────────────────────────────────────
    // MAIN EXECUTION
    // ────────────────────────────────────────────────
    public async Task<int> RunAsync(
        string projectPath,
        ReportDetailLevel detailLevel = ReportDetailLevel.SummaryOnly,
        bool exportJson = true,
        CancellationToken token = default)
    {
        try
        {
            _logger.LogInformation("🔍 Detecting project context for {Path}", projectPath);
            var context = ProjectContextDetector.Detect(projectPath);

            _logger.LogInformation(
                "🧭 Context detected: {Lang}/{Framework} ({Architecture}) → {Domain}/{Layer} [{Nature}]",
                context.Language, context.Framework, context.ArchitectureStyle,
                context.DomainType, context.Layer, context.Nature);

            // Run full deterministic audit
            var report = await _engine.RunAsync(projectPath, context, token);

            // Persist results
            await PersistResultsAsync(report, context, token);

            // Optional JSON export
            if (exportJson)
                await ExportJsonAsync(report, context, detailLevel, token);

            // Compute verdict
            var exitCode = report.TotalViolations == 0 ? 0 : 1;
            _logger.LogInformation(
                "📊 Final verdict for {Project}: {Result}",
                report.ProjectName,
                exitCode == 0 ? "✅ Clean" : "⚠️ Violations found");

            return exitCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Aegis analysis failed for {Path}", projectPath);
            return -1;
        }
    }

    // 💾───────────────────────────────────────────────
    // PERSISTENCE LAYER
    // ────────────────────────────────────────────────
    private async Task PersistResultsAsync(AegisReport report, ProjectContext context, CancellationToken token)
    {
        try
        {
            _logger.LogInformation("💾 Persisting analysis results for {Project}", report.ProjectName);

            // Convert RuleResults → Entities
            var ruleEntities = report.Results.Select(r => new RuleResultEntity
            {
                RuleId = r.RuleId,
                RuleName = r.RuleName,
                Severity = r.Severity,
                Category = r.Category.ToString(),
                Target = r.Target,
                Message = r.Message,
                ImpactScore = r.ImpactScore,
                WeightedImpact = r.WeightedImpact,
                DateDetected = DateTime.UtcNow,
                Domain = r.Domain,
                AnalyzerVersion = r.AnalyzerVersion,
                Project = report.ProjectName
            }).ToList();

            foreach (var entity in ruleEntities)
            {
                _logger.LogDebug(
                    "   - Violation: {RuleId} | {Severity} | {Target} | {Message}",
                    entity.RuleId, entity.Severity, entity.Target, entity.Message);
                await _ruleResultRepo.AddAsync(entity);
            }
            
      

            var reportEntity = new ReportEntity
            {
                ProjectName = report.ProjectName,
                Language = report.Language,
                Framework = report.Framework ?? "none",
                ScanDate = report.ScanDate.UtcDateTime,
                TotalViolations = report.TotalViolations,
                HealthIndex = report.Metrics.ProjectHealthIndex,
                WeightedCompliance = report.Metrics.WeightedCompliance,
                FileCount = report.TotalFilesScanned,
                DomainCount = report.Domains.Count,
                RuleResults = ruleEntities
            };

            await _reportRepo.AddAsync(reportEntity);
           

            _logger.LogInformation("✅ Report persisted successfully for {Project}", report.ProjectName);

            // Example custom repo usage (demonstration)
            var criticalViolations = await _customRuleResultRepo.GetViolationsBySeverityAsync("Critical", token);
            _logger.LogInformation("🚨 Found {Count} critical violations across this and prior runs.", criticalViolations.Count());

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to persist Aegis analysis results for {Project}", report.ProjectName);
        }
    }

    // 🧾───────────────────────────────────────────────
    // JSON EXPORT
    // ────────────────────────────────────────────────
    private async Task ExportJsonAsync(
        AegisReport report,
        ProjectContext context,
        ReportDetailLevel detailLevel,
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

    // 🔍───────────────────────────────────────────────
    // OPTIONAL: FETCH & COMPARE LAST REPORTS
    // ────────────────────────────────────────────────
    public async Task CompareWithLastReportAsync(string projectName, CancellationToken token = default)
    {
        try
        {
            var lastReport = (await _readReportRepo.GetAll(token))
                .OrderByDescending(r => r.ScanDate)
                .FirstOrDefault(r => r.ProjectName == projectName);

            if (lastReport is null)
            {
                _logger.LogWarning("📂 No prior report found for {Project}", projectName);
                return;
            }

            var fullReport = await _customReportRepo.GetFullReportAsync(lastReport.Id, token);
            _logger.LogInformation("📈 Loaded last report for {Project} (Violations={Count})", projectName, fullReport?.TotalViolations ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to fetch last report for {Project}", projectName);
        }
    }
}
