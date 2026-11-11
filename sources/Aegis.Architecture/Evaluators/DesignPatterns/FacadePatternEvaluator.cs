
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
        ^[ \t]*(?:(?:public|static|async|override|abstract)\s+)*
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
        "LanguageAgnostic"
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
            var sanitized = SourceCodeMasker.Mask(content);

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
                    content,
                    sanitized,
                    classMatch,
                    openingBrace + 1,
                    classBody,
                    dependencyNames,
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
        string source,
        string sanitized,
        Match classMatch,
        int bodyStart,
        string classBody,
        HashSet<string> dependencyNames,
        Dictionary<string, double> metrics,
        bool hasFacadeSuffix,
        bool structurallySupported)
    {
        var framework = ResolveFrameworkForFile(file, projectPath, Context!);
        var result = CreateResult(file, ArchitectureRuleCategory.DesignPatterns.ToString());

        result.Target = className;
        result.Language = language;
        result.Framework = framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = Context?.Confidence ?? 0;
        result.Metrics = metrics;

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
        result.Metadata["Framework"] = framework ?? "Unknown";
        result.Metadata["AnalysisComplete"] = bool.TrueString;

        var classLine = GetLineNumber(sanitized, classMatch.Index);
        var classSnippet = GetSourceLine(source, classLine);
        var dependencyRegex = language == "TypeScript"
            ? TypeScriptDependencyRegex
            : CSharpJavaDependencyRegex;
        var dependencyMatches = dependencyRegex.Matches(classBody)
            .Cast<Match>()
            .Where(match => dependencyNames.Contains(match.Groups["name"].Value))
            .ToArray();
        var publicMethodRegex = language == "TypeScript"
            ? TypeScriptPublicMethodRegex
            : PublicMethodRegex;
        var publicMethodMatches = publicMethodRegex.Matches(classBody)
            .Cast<Match>()
            .ToArray();
        var serviceCallMatches = MemberAccessCallRegex.Matches(classBody)
            .Cast<Match>()
            .Where(match =>
            {
                var receiver = match.Groups["receiver"].Value;
                return dependencyNames.Contains(receiver)
                    || dependencyNames.Contains(receiver.TrimStart('_'));
            })
            .ToArray();
        var constructorMatches = (language switch
        {
            "TypeScript" => ConstructorRegex.Matches(classBody),
            "Java" => JavaConstructorRegex.Matches(classBody),
            _ => CSharpConstructorRegex.Matches(classBody)
        }).Cast<Match>().ToArray();

        var dependencyEvidence = BuildMatchEvidence(
            dependencyMatches,
            source,
            sanitized,
            bodyStart,
            "No service-like dependency fields were detected.");
        var publicMethodEvidence = BuildMatchEvidence(
            publicMethodMatches,
            source,
            sanitized,
            bodyStart,
            "No public methods were detected in this facade candidate.");
        var serviceCallEvidence = BuildMatchEvidence(
            serviceCallMatches,
            source,
            sanitized,
            bodyStart,
            "No calls through recognized service-like dependencies were detected.");
        var constructorEvidence = BuildMatchEvidence(
            constructorMatches,
            source,
            sanitized,
            bodyStart,
            "No constructor signature was detected.");

        var publicMethodSummary =
            $"Facade '{className}' exposes {publicMethodMatches.Length} public method(s); " +
            $"configured maximum {_policy.MaxPublicMethodsPerFacade}; " +
            $"public surface ratio {metrics["PublicSurfaceRatio"]:0.##}.";
        AddMetricEvidence(
            result,
            "PublicMethodCount",
            $"{publicMethodSummary}{Environment.NewLine}{publicMethodEvidence}",
            publicMethodMatches.Length > 0
                ? GetLineNumber(sanitized, bodyStart + publicMethodMatches[0].Index)
                : classLine,
            publicMethodMatches.Length > 0
                ? GetSourceLine(
                    source,
                    GetLineNumber(sanitized, bodyStart + publicMethodMatches[0].Index))
                : classSnippet);
        AddMetricEvidence(
            result,
            "PublicSurfaceRatio",
            $"{publicMethodSummary}{Environment.NewLine}{publicMethodEvidence}",
            publicMethodMatches.Length > 0
                ? GetLineNumber(sanitized, bodyStart + publicMethodMatches[0].Index)
                : classLine,
            publicMethodMatches.Length > 0
                ? GetSourceLine(
                    source,
                    GetLineNumber(sanitized, bodyStart + publicMethodMatches[0].Index))
                : classSnippet);
        AddMetricEvidence(
            result,
            "DependencyCount",
            dependencyEvidence,
            dependencyMatches.Length > 0
                ? GetLineNumber(sanitized, bodyStart + dependencyMatches[0].Index)
                : classLine,
            dependencyMatches.Length > 0
                ? GetSourceLine(
                    source,
                    GetLineNumber(sanitized, bodyStart + dependencyMatches[0].Index))
                : classSnippet);
        AddMetricEvidence(
            result,
            "ServiceCallCount",
            serviceCallEvidence,
            serviceCallMatches.Length > 0
                ? GetLineNumber(sanitized, bodyStart + serviceCallMatches[0].Index)
                : classLine,
            serviceCallMatches.Length > 0
                ? GetSourceLine(
                    source,
                    GetLineNumber(sanitized, bodyStart + serviceCallMatches[0].Index))
                : classSnippet);
        AddMetricEvidence(
            result,
            "FacadeStructuralEvidence",
            $"Facade candidate '{className}': explicit suffix {hasFacadeSuffix}; " +
            $"structurally supported {structurallySupported}; dependencies {dependencyNames.Count}; " +
            $"service calls {serviceCallMatches.Length}; public methods {publicMethodMatches.Length}." +
            $"{Environment.NewLine}{dependencyEvidence}{Environment.NewLine}{constructorEvidence}{Environment.NewLine}{serviceCallEvidence}",
            classLine,
            classSnippet);

        result.Evidence = string.Join(
            Environment.NewLine,
            new[]
            {
                publicMethodSummary,
                dependencyEvidence,
                publicMethodEvidence,
                serviceCallEvidence,
                constructorEvidence
            });
        result.LineNumber = classLine;

        return result;
    }

    private static string BuildMatchEvidence(
        IReadOnlyCollection<Match> matches,
        string source,
        string sanitized,
        int bodyStart,
        string emptyEvidence)
    {
        if (matches.Count == 0)
            return emptyEvidence;

        return string.Join(
            Environment.NewLine,
            matches.Take(50).Select(match =>
            {
                var line = GetLineNumber(sanitized, bodyStart + match.Index);
                return $"Line {line}: {GetSourceLine(source, line)}";
            }));
    }

    private static void AddMetricEvidence(
        ArchitectureEvaluatorResult result,
        string metricKey,
        string evidence,
        int lineNumber,
        string codeSnippet)
    {
        result.Metadata[$"Evidence.{metricKey}"] = evidence;
        result.Metadata[$"EvidenceSummary.{metricKey}"] = evidence;
        result.Metadata[$"EvidenceScope.{metricKey}"] = "FileMetrics";
        result.Metadata[$"EvidenceLine.{metricKey}"] =
            lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata[$"CodeSnippet.{metricKey}"] = codeSnippet;
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
