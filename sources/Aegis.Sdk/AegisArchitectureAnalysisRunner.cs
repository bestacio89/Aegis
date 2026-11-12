
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Aegis.Architecture.Analysis;
using Aegis.Architecture.Analysis.Semantics;
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
using Microsoft.Extensions.Options;

namespace Aegis.Sdk;

/// <summary>
/// Executes deterministic Aegis architecture analysis sessions.
/// Handles context detection, rule execution, persistence,
/// comparison and report generation orchestration.
/// </summary>
public sealed class AegisArchitectureAnalysisRunner
{
    private static readonly JsonSerializerOptions PolicyJsonOptions = CreatePolicyJsonOptions();

    private readonly RuleEngine _engine;
    private readonly IEntityRepository<RuleResultEntity, int> _ruleResultRepo;
    private readonly IEntityRepository<ReportEntity, int> _reportRepo;
    private readonly IRuleResultRepository _customRuleResultRepo;
    private readonly IReportRepository _customReportRepo;
    private readonly IReadOnlyDictionary<string, IReportExporter> _exporters;
    private readonly AegisArchitecturePolicy _configuredPolicy;
    private readonly ILogger<AegisArchitectureAnalysisRunner> _logger;
    private readonly JavaSemanticAnalysisOrchestrator? _javaSemanticAnalysis;
    private readonly SemanticAnalysisPipeline? _semanticAnalysisPipeline;

    public AegisArchitectureAnalysisRunner(
        RuleEngine engine,
        IEntityRepository<RuleResultEntity, int> ruleResultRepo,
        IEntityRepository<ReportEntity, int> reportRepo,
        IRuleResultRepository customRuleResultRepo,
        IReportRepository customReportRepo,
        IEnumerable<IReportExporter> exporters,
        IOptions<AegisArchitecturePolicy> policyOptions,
        ILogger<AegisArchitectureAnalysisRunner> logger,
        JavaSemanticAnalysisOrchestrator? javaSemanticAnalysis = null,
        SemanticAnalysisPipeline? semanticAnalysisPipeline = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(ruleResultRepo);
        ArgumentNullException.ThrowIfNull(reportRepo);
        ArgumentNullException.ThrowIfNull(customRuleResultRepo);
        ArgumentNullException.ThrowIfNull(customReportRepo);
        ArgumentNullException.ThrowIfNull(exporters);
        ArgumentNullException.ThrowIfNull(policyOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _engine = engine;
        _ruleResultRepo = ruleResultRepo;
        _reportRepo = reportRepo;
        _customRuleResultRepo = customRuleResultRepo;
        _customReportRepo = customReportRepo;
        _configuredPolicy = policyOptions.Value;
        _logger = logger;
        _javaSemanticAnalysis = javaSemanticAnalysis;
        _semanticAnalysisPipeline = semanticAnalysisPipeline;

        var exporterList = exporters.ToList();

        var duplicateFormats = exporterList
            .GroupBy(exporter => exporter.Format, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateFormats.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate report exporter formats registered: {string.Join(", ", duplicateFormats)}.");
        }

        _exporters = exporterList.ToDictionary(
            exporter => exporter.Format,
            StringComparer.OrdinalIgnoreCase);
    }

    private static JsonSerializerOptions CreatePolicyJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    public async Task<AegisAnalysisSessionResult> RunSessionAsync(
        string projectPath,
        string? policyPath = null,
        CancellationToken token = default,
        IProgress<AnalysisProgressInfo>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        try
        {
            token.ThrowIfCancellationRequested();

            Report(progress, 1, "Preparing", "Resolving active policy snapshot");

            // The registered options instance is the same policy snapshot
            // consumed by evaluator constructors. Using it by default keeps
            // evaluator execution and rule interpretation on one policy.
            var policy = string.IsNullOrWhiteSpace(policyPath)
                ? _configuredPolicy
                : await LoadPolicyAsync(policyPath, token).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(policyPath)
                && !HaveEquivalentEvaluatorSettings(_configuredPolicy, policy))
            {
                throw new InvalidOperationException(
                    "The explicit policy file differs from the evaluator configuration " +
                    "loaded from the split policy documents. Evaluators are constructed " +
                    "from the split configuration and cannot safely consume a separate " +
                    "policy snapshot. Update the split policy documents under config/ or " +
                    "run without an explicit --policy override. No analysis was executed.");
            }

            token.ThrowIfCancellationRequested();

            _engine.ApplyPolicy(policy);

            var designPatterns = policy.Architecture.DesignPatterns;
            _logger.LogInformation(
                "Active design-pattern policy: Command={Command}, Strategy={Strategy}, " +
                "Observer={Observer}, Singleton={Singleton}, Mediator={Mediator}.",
                designPatterns.EnforceCommandPattern,
                designPatterns.EnforceStrategyPattern,
                designPatterns.EnforceObserverPattern,
                designPatterns.EnforceSingletonPattern,
                designPatterns.EnforceMediatorPattern);

            if (!string.IsNullOrWhiteSpace(policyPath))
            {
                _logger.LogInformation(
                    "Explicit policy file matches the active evaluator configuration; " +
                    "the validated policy snapshot will be used for rule interpretation.");
            }

            Report(progress, 4, "Preparing", "Policy applied");

            _logger.LogInformation(
                "Detecting project context for {Path}",
                projectPath);

            Report(progress, 5, "Detecting project", projectPath);

            var context = ProjectArchitectureContextDetector.Detect(
                projectPath,
                policy.Architecture);

            _logger.LogInformation(
                "Context detected: {Language}/{Framework} -> {Domain}/{Layer}",
                context.Language,
                context.Framework,
                context.DomainType,
                context.Layer);

            if (_semanticAnalysisPipeline is not null)
            {
                Report(progress, 7, "Semantic analysis", "Running registered language analyzers");
                await _semanticAnalysisPipeline.AnalyzeAsync(context, token)
                    .ConfigureAwait(false);
            }
            else if (_javaSemanticAnalysis is not null)
            {
                Report(progress, 7, "Semantic analysis", "Analysing Java project modules");
                await _javaSemanticAnalysis.AnalyzeAsync(context, token)
                    .ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();

            Report(progress, 9, "Opening audit session", null);

            var reportEntity = await _customReportRepo.CreateSessionAsync(
                projectPath,
                context,
                token).ConfigureAwait(false);

            var report = await _engine.RunAsync(
                projectPath,
                context,
                token,
                path => IsAnalysisPathAllowed(path),
                progress).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            Report(
                progress,
                92,
                "Saving results",
                $"{report.Results.Count:N0} rule results");

            await PersistRuleResultsAsync(
                reportEntity.Id,
                report,
                token).ConfigureAwait(false);

            Report(progress, 96, "Saving snapshot", "Serializing the full report");

            await _customReportRepo.FinalizeReportAsync(
                reportEntity.Id,
                report,
                token).ConfigureAwait(false);

            Report(progress, 99, "Comparing", "Previous report");

            await CompareWithPreviousAsync(
                reportEntity,
                token).ConfigureAwait(false);

            Report(progress, 100, "Complete", null);

            _logger.LogInformation(
                "Audit session {Id} complete for {Project}",
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
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Analysis session cancelled for {Path}",
                projectPath);

            return new AegisAnalysisSessionResult
            {
                Success = false,
                ErrorMessage = "Analysis was cancelled."
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Aegis session failed for {Path}",
                projectPath);

            return new AegisAnalysisSessionResult
            {
                Success = false,
                ErrorMessage = exception.Message
            };
        }
    }

    private static bool IsAnalysisPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Do not exclude a directory merely because it is named "client":
        // Unity projects commonly live there, and frontend source is part of
        // the intended analysis surface. Exclude generated/dependency folders
        // by path segment instead.
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var excludedSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Temp{Path.DirectorySeparatorChar}"
        };

        return !excludedSegments.Any(segment =>
            normalized.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static void Report(
        IProgress<AnalysisProgressInfo>? progress,
        double percent,
        string stage,
        string? detail)
    {
        progress?.Report(new AnalysisProgressInfo(percent, stage, detail));
    }

    private async Task<AegisArchitecturePolicy> LoadPolicyAsync(
        string? policyPath,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyPath);

        _logger.LogInformation(
            "Loading Aegis policy from {Path}",
            policyPath);

        if (!File.Exists(policyPath))
        {
            throw new FileNotFoundException(
                "The Aegis policy file was not found.",
                policyPath);
        }

        var json = await File.ReadAllTextAsync(policyPath, token)
            .ConfigureAwait(false);

        try
        {
            var policy = DeserializePolicyDocument(json);

            _logger.LogInformation(
                "Aegis policy loaded: {Name}, version {Version}, detail level {DetailLevel}",
                policy.Name,
                policy.Version,
                policy.ReportDetailLevel);

            return policy;
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "Invalid Aegis policy document at {Path}.",
                policyPath);

            throw new InvalidDataException(
                $"The Aegis policy document '{policyPath}' is invalid. " +
                "Analysis cannot safely continue with an assumed default policy.",
                exception);
        }
    }

    internal static AegisArchitecturePolicy DeserializePolicyDocument(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                "The policy document root must be a JSON object.");
        }

        var root = document.RootElement;

        var hasArchitectureWrapper = TryGetPropertyIgnoreCase(
            root,
            "AegisArchitecturePolicy",
            out var architecturePolicy);

        var hasLegacyWrapper = TryGetPropertyIgnoreCase(
            root,
            "AegisPolicy",
            out var legacyPolicy);

        if (hasArchitectureWrapper && hasLegacyWrapper)
        {
            throw new JsonException(
                "The policy document cannot contain both 'AegisArchitecturePolicy' " +
                "and 'AegisPolicy' wrappers.");
        }

        if (hasArchitectureWrapper || hasLegacyWrapper)
        {
            var wrappedPolicy = hasArchitectureWrapper
                ? architecturePolicy
                : legacyPolicy;

            if (wrappedPolicy.ValueKind != JsonValueKind.Object)
            {
                var wrapperName = hasArchitectureWrapper
                    ? "AegisArchitecturePolicy"
                    : "AegisPolicy";

                throw new JsonException(
                    $"'{wrapperName}' must contain a JSON object.");
            }

            root = wrappedPolicy;
        }

        var policy = root.Deserialize<AegisArchitecturePolicy>(
            PolicyJsonOptions);

        return policy
            ?? throw new JsonException(
                "The policy document deserialized to null.");
    }

    private static bool HaveEquivalentEvaluatorSettings(
        AegisArchitecturePolicy configuredPolicy,
        AegisArchitecturePolicy requestedPolicy)
    {
        ArgumentNullException.ThrowIfNull(configuredPolicy);
        ArgumentNullException.ThrowIfNull(requestedPolicy);

        // Metadata and rule thresholds are intentionally excluded: thresholds
        // are consumed by RuleEngineCore, while the remaining groups configure
        // evaluator instances that were constructed from the registered options.
        var configuredSettings = JsonSerializer.SerializeToNode(
            new
            {
                configuredPolicy.Architecture,
                configuredPolicy.Repository,
                configuredPolicy.Security,
                configuredPolicy.Maintainability,
                configuredPolicy.Dependency,
                configuredPolicy.Naming,
                configuredPolicy.Complexity,
                configuredPolicy.Cohesion,
                configuredPolicy.Coupling,
                configuredPolicy.ApiConsistency,
                configuredPolicy.ErrorHandling,
                configuredPolicy.Frontend,
                configuredPolicy.DependencyGraph,
                configuredPolicy.Node,
                configuredPolicy.CircularDependency,
                configuredPolicy.Configuration,
                configuredPolicy.Logging,
                configuredPolicy.Transaction,
                configuredPolicy.Performance
            },
            PolicyJsonOptions);

        var requestedSettings = JsonSerializer.SerializeToNode(
            new
            {
                requestedPolicy.Architecture,
                requestedPolicy.Repository,
                requestedPolicy.Security,
                requestedPolicy.Maintainability,
                requestedPolicy.Dependency,
                requestedPolicy.Naming,
                requestedPolicy.Complexity,
                requestedPolicy.Cohesion,
                requestedPolicy.Coupling,
                requestedPolicy.ApiConsistency,
                requestedPolicy.ErrorHandling,
                requestedPolicy.Frontend,
                requestedPolicy.DependencyGraph,
                requestedPolicy.Node,
                requestedPolicy.CircularDependency,
                requestedPolicy.Configuration,
                requestedPolicy.Logging,
                requestedPolicy.Transaction,
                requestedPolicy.Performance
            },
            PolicyJsonOptions);

        return JsonNode.DeepEquals(configuredSettings, requestedSettings);
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public async Task ExportReportAsync(
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        string format,
        string outputPath,
        ArchitectureReportDetailLevel detailLevel =
            ArchitectureReportDetailLevel.FullForensic,
        ReportLanguage language = ReportLanguage.English,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (!_exporters.TryGetValue(format, out var exporter))
        {
            _logger.LogWarning(
                "Exporter {Format} not registered.",
                format);

            throw new InvalidOperationException(
                $"No report exporter registered for format '{format}'.");
        }

        await exporter.ExportAsync(
            report,
            context,
            outputPath,
            detailLevel,
            language,
            token).ConfigureAwait(false);

        _logger.LogInformation(
            "{Format} report exported to {Path}",
            format,
            outputPath);
    }

    public IReadOnlyCollection<string> AvailableExportFormats =>
        _exporters.Keys.ToArray();

    private async Task PersistRuleResultsAsync(
        int reportId,
        AegisArchitectureReport report,
        CancellationToken token)
    {
        var entities = report.Results
            .Select(result => new RuleResultEntity
            {
                ReportId = reportId,
                RuleId = result.RuleId,
                RuleName = result.RuleName,
                Severity = result.Severity,
                Category = result.Category.ToString(),
                Target = Path.GetFileName(result.FilePath),
                SourceFile = result.FilePath,
                Message = result.Message,
                ImpactScore = result.ImpactScore,
                WeightedImpact = result.WeightedImpact,
                Domain = result.Domain,
                AnalyzerVersion = result.AnalyzerVersion,
                Project = report.ProjectName,
                DateDetected = DateTime.UtcNow
            })
            .ToList();

        if (entities.Count == 0)
        {
            _logger.LogInformation(
                "No rule results to persist for report {ReportId}.",
                reportId);

            return;
        }

        await _customRuleResultRepo.AddBatchAsync(
            entities,
            token).ConfigureAwait(false);
    }

    private async Task CompareWithPreviousAsync(
        ReportEntity currentReport,
        CancellationToken token)
    {
        try
        {
            var previousReport = await _customReportRepo.GetPreviousForProjectAsync(
                currentReport.ProjectPath,
                currentReport.ScanDate,
                currentReport.Id,
                token).ConfigureAwait(false);

            if (previousReport is null)
            {
                _logger.LogInformation(
                    "No previous report exists for project {ProjectPath}.",
                    currentReport.ProjectPath);

                return;
            }

            var currentResults = await _customRuleResultRepo.GetViolationsByReportIdAsync(
                currentReport.Id,
                token).ConfigureAwait(false);

            var previousResultsWithSourcePaths = previousReport.RuleResults
                .Where(result => !string.IsNullOrWhiteSpace(result.SourceFile))
                .Select(CreateSourceIdentity)
                .ToHashSet(ViolationIdentityComparer.Instance);

            // Older persisted reports did not populate SourceFile. Their basename
            // is the only available identity, so retain a deliberate legacy fallback.
            var previousLegacyResults = previousReport.RuleResults
                .Where(result => string.IsNullOrWhiteSpace(result.SourceFile))
                .Select(CreateTargetIdentity)
                .ToHashSet(ViolationIdentityComparer.Instance);

            var newViolations = currentResults
                .Where(current =>
                    !previousResultsWithSourcePaths.Contains(CreateSourceIdentity(current))
                    && !previousLegacyResults.Contains(CreateTargetIdentity(current)))
                .ToList();

            _logger.LogInformation(
                "Compared report {CurrentReportId} with project-scoped previous report {PreviousReportId}.",
                currentReport.Id,
                previousReport.Id);

            if (newViolations.Count > 0)
            {
                _logger.LogWarning(
                    "{Count} new violations detected for project {ProjectPath}.",
                    newViolations.Count,
                    currentReport.ProjectPath);
            }
            else
            {
                _logger.LogInformation(
                    "No new violations introduced for project {ProjectPath}.",
                    currentReport.ProjectPath);
            }
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed report comparison for project {ProjectPath}.",
                currentReport.ProjectPath);
        }
    }

    private static ViolationIdentity CreateSourceIdentity(RuleResultEntity result) =>
        new(result.RuleId, result.SourceFile ?? result.Target, result.Category, result.Domain);

    private static ViolationIdentity CreateTargetIdentity(RuleResultEntity result) =>
        new(result.RuleId, result.Target, result.Category, result.Domain);

    private readonly record struct ViolationIdentity(
        string RuleId,
        string Target,
        string Category,
        string? Domain);

    private sealed class ViolationIdentityComparer : IEqualityComparer<ViolationIdentity>
    {
        private static StringComparer FilePathComparer =>
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public static ViolationIdentityComparer Instance { get; } = new();

        public bool Equals(ViolationIdentity left, ViolationIdentity right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left.RuleId, right.RuleId)
            && FilePathComparer.Equals(left.Target, right.Target)
            && StringComparer.OrdinalIgnoreCase.Equals(left.Category, right.Category)
            && StringComparer.OrdinalIgnoreCase.Equals(left.Domain, right.Domain);

        public int GetHashCode(ViolationIdentity value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.RuleId),
                FilePathComparer.GetHashCode(value.Target),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Category),
                value.Domain is null
                    ? 0
                    : StringComparer.OrdinalIgnoreCase.GetHashCode(value.Domain));
    }

}
