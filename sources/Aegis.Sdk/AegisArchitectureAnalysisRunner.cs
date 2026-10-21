
using System.Text.Json;
using System.Text.Json.Serialization;
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
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(ruleResultRepo);
        ArgumentNullException.ThrowIfNull(reportRepo);
        ArgumentNullException.ThrowIfNull(customRuleResultRepo);
        ArgumentNullException.ThrowIfNull(customReportRepo);
        ArgumentNullException.ThrowIfNull(exporters);
        ArgumentNullException.ThrowIfNull(logger);

        _engine = engine;
        _ruleResultRepo = ruleResultRepo;
        _reportRepo = reportRepo;
        _customRuleResultRepo = customRuleResultRepo;
        _customReportRepo = customReportRepo;
        _logger = logger;

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

            Report(progress, 1, "Preparing", "Loading policy");

            var policy = await LoadPolicyAsync(policyPath, token)
                .ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            _engine.ApplyPolicy(policy);

            Report(progress, 4, "Preparing", "Policy applied");

            _logger.LogInformation(
                "Detecting project context for {Path}",
                projectPath);

            Report(progress, 5, "Detecting project", projectPath);

            var context = ProjectArchitectureContextDetector.Detect(projectPath);

            _logger.LogInformation(
                "Context detected: {Language}/{Framework} -> {Domain}/{Layer}",
                context.Language,
                context.Framework,
                context.DomainType,
                context.Layer);

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
        if (string.IsNullOrWhiteSpace(policyPath))
        {
            policyPath = Path.Combine(
                AppContext.BaseDirectory,
                "config",
                "aegis.policy.json");
        }

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

            if (TryGetPropertyIgnoreCase(
                    root,
                    "AegisArchitecturePolicy",
                    out var wrappedPolicy))
            {
                if (wrappedPolicy.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException(
                        "'AegisArchitecturePolicy' must contain a JSON object.");
                }

                root = wrappedPolicy;
            }

            var policy = root.Deserialize<AegisArchitecturePolicy>(
                PolicyJsonOptions);

            if (policy is null)
            {
                throw new JsonException(
                    "The policy document deserialized to null.");
            }

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
            var previous = await _customReportRepo.GetLatestAsync(token)
                .ConfigureAwait(false);

            if (previous is null || previous.Id == currentReport.Id)
            {
                _logger.LogInformation(
                    "No previous report to compare with.");

                return;
            }

            var previousResults =
                await _customRuleResultRepo.GetPreviousReportAsync(
                    currentReport.ProjectName,
                    currentReport.DateCreated,
                    token).ConfigureAwait(false);

            var currentResults =
                await _customRuleResultRepo.GetViolationsByReportIdAsync(
                    currentReport.Id,
                    token).ConfigureAwait(false);

            var previousViolationKeys =
                new HashSet<(string RuleId, string Project)>(
                    previousResults.Select(
                        result => (result.RuleId, result.Project)));

            var newViolations = currentResults
                .Where(current =>
                    !previousViolationKeys.Contains(
                        (current.RuleId, current.Project)))
                .ToList();

            if (newViolations.Count > 0)
            {
                _logger.LogWarning(
                    "{Count} new violations detected.",
                    newViolations.Count);
            }
            else
            {
                _logger.LogInformation(
                    "No new violations introduced.");
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
                "Failed report comparison.");
        }
    }
}
