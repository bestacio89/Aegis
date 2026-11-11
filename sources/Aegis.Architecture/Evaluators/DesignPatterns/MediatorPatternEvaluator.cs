
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Architecture.Evaluators.Analysis;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Collects heuristic evidence about Mediator pattern adoption and
/// communication boundaries.
///
/// Handler declarations, mediator call hints, service-like member calls,
/// and port-like identifiers are measured separately.
/// RuleEngine determines severity and remediation.
///
/// This evaluator does not perform semantic or AST analysis.
/// </summary>
public sealed class MediatorPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    private static readonly string[] Extensions =
    [
        ".cs",
        ".java",
        ".ts",
        ".py"
    ];

    private static readonly Regex HandlerClassRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*Handler)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonHandlerClassRegex = new(
        @"^[ \t]*class[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*Handler)\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline);

    private static readonly Regex MediatorImplementationRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*Mediator)\b[^{}]*\{|\bclass\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\b[^{}]*:\s*[^{}]*\bI[A-Za-z0-9_$]*Mediator\b[^{}]*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MediatorHandlerContractRegex = new(
        @"\bI(?:Request|Command|Query|Notification)Handler\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PortRegex = new(
        @"\bI[A-Za-z_$][A-Za-z0-9_$]*(?:Service|Repository|Client|Gateway|Port)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ServiceLikeMemberCallRegex = new(
        @"(?<receiver>_?[A-Za-z_$][A-Za-z0-9_$]*)\s*\.\s*(?<method>[A-Za-z_$][A-Za-z0-9_$]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public MediatorPatternEvaluator(
        ILogger<MediatorPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }

    public override string Name => "MediatorPatternEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript",
        "Python"
    ];

    public override string[] SupportedFrameworks =>
    [
        "LanguageAgnostic"
    ];

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (Context is null)
            return results;

        var files = ResolveSourceFiles(projectPath, Extensions);
        var readableContents = new Dictionary<
            string,
            (string Source, string Sanitized, string Language)>(
                StringComparer.OrdinalIgnoreCase);

        var unreadableFiles = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var source = await File.ReadAllTextAsync(file, token);
                var language = ResolveLanguage(file);
                readableContents[file] = (
                    source,
                    SourceCodeMasker.Mask(source),
                    language);
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException exception)
            {
                unreadableFiles++;
                _logger.LogWarning(
                    exception,
                    "Unable to read {FilePath} during mediator evaluation.",
                    file);
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFiles++;
                _logger.LogWarning(
                    exception,
                    "Access denied to {FilePath} during mediator evaluation.",
                    file);
            }
        }

        var mediatorFramework = DetectMediatorFramework(
            readableContents.Values.Select(item => item.Sanitized));

        var fileMetrics = new List<Dictionary<string, double>>();

        foreach (var entry in readableContents)
        {
            token.ThrowIfCancellationRequested();

            var file = entry.Key;
            var source = entry.Value.Source;
            var content = entry.Value.Sanitized;
            var language = entry.Value.Language;

            if (_policy.MediatorApplicableLanguages.TryGetValue(
                    language,
                    out var enabled)
                && !enabled)
            {
                continue;
            }

            var handlerMatches = FindHandlerMatches(content, language);
            var mediatorMatches = FindMediatorCallMatches(content);
            var contractMatches = MediatorHandlerContractRegex.Matches(content)
                .Cast<Match>()
                .ToArray();
            var portMatches = PortRegex.Matches(content)
                .Cast<Match>()
                .ToArray();

            var mediatorImplementation = language is "Python"
                ? null
                : MediatorImplementationRegex.Match(content);
            if (mediatorImplementation is { Success: false })
                mediatorImplementation = null;

            var directMatches = mediatorImplementation is null
                ? []
                : FindDirectServiceLikeMatches(content, mediatorImplementation);

            var handlerCount = handlerMatches.Count;
            var mediatorCalls = mediatorMatches.Count;
            var handlerContractCount = contractMatches.Length;
            var directCouplingCalls = directMatches.Count;
            var portReferences = portMatches.Length;

            var hasMediatorImplementation = mediatorImplementation is not null;
            var hasMediatorSignal = handlerCount > 0
                || mediatorCalls > 0
                || handlerContractCount > 0
                || hasMediatorImplementation;

            if (!hasMediatorSignal)
                continue;

            var handlerDensity = handlerCount /
                (double)Math.Max(1, _policy.MaxHandlersPerFile);

            var mediatorUsageRatio = Math.Min(
                mediatorCalls /
                (double)Math.Max(1, _policy.MaxMediatorCallsPerFile),
                1.0);

            var hasMediatorCalls = mediatorCalls > 0;
            var hasHandlerContract = handlerContractCount > 0;

            // Coupling risk is only measured on an actual mediator implementation.
            // A handler calling its injected repository/service is normal application
            // behavior and is not evidence of a centralized mediator.
            var couplingRisk = hasMediatorImplementation && directCouplingCalls > 0
                ? 1.0
                : 0.0;
            var portAlignment = portReferences > 0 ? 1.0 : 0.0;

            var compliance = ComputeCompliance(
                hasHandlerContract ? 1 : 0,
                mediatorUsageRatio,
                couplingRisk,
                handlerDensity,
                portAlignment);

            var metrics = new Dictionary<string, double>
            {
                ["HandlerCount"] = handlerCount,
                ["MediatorCallCount"] = mediatorCalls,
                ["MediatorHandlerContractCount"] = handlerContractCount,
                ["UsesMediator"] = hasMediatorCalls ? 1 : 0,
                ["HandlerDensity"] = handlerDensity,
                ["MediatorUsageRatio"] = mediatorUsageRatio,
                ["DirectServiceLikeCallCount"] = directCouplingCalls,
                ["PortReferenceCount"] = portReferences,
                ["HexagonalPortAlignment"] = portAlignment,
                ["MediatorComplianceScore"] = compliance
            };

            if (hasMediatorImplementation)
                metrics["CouplingRisk"] = couplingRisk;

            fileMetrics.Add(metrics);

            results.Add(CreateFileResult(
                projectPath,
                file,
                language,
                mediatorFramework,
                metrics,
                source,
                content,
                mediatorImplementation,
                handlerMatches,
                mediatorMatches,
                contractMatches,
                directMatches,
                portMatches));
        }

        results.Add(CreateSummary(
            projectPath,
            files.Count,
            readableContents.Count,
            unreadableFiles,
            mediatorFramework,
            fileMetrics));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Mediator evaluation completed: {fileMetrics.Count} mediator-related files analyzed, " +
            $"{unreadableFiles} files unreadable.");

        return results;
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string projectPath,
        string file,
        string language,
        string mediatorFramework,
        Dictionary<string, double> metrics,
        string source,
        string sanitized,
        Match? mediatorImplementation,
        IReadOnlyCollection<Match> handlerMatches,
        IReadOnlyCollection<Match> mediatorMatches,
        IReadOnlyCollection<Match> contractMatches,
        IReadOnlyCollection<Match> directMatches,
        IReadOnlyCollection<Match> portMatches)
    {
        var framework = ResolveFrameworkForFile(file, projectPath, Context!);
        var result = CreateResult(file, ArchitectureRuleCategory.DesignPatterns.ToString());

        result.Target = mediatorImplementation is null
            ? Path.GetFileName(file)
            : mediatorImplementation.Groups["name"].Value;
        result.Language = language;
        result.Framework = framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = Context?.Confidence ?? 0;
        result.Metrics = metrics;

        result.Metadata[FilePathMetadataKey] = file;
        result.Metadata[EvaluationScopeMetadataKey] = FileScope;
        result.Metadata["Framework"] = framework ?? "Unknown";
        result.Metadata["MediatorFramework"] = mediatorFramework;
        result.Metadata["Language"] = language;
        result.Metadata["Layer"] = result.Layer ?? "Unknown";
        result.Metadata["ArchitectureStyle"] =
            Context?.ArchitectureStyle ?? "Unknown";
        result.Metadata["SemanticAnalysisPerformed"] = "false";
        result.Metadata["StructuralAnalysisHeuristic"] = "true";
        result.Metadata["AnalysisComplete"] = "true";

        var evidence = new List<string>();
        AddMetricEvidence(
            result,
            evidence,
            "HandlerCount",
            handlerMatches,
            source,
            sanitized,
            "No handler class declarations were detected in this file.");
        AddMetricEvidence(
            result,
            evidence,
            "MediatorCallCount",
            mediatorMatches,
            source,
            sanitized,
            "No configured mediator method calls were detected in this file.");
        AddMetricEvidence(
            result,
            evidence,
            "MediatorHandlerContractCount",
            contractMatches,
            source,
            sanitized,
            "No mediator handler contracts were detected in this file.");
        AddMetricEvidence(
            result,
            evidence,
            "DirectServiceLikeCallCount",
            directMatches,
            source,
            sanitized,
            "No service-like member calls were detected within the mediator implementation.");
        AddMetricEvidence(
            result,
            evidence,
            "PortReferenceCount",
            portMatches,
            source,
            sanitized,
            "No port-like interface references were detected in this file.");
        AddMetricEvidence(
            result,
            evidence,
            "MediatorUsageRatio",
            mediatorMatches,
            source,
            sanitized,
            "No configured mediator method calls were detected in this file.");
        AddMetricEvidence(
            result,
            evidence,
            "HexagonalPortAlignment",
            portMatches,
            source,
            sanitized,
            "No port-like interface references were detected in this file.");

        if (mediatorImplementation is not null)
        {
            var couplingMatches = new[] { mediatorImplementation }
                .Concat(directMatches)
                .ToArray();
            var className = mediatorImplementation.Groups["name"].Value;
            var couplingDescription = directMatches.Count == 0
                ? $"Mediator implementation '{className}' has no detected service-like member calls."
                : $"Mediator implementation '{className}' contains {directMatches.Count} service-like member call(s).";

            AddMetricEvidence(
                result,
                evidence,
                "CouplingRisk",
                couplingMatches,
                source,
                sanitized,
                couplingDescription);
        }

        var complianceMatches = (mediatorImplementation is null
                ? Enumerable.Empty<Match>()
                : [mediatorImplementation])
            .Concat(handlerMatches)
            .Concat(mediatorMatches)
            .Concat(contractMatches)
            .Concat(directMatches)
            .Concat(portMatches)
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();

        AddMetricEvidence(
            result,
            evidence,
            "MediatorComplianceScore",
            complianceMatches,
            source,
            sanitized,
            "No mediator-related declarations or calls were detected in this file.");

        result.Evidence = string.Join(Environment.NewLine, evidence);
        result.LineNumber = mediatorImplementation is not null
            ? GetLineNumber(sanitized, mediatorImplementation.Index)
            : complianceMatches.Length > 0
                ? GetLineNumber(sanitized, complianceMatches[0].Index)
                : null;

        return result;
    }

    private static void AddMetricEvidence(
        ArchitectureEvaluatorResult result,
        ICollection<string> aggregateEvidence,
        string metricKey,
        IReadOnlyCollection<Match> matches,
        string source,
        string sanitized,
        string emptyDescription)
    {
        var evidence = matches.Count == 0
            ? emptyDescription
            : string.Join(
                Environment.NewLine,
                matches.Take(50).Select(match =>
                {
                    var line = GetLineNumber(sanitized, match.Index);
                    return $"Line {line}: {GetSourceLine(source, line)}";
                }));

        result.Metadata[$"Evidence.{metricKey}"] = evidence;
        result.Metadata[$"EvidenceScope.{metricKey}"] = "FileMetrics";
        aggregateEvidence.Add($"{metricKey}: {evidence}");

        var first = matches.FirstOrDefault();
        if (first is null)
            return;

        var lineNumber = GetLineNumber(sanitized, first.Index);
        result.Metadata[$"EvidenceLine.{metricKey}"] =
            lineNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata[$"CodeSnippet.{metricKey}"] =
            GetSourceLine(source, lineNumber);
    }

    private static IReadOnlyCollection<Match> FindHandlerMatches(
        string content,
        string language)
    {
        var regex = language == "Python"
            ? PythonHandlerClassRegex
            : HandlerClassRegex;

        return regex.Matches(content).Cast<Match>().ToArray();
    }

    private IReadOnlyCollection<Match> FindMediatorCallMatches(string content)
    {
        return _policy.MediatorMethodHints
            .Where(hint => !string.IsNullOrWhiteSpace(hint))
            .SelectMany(hint =>
            {
                var pattern = $@"\b{Regex.Escape(hint)}\s*\(";
                return Regex.Matches(
                        content,
                        pattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    .Cast<Match>();
            })
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();
    }

    private static IReadOnlyCollection<Match> FindDirectServiceLikeMatches(
        string content,
        Match mediatorImplementation)
    {
        var openingBrace = content.IndexOf('{', mediatorImplementation.Index);
        if (openingBrace < 0)
            return [];

        var closingBrace = FindMatchingBrace(content, openingBrace);
        if (closingBrace < 0)
            return [];

        return ServiceLikeMemberCallRegex.Matches(content)
            .Cast<Match>()
            .Where(match =>
                match.Index > openingBrace
                && match.Index < closingBrace
                && IsServiceLikeName(match.Groups["receiver"].Value.TrimStart('_')))
            .ToArray();
    }

    private static int FindMatchingBrace(string content, int openingBrace)
    {
        var depth = 0;
        for (var index = openingBrace; index < content.Length; index++)
        {
            if (content[index] == '{')
                depth++;
            else if (content[index] == '}' && --depth == 0)
                return index;
        }

        return -1;
    }

    private static int GetLineNumber(string content, int position)
    {
        var lineNumber = 1;
        for (var index = 0; index < position && index < content.Length; index++)
        {
            if (content[index] == '\n')
                lineNumber++;
        }

        return lineNumber;
    }

    private static string GetSourceLine(string content, int lineNumber)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        return lineNumber > 0 && lineNumber <= lines.Length
            ? lines[lineNumber - 1].Trim()
            : string.Empty;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        int discoveredFiles,
        int analyzedFiles,
        int unreadableFiles,
        string mediatorFramework,
        IReadOnlyCollection<Dictionary<string, double>> fileMetrics)
    {
        var averageCompliance = fileMetrics.Count == 0
            ? 0
            : fileMetrics.Average(metrics =>
                metrics.GetValueOrDefault("MediatorComplianceScore"));

        var filesWithCouplingSignals = fileMetrics.Count(metrics =>
            metrics.GetValueOrDefault("CouplingRisk") > 0);

        var couplingSignalRate = fileMetrics.Count == 0
            ? 0
            : filesWithCouplingSignals / (double)fileMetrics.Count;

        var summary = new ArchitectureEvaluatorResult(
            Name,
            "MediatorPatternSummary")
        {
            Target = projectPath,
            Category = ArchitectureRuleCategory.DesignPatterns.ToString(),
            Metrics =
            {
                ["DiscoveredFiles"] = discoveredFiles,
                ["AnalyzedFiles"] = analyzedFiles,
                ["UnreadableFiles"] = unreadableFiles,
                ["AverageComplianceScore"] = averageCompliance,
                ["FilesWithDirectCouplingSignals"] = filesWithCouplingSignals,
                ["DirectCouplingSignalRate"] = couplingSignalRate,
                ["OverallMediatorHealth"] =
                    averageCompliance * (1 - couplingSignalRate)
            }
        };

        summary.Metadata[EvaluationScopeMetadataKey] = ProjectScope;
        summary.Metadata["Evaluator"] = Name;
        summary.Metadata["MediatorFramework"] = mediatorFramework;
        summary.Metadata["SemanticAnalysisPerformed"] = "false";
        summary.Metadata["StructuralAnalysisHeuristic"] = "true";

        return summary;
    }

    private static int CountHandlerClasses(
        string content,
        string language) =>
        FindHandlerMatches(content, language).Count;

    private static int CountDirectServiceLikeCalls(string content)
    {
        return ServiceLikeMemberCallRegex.Matches(content)
            .Cast<Match>()
            .Count(match =>
                IsServiceLikeName(match.Groups["receiver"].Value.TrimStart('_')));
    }

    private static bool IsServiceLikeName(string name) =>
        name.EndsWith("Service", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Client", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Handler", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Gateway", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Provider", StringComparison.OrdinalIgnoreCase);

    private static string DetectMediatorFramework(
        IEnumerable<string> contents)
    {
        foreach (var content in contents)
        {
            if (content.Contains(
                    "Franz.Common.Mediator",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "FranzMediator";
            }

            if (content.Contains(
                    "MediatR",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "MediatR";
            }
        }

        return "GenericMediator";
    }

    private string ResolveLayer(string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(layer =>
                layer.Files.Contains(
                    file,
                    StringComparer.OrdinalIgnoreCase))
            ?.Name
            ?? "Unknown";
    }

    private static string ResolveLanguage(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".ts" => "TypeScript",
            ".py" => "Python",
            _ => "Unknown"
        };
    }

    private static double ComputeCompliance(
        double contract,
        double usage,
        double coupling,
        double density,
        double portAlignment)
    {
        var score =
            contract * 0.25 +
            usage * 0.30 +
            (1 - coupling) * 0.20 +
            (1 - Math.Min(density, 1)) * 0.10 +
            portAlignment * 0.15;

        return Math.Round(score * 100, 2);
    }
}
