
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
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

    private static readonly Regex MediatorHandlerContractRegex = new(
        @"\bI(?:Request|Command|Query|Notification)Handler\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PortRegex = new(
        @"\bI[A-Za-z_$][A-Za-z0-9_$]*(?:Service|Repository|Client|Gateway|Port)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ServiceLikeMemberCallRegex = new(
        @"(?<receiver>_?[A-Za-z_$][A-Za-z0-9_$]*)\s*\.\s*(?<method>[A-Za-z_$][A-Za-z0-9_$]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StringLiteralRegex = new(
        """
        @"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`
        """,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BlockCommentRegex = new(
        @"/\*[\s\S]*?\*/",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LineCommentRegex = new(
        @"//[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonCommentRegex = new(
        @"\#[^\r\n]*",
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
        "ASP.NET",
        "Spring",
        "NestJS",
        "Angular",
        "FastAPI",
        "CleanArchitecture",
        "Hexagonal",
        "DDD",
        "Microservices"
    ];

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        var contextLanguage = Context?.Language ?? "Unknown";

        if (_policy.MediatorApplicableLanguages.TryGetValue(
                contextLanguage,
                out var enabled)
            && !enabled)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                $"Mediator evaluation disabled for {contextLanguage}.");

            return results;
        }

        if (Context is null)
            return results;

        var files = ResolveSourceFiles(projectPath, Extensions);

        var readableContents = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var unreadableFiles = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var source = await File.ReadAllTextAsync(file, token);
                readableContents[file] = SanitizeSource(
                    source,
                    ResolveLanguage(file));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    ex,
                    "Unable to read {FilePath} during mediator evaluation.",
                    file);
            }
            catch (UnauthorizedAccessException ex)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    ex,
                    "Access denied to {FilePath} during mediator evaluation.",
                    file);
            }
        }

        var mediatorFramework = DetectMediatorFramework(
            readableContents.Values);

        var fileMetrics = new List<Dictionary<string, double>>();

        foreach (var entry in readableContents)
        {
            token.ThrowIfCancellationRequested();

            var content = entry.Value;
            var language = ResolveLanguage(entry.Key);

            var handlerCount = CountHandlerClasses(content, language);
            var mediatorCalls = CountMediatorCalls(content);
            var handlerContractCount =
                MediatorHandlerContractRegex.Matches(content).Count;

            var directCouplingCalls = CountDirectServiceLikeCalls(content);
            var portReferences = PortRegex.Matches(content).Count;

            var handlerDensity = handlerCount /
                (double)Math.Max(1, _policy.MaxHandlersPerFile);

            var mediatorUsageRatio = Math.Min(
                mediatorCalls /
                (double)Math.Max(1, _policy.MaxMediatorCallsPerFile),
                1.0);

            var hasMediatorCalls = mediatorCalls > 0;
            var hasHandlerContract = handlerContractCount > 0;

            // This is a lexical indicator, not proof that dependencies
            // violate an architectural boundary.
            var couplingRisk = directCouplingCalls > 0 ? 1.0 : 0.0;
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
                ["CouplingRisk"] = couplingRisk,
                ["PortReferenceCount"] = portReferences,
                ["HexagonalPortAlignment"] = portAlignment,
                ["MediatorComplianceScore"] = compliance
            };

            fileMetrics.Add(metrics);

            results.Add(CreateFileResult(
                projectPath,
                entry.Key,
                language,
                mediatorFramework,
                metrics));
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
            $"Mediator evaluation completed: {fileMetrics.Count} files analyzed, " +
            $"{unreadableFiles} files unreadable.");

        return results;
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string projectPath,
        string file,
        string language,
        string mediatorFramework,
        Dictionary<string, double> metrics)
    {
        var result = new ArchitectureEvaluatorResult(Name, file)
        {
            Target = projectPath,
            Category = ArchitectureRuleCategory.DesignPatterns.ToString(),
            Layer = ResolveLayer(file),
            Metrics = metrics
        };

        result.Metadata[FilePathMetadataKey] = file;
        result.Metadata[EvaluationScopeMetadataKey] = FileScope;
        result.Metadata["Framework"] = mediatorFramework;
        result.Metadata["Language"] = language;
        result.Metadata["Layer"] = ResolveLayer(file);
        result.Metadata["ArchitectureStyle"] =
            Context?.ArchitectureStyle ?? "Unknown";
        result.Metadata["SemanticAnalysisPerformed"] = "false";
        result.Metadata["StructuralAnalysisHeuristic"] = "true";

        return result;
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

    private int CountMediatorCalls(string content)
    {
        var count = 0;

        foreach (var hint in _policy.MediatorMethodHints)
        {
            if (string.IsNullOrWhiteSpace(hint))
                continue;

            var pattern = $@"\b{Regex.Escape(hint)}\s*\(";

            count += Regex.Matches(
                content,
                pattern,
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant).Count;
        }

        return count;
    }

    private static int CountHandlerClasses(
        string content,
        string language)
    {
        var regex = language == "Python"
            ? PythonHandlerClassRegex
            : HandlerClassRegex;

        return regex.Matches(content).Count;
    }

    private static int CountDirectServiceLikeCalls(string content)
    {
        var count = 0;

        foreach (Match match in ServiceLikeMemberCallRegex.Matches(content))
        {
            var receiver = match.Groups["receiver"].Value.TrimStart('_');

            if (IsServiceLikeName(receiver))
                count++;
        }

        return count;
    }

    private static bool IsServiceLikeName(string name)
    {
        return name.EndsWith("Service", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Client", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Handler", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Gateway", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Provider", StringComparison.OrdinalIgnoreCase);
    }

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

    private static string SanitizeSource(
        string source,
        string language)
    {
        var sanitized = StringLiteralRegex.Replace(
            source,
            match => PreserveLayout(match.Value));

        if (language == "Python")
        {
            // Python triple-quoted strings/docstrings need a real lexer
            // for complete handling; this only removes ordinary literals.
            return PythonCommentRegex.Replace(
                sanitized,
                match => PreserveLayout(match.Value));
        }

        sanitized = BlockCommentRegex.Replace(
            sanitized,
            match => PreserveLayout(match.Value));

        return LineCommentRegex.Replace(
            sanitized,
            match => PreserveLayout(match.Value));
    }

    private static string PreserveLayout(string value)
    {
        var chars = value.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is not '\r' and not '\n')
                chars[i] = ' ';
        }

        return new string(chars);
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
