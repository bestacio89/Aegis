
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
/// Collects heuristic structural evidence for the Facade pattern.
///
/// Measures candidate classes, dependency surfaces, public method surfaces,
/// and calls delegated to recognized service-like dependencies.
///
/// These measurements are structural facts, not proof of pattern compliance.
/// Rule interpretation belongs to the RuleEngine.
/// </summary>
public sealed class FacadePatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    private static readonly string[] Extensions =
    [
        ".cs",
        ".java",
        ".ts"
    ];

    private static readonly Regex ClassDeclarationRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);


    private static readonly Regex StringAndCharacterLiteralRegex = new(
        """
    @(?:""|[^"])*"|
    "(?:\\.|[^"\\])*"|
    '(?:\\.|[^'\\])*'|
    `(?:\\.|[^`\\])*`
    """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);


    private static readonly Regex BlockCommentRegex = new(
        @"/\*[\s\S]*?\*/",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LineCommentRegex = new(
        @"//[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CSharpJavaDependencyRegex = new(
        """
        \b(?:private|protected|public|internal)\s+
        (?:(?:static|readonly|final|volatile|transient)\s+)*
        [A-Za-z_$][A-Za-z0-9_$.<>?,\[\]]*\s+
        (?<name>_?[A-Za-z_$][A-Za-z0-9_$]*)
        \s*(?:[;=]|=>)
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex TypeScriptDependencyRegex = new(
        """
        \b(?:(?:private|protected|public|readonly|static)\s+)*
        (?<name>_?[A-Za-z_$][A-Za-z0-9_$]*)\s*\??\s*:\s*
        [A-Za-z_$][A-Za-z0-9_$.<>?,\[\] |&]*
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex PublicMethodRegex = new(
        """
        \bpublic\s+
        (?:(?:static|async|virtual|override|final|abstract|synchronized)\s+)*
        [A-Za-z_$][A-Za-z0-9_$.<>?,\[\]]*\s+
        [A-Za-z_$][A-Za-z0-9_$]*\s*\(
        |
        \bpublic\s+
        (?:(?:static|async|override|abstract)\s+)*
        [A-Za-z_$][A-Za-z0-9_$]*\s*\(
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex TypeScriptPublicMethodRegex = new(
        """
        \b(?:(?:public|protected|private|static|async|override|abstract)\s+)*
        (?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*
        \([^;{}]*\)\s*(?::\s*[^;{}]+)?\s*\{
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex ConstructorRegex = new(
        @"\bconstructor\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CSharpConstructorRegex = new(
        """
        \b(?:public|protected|internal)\s+
        [A-Za-z_][A-Za-z0-9_]*\s*\(
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex JavaConstructorRegex = new(
        """
        \b(?:public|protected)\s+
        [A-Za-z_$][A-Za-z0-9_$]*\s*\(
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex MemberAccessCallRegex = new(
        @"(?<receiver>_?[A-Za-z_$][A-Za-z0-9_$]*)\s*\.\s*[A-Za-z_$][A-Za-z0-9_$]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public FacadePatternEvaluator(
        ILogger<FacadePatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }

    public override string Name => "FacadePatternEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript"
    ];

    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "NestJS",
        "CleanArchitecture",
        "DDD"
    ];

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceFacadePattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Facade pattern evaluation disabled.");

            return results;
        }

        if (Context is null)
            return results;

        var files = ResolveSourceFiles(projectPath, Extensions);

        var analyzedFiles = 0;
        var unreadableFiles = 0;
        var candidateCount = 0;
        var structurallySupportedCount = 0;

        var aggregateMetrics = new List<Dictionary<string, double>>();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
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
                    "Could not read source file {FilePath} during facade evaluation.",
                    file);

                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    ex,
                    "Access denied to source file {FilePath} during facade evaluation.",
                    file);

                continue;
            }

            token.ThrowIfCancellationRequested();
            analyzedFiles++;

            var language = ResolveLanguage(file);
            var sanitized = SanitizeSource(content, language);

            foreach (Match classMatch in ClassDeclarationRegex.Matches(sanitized))
            {
                token.ThrowIfCancellationRequested();

                var openingBrace = sanitized.IndexOf(
                    '{',
                    classMatch.Index + classMatch.Length);

                if (openingBrace < 0)
                    continue;

                var closingBrace = FindMatchingBrace(sanitized, openingBrace);

                if (closingBrace < 0)
                    continue;

                var className = classMatch.Groups["name"].Value;
                var classBody = sanitized.Substring(
                    openingBrace + 1,
                    closingBrace - openingBrace - 1);

                var dependencyNames = FindDependencyNames(classBody, language);
                var publicMethodCount = CountPublicMethods(classBody, language);
                var serviceCallCount = CountCallsOnDependencies(
                    classBody,
                    dependencyNames);

                var isExplicitFacade = className.EndsWith(
                    "Facade",
                    StringComparison.OrdinalIgnoreCase);

                // Do not classify every class with dependencies as a facade.
                // Require an explicit name or multiple structural signals.
                var isStructuralCandidate =
                    dependencyNames.Count >= 2 &&
                    serviceCallCount >= 2 &&
                    publicMethodCount > 0;

                if (!isExplicitFacade && !isStructuralCandidate)
                    continue;

                candidateCount++;

                var hasConstructorInjection =
                    HasConstructor(language, classBody);

                var hasDependencySurface = dependencyNames.Count > 0;
                var hasDelegationCalls = serviceCallCount > 0;

                var evidenceCount = 0;

                if (hasDependencySurface)
                    evidenceCount++;

                if (hasDelegationCalls)
                    evidenceCount++;

                if (hasConstructorInjection)
                    evidenceCount++;

                if (isExplicitFacade)
                    evidenceCount++;

                var structurallySupported = evidenceCount >= 3;

                if (structurallySupported)
                    structurallySupportedCount++;

                var dependencyRatio = CalculateRatio(
                    dependencyNames.Count,
                    _policy.MaxDependenciesPerFacade);

                var publicSurfaceRatio = CalculateRatio(
                    publicMethodCount,
                    _policy.MaxPublicMethodsPerFacade);

                var orchestrationRatio = CalculateRatio(
                    serviceCallCount,
                    _policy.MaxServiceCallsPerFacade);

                var metrics = new Dictionary<string, double>
                {
                    ["DependencyCount"] = dependencyNames.Count,
                    ["PublicMethodCount"] = publicMethodCount,
                    ["ServiceCallCount"] = serviceCallCount,
                    ["DependencyRatio"] = dependencyRatio,
                    ["PublicSurfaceRatio"] = publicSurfaceRatio,
                    ["OrchestrationRatio"] = orchestrationRatio,
                    ["HasDependencySurface"] = hasDependencySurface ? 1 : 0,
                    ["HasConstructorInjection"] = hasConstructorInjection ? 1 : 0,
                    ["HasDelegationCalls"] = hasDelegationCalls ? 1 : 0,
                    ["HasFacadeSuffix"] = isExplicitFacade ? 1 : 0,
                    ["StructuralEvidenceCount"] = evidenceCount,
                    ["StructurallySupported"] = structurallySupported ? 1 : 0
                };

                aggregateMetrics.Add(metrics);

                results.Add(CreateClassResult(
                    projectPath,
                    file,
                    className,
                    language,
                    metrics,
                    isExplicitFacade,
                    structurallySupported));
            }
        }

        results.Add(CreateSummary(
            projectPath,
            files.Count,
            analyzedFiles,
            unreadableFiles,
            candidateCount,
            structurallySupportedCount,
            aggregateMetrics));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Facade evaluation completed: {candidateCount} candidates across " +
            $"{analyzedFiles}/{files.Count} readable source files; " +
            $"{unreadableFiles} files unreadable.");

        return results;
    }

    private ArchitectureEvaluatorResult CreateClassResult(
        string projectPath,
        string file,
        string className,
        string language,
        Dictionary<string, double> metrics,
        bool hasFacadeSuffix,
        bool structurallySupported)
    {
        var result = new ArchitectureEvaluatorResult(Name, className)
        {
            Target = file,
            Category = ArchitectureRuleCategory.DesignPatterns.ToString(),
            Metrics = metrics
        };

        result.Metadata[FilePathMetadataKey] = file;
        result.Metadata[EvaluationScopeMetadataKey] = FileScope;
        result.Metadata["ClassName"] = className;
        result.Metadata["MetricUnit.PublicSurfaceRatio"] = "RatioOfConfiguredMaximum";
        result.Metadata["Language"] = language;
        result.Metadata["Layer"] = ResolveLayer(file);
        result.Metadata["FacadeSuffixDetected"] = hasFacadeSuffix.ToString();
        result.Metadata["StructurallySupported"] = structurallySupported.ToString();
        result.Metadata["SemanticAnalysisPerformed"] = "false";
        result.Metadata["StructuralAnalysisHeuristic"] = "true";

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        int discoveredFiles,
        int analyzedFiles,
        int unreadableFiles,
        int candidateCount,
        int structurallySupportedCount,
        IReadOnlyCollection<Dictionary<string, double>> candidates)
    {
        var averageDependencies = AverageMetric(
            candidates,
            "DependencyCount");

        var averagePublicMethods = AverageMetric(
            candidates,
            "PublicMethodCount");

        var averageServiceCalls = AverageMetric(
            candidates,
            "ServiceCallCount");

        var summary = new ArchitectureEvaluatorResult(
            Name,
            "FacadePatternSummary")
        {
            Target = projectPath,
            Category = ArchitectureRuleCategory.DesignPatterns.ToString(),
            Metrics =
            {
                ["DiscoveredFiles"] = discoveredFiles,
                ["AnalyzedFiles"] = analyzedFiles,
                ["UnreadableFiles"] = unreadableFiles,
                ["FacadeCandidateCount"] = candidateCount,
                ["StructurallySupportedCandidateCount"] = structurallySupportedCount,
                ["AverageDependencyCount"] = averageDependencies,
                ["AveragePublicMethodCount"] = averagePublicMethods,
                ["AverageServiceCallCount"] = averageServiceCalls,
                ["StructuralSupportRate"] = CalculateRatio(
                    structurallySupportedCount,
                    candidateCount) * 100.0
            }
        };

        summary.Metadata[EvaluationScopeMetadataKey] = ProjectScope;
        summary.Metadata["SemanticAnalysisPerformed"] = "false";
        summary.Metadata["StructuralAnalysisHeuristic"] = "true";

        return summary;
    }

    private static HashSet<string> FindDependencyNames(
        string classBody,
        string language)
    {
        var regex = language == "TypeScript"
            ? TypeScriptDependencyRegex
            : CSharpJavaDependencyRegex;

        return regex.Matches(classBody)
            .Select(match => match.Groups["name"].Value)
            .Where(IsServiceLikeName)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsServiceLikeName(string name)
    {
        var normalized = name.TrimStart('_');

        return normalized.EndsWith("Service", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("Repository", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("Client", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("Gateway", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("Provider", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountCallsOnDependencies(
        string classBody,
        HashSet<string> dependencyNames)
    {
        if (dependencyNames.Count == 0)
            return 0;

        var count = 0;

        foreach (Match match in MemberAccessCallRegex.Matches(classBody))
        {
            var receiver = match.Groups["receiver"].Value;

            if (dependencyNames.Contains(receiver))
            {
                count++;
                continue;
            }

            var withoutUnderscore = receiver.TrimStart('_');

            if (dependencyNames.Contains(withoutUnderscore))
                count++;
        }

        return count;
    }

    private static int CountPublicMethods(
        string classBody,
        string language)
    {
        var regex = language == "TypeScript"
            ? TypeScriptPublicMethodRegex
            : PublicMethodRegex;

        return regex.Matches(classBody).Count;
    }

    private static bool HasConstructor(
        string language,
        string classBody)
    {
        return language switch
        {
            "TypeScript" => ConstructorRegex.IsMatch(classBody),
            "Java" => JavaConstructorRegex.IsMatch(classBody),
            _ => CSharpConstructorRegex.IsMatch(classBody)
        };
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
            _ => "Unknown"
        };
    }

    private static string SanitizeSource(
        string source,
        string language)
    {
        // Preserve source length and newlines so brace offsets remain valid.
        var sanitized = StringAndCharacterLiteralRegex.Replace(
            source,
            match => PreserveLayout(match.Value));

        if (language == "Python")
            return sanitized;

        sanitized = BlockCommentRegex.Replace(
            sanitized,
            match => PreserveLayout(match.Value));

        sanitized = LineCommentRegex.Replace(
            sanitized,
            match => PreserveLayout(match.Value));

        return sanitized;
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

    private static int FindMatchingBrace(
        string source,
        int openingBrace)
    {
        var depth = 0;

        for (var i = openingBrace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;

                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static double AverageMetric(
        IReadOnlyCollection<Dictionary<string, double>> candidates,
        string metricName)
    {
        if (candidates.Count == 0)
            return 0;

        return candidates.Average(candidate =>
            candidate.GetValueOrDefault(metricName));
    }

    private static double CalculateRatio(
        int value,
        int maximum)
    {
        return value / (double)Math.Max(1, maximum);
    }
}
