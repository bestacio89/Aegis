using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Evaluates lexical signals associated with the Strategy design pattern.
///
/// Detects:
/// - Strategy-named classes and interfaces
/// - Classes that appear to implement a Strategy-named abstraction
/// - Conditional dispatch signals involving strategy-related identifiers
/// - Naming convention adherence
///
/// Detection is heuristic. It does not prove polymorphic substitution,
/// behavioral equivalence, or complete Strategy-pattern implementation.
/// </summary>
public sealed class StrategyPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => nameof(StrategyPatternEvaluator);

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript"
    ];

    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "NestJS",
        "FastAPI"
    ];

    private static readonly Regex StringLiteralRegex = new(
        """
        @(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`
        """,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommentRegex = new(
        @"//[^\r\n]*|/\*[\s\S]*?\*/|#[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BraceClassRegex = new(
        """
        \bclass\s+(?<name>[A-Za-z_$]\w*)
        (?<header>[^{};]*)
        \{
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex PythonClassRegex = new(
        """
        ^[ \t]*class\s+(?<name>[A-Za-z_]\w*)
        (?<header>[ \t]*(?:\([^)]*\))?[ \t]*):
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex StrategyInterfaceRegex = new(
        @"\binterface\s+(?<name>I?[A-Za-z_$]\w*Strategy)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StrategyNameRegex = new(
        @"\b[A-Za-z_$]\w*Strategy\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConditionalDispatchRegex = new(
        """
        \b(?:if|switch|match)\s*
        (?:\([^)]*\)|[A-Za-z_$]\w*)
        """,
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex StrategyIdentifierRegex = new(
        @"\b[A-Za-z_$]\w*Strategy\b|\bstrategy\b",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    public StrategyPatternEvaluator(
        ILogger<StrategyPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceStrategyPattern)
        {
            _logger.LogInformation(
                "{Evaluator} disabled by policy.",
                Name);

            return results;
        }

        var files = ResolveSourceFiles(
            projectPath,
            ".cs",
            ".java",
            ".py",
            ".ts");

        var analyzedFiles = 0;
        var unreadableFiles = 0;
        var strategyCandidateCount = 0;
        var filesWithConditionalDispatch = 0;

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
                    "{Evaluator} could not read {FilePath}.",
                    Name,
                    file);

                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    ex,
                    "{Evaluator} has no access to {FilePath}.",
                    Name,
                    file);

                continue;
            }

            analyzedFiles++;

            var language = DetectLanguage(file);
            var sanitized = RemoveCommentsAndStrings(content);

            var interfaceNames = StrategyInterfaceRegex
                .Matches(sanitized)
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var classRegex = language == "Python"
                ? PythonClassRegex
                : BraceClassRegex;

            var classMatches = classRegex.Matches(sanitized);
            var fileHasConditionalDispatch =
                HasConditionalStrategyDispatch(sanitized);

            if (fileHasConditionalDispatch)
            {
                filesWithConditionalDispatch++;
            }

            var fileCandidateCount = 0;

            foreach (Match classMatch in classMatches)
            {
                token.ThrowIfCancellationRequested();

                var className = classMatch.Groups["name"].Value;
                var classHeader = classMatch.Groups["header"].Value;

                var implementsStrategyInterface =
                    interfaceNames.Any(interfaceName =>
                        classHeader.Contains(
                            interfaceName,
                            StringComparison.Ordinal));

                var strategyNamed =
                    StrategyNameRegex.IsMatch(className);

                if (!strategyNamed && !implementsStrategyInterface)
                {
                    continue;
                }

                if (!TryExtractClassBody(
                    sanitized,
                    classMatch,
                    language,
                    out var classBody))
                {
                    _logger.LogDebug(
                        "{Evaluator} could not isolate class body for {ClassName} in {FilePath}.",
                        Name,
                        className,
                        file);

                    continue;
                }

                fileCandidateCount++;
                strategyCandidateCount++;

                // Count only strategy-related conditionals within this class.
                var classHasConditionalDispatch =
                    HasConditionalStrategyDispatch(classBody);

                var hasMatchingInterface =
                    implementsStrategyInterface;

                var interfaceAdherence =
                    hasMatchingInterface ? 1d : 0d;

                // Multiple Strategy classes in one file are not a violation.
                // This score only indicates that this candidate was isolated
                // for analysis, not that architectural isolation was proven.
                const double isolationScore = 1d;

                var namingCompliance = IsNamingConventionSatisfied(className)
                    ? 1d
                    : 0d;

                var conditionalPenalty =
                    classHasConditionalDispatch ? 1d : 0d;

                var complianceScore = ComputeCompliance(
                    interfaceAdherence,
                    isolationScore,
                    namingCompliance,
                    conditionalPenalty);

                var result = new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        ArchitectureRuleCategory.DesignPatterns.ToString()
                };

                result.Metrics["StrategyCount"] = 1;
                result.Metrics["HasStrategy"] = 1;
                result.Metrics["InterfaceAdherence"] = interfaceAdherence;
                result.Metrics["IsolationScore"] = isolationScore;
                result.Metrics["ConditionalPenalty"] = conditionalPenalty;
                result.Metrics["NamingCompliance"] = namingCompliance;
                result.Metrics["StrategyComplianceScore"] = complianceScore;

                result.Metrics["HasMatchingStrategyInterface"] =
                    hasMatchingInterface ? 1 : 0;

                result.Metrics["ConditionalDispatchSignalCount"] =
                    CountConditionalStrategyDispatch(classBody);

                result.Metadata[BaseArchitectureEvaluator.FilePathMetadataKey] =
                    file;
                result.Metadata[BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
                    BaseArchitectureEvaluator.FileScope;
                result.Metadata["FileName"] = Path.GetFileName(file);
                result.Metadata["ClassName"] = className;
                result.Metadata["Language"] = language;
                result.Metadata["StrategyInterfaceNames"] =
                    string.Join(", ", interfaceNames);
                result.Metadata["HasMatchingStrategyInterface"] =
                    hasMatchingInterface.ToString();
                result.Metadata["UsesStrategyConditionalLogic"] =
                    classHasConditionalDispatch.ToString();
                result.Metadata["NamingConventionSatisfied"] =
                    (namingCompliance > 0).ToString();
                result.Metadata["IsolationVerified"] =
                    bool.FalseString;
                result.Metadata["SemanticAnalysisPerformed"] =
                    bool.FalseString;
                result.Metadata["PatternSuffix"] =
                    _policy.StrategySuffix ?? string.Empty;

                results.Add(result);
            }

            // An interface-only file is useful evidence, but is not counted
            // as a Strategy implementation without an associated class.
            if (fileCandidateCount == 0 && interfaceNames.Length > 0)
            {
                var interfaceResult = new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        ArchitectureRuleCategory.DesignPatterns.ToString()
                };

                interfaceResult.Metrics["StrategyCount"] = 0;
                interfaceResult.Metrics["HasStrategy"] = 0;
                interfaceResult.Metrics["InterfaceAdherence"] = 1d;
                interfaceResult.Metrics["IsolationScore"] = 0d;
                interfaceResult.Metrics["ConditionalPenalty"] =
                    fileHasConditionalDispatch ? 1d : 0d;
                interfaceResult.Metrics["NamingCompliance"] = 0d;
                interfaceResult.Metrics["StrategyComplianceScore"] = 0d;

                interfaceResult.Metrics["HasMatchingStrategyInterface"] = 0;
                interfaceResult.Metrics["ConditionalDispatchSignalCount"] =
                    CountConditionalStrategyDispatch(sanitized);

                interfaceResult.Metadata[
                    BaseArchitectureEvaluator.FilePathMetadataKey] = file;
                interfaceResult.Metadata[
                    BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
                    BaseArchitectureEvaluator.FileScope;
                interfaceResult.Metadata["FileName"] = Path.GetFileName(file);
                interfaceResult.Metadata["Language"] = language;
                interfaceResult.Metadata["StrategyInterfaceNames"] =
                    string.Join(", ", interfaceNames);
                interfaceResult.Metadata["InterfaceOnlyEvidence"] =
                    bool.TrueString;
                interfaceResult.Metadata["SemanticAnalysisPerformed"] =
                    bool.FalseString;

                results.Add(interfaceResult);
            }
        }

        // Aggregate only implementation candidates. Interface-only evidence
        // is excluded from implementation compliance averages.
        var implementationResults = results
            .Where(result =>
                result.Metadata.GetValueOrDefault("ClassName") is not null)
            .ToList();

        var implementationCount = implementationResults.Count;

        var averageScore = AverageMetric(
            implementationResults,
            "StrategyComplianceScore");

        var averageInterfaceAdherence = AverageMetric(
            implementationResults,
            "InterfaceAdherence");

        var conditionalViolations = implementationResults.Count(
            result => GetMetric(result, "ConditionalPenalty") > 0);

        var namingViolations = implementationResults.Count(
            result => GetMetric(result, "NamingCompliance") == 0);

        var summary = new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            Category =
                ArchitectureRuleCategory.DesignPatterns.ToString()
        };

        summary.Metrics["DiscoveredFiles"] = files.Count;
        summary.Metrics["AnalyzedFiles"] = analyzedFiles;
        summary.Metrics["UnreadableFiles"] = unreadableFiles;
        summary.Metrics["StrategyCount"] = implementationCount;
        summary.Metrics["AverageComplianceScore"] = averageScore;
        summary.Metrics["AverageInterfaceAdherence"] =
            averageInterfaceAdherence;
        summary.Metrics["ConditionalViolationCount"] = conditionalViolations;
        summary.Metrics["NamingViolationCount"] = namingViolations;
        summary.Metrics["FilesWithConditionalDispatch"] =
            filesWithConditionalDispatch;

        // Conditional logic is not automatically an architectural violation.
        // Keep this health figure transparent and based on the existing
        // evaluator signals rather than treating multiple classes per file
        // as a violation.
        var conditionalViolationRate = implementationCount > 0
            ? conditionalViolations / (double)implementationCount
            : 0d;

        summary.Metrics["OverallStrategyHealth"] =
            implementationCount > 0
                ? averageScore * (1d - conditionalViolationRate)
                : 0d;

        summary.Metadata[
            BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
            BaseArchitectureEvaluator.ProjectScope;
        summary.Metadata["Evaluator"] = Name;
        summary.Metadata["PolicyEnabled"] =
            _policy.EnforceStrategyPattern.ToString();
        summary.Metadata["SemanticAnalysisPerformed"] =
            bool.FalseString;
        summary.Metadata["EmptyImplementationSet"] =
            (implementationCount == 0).ToString();

        results.Add(summary);

        _logger.LogInformation(
            "{Evaluator} completed: {DiscoveredFiles} files discovered, " +
            "{AnalyzedFiles} readable files, {StrategyCount} implementation candidates, " +
            "{UnreadableFiles} unreadable files.",
            Name,
            files.Count,
            analyzedFiles,
            implementationCount,
            unreadableFiles);

        return results;
    }

    private static string RemoveCommentsAndStrings(string source)
    {
        var withoutStrings = StringLiteralRegex.Replace(
            source,
            match => MaskPreservingLineBreaks(match.Value));

        return CommentRegex.Replace(
            withoutStrings,
            match => MaskPreservingLineBreaks(match.Value));
    }

    private static string MaskPreservingLineBreaks(string value)
    {
        var chars = value.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is not '\r' and not '\n')
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    private static bool HasConditionalStrategyDispatch(string source)
    {
        foreach (Match match in ConditionalDispatchRegex.Matches(source))
        {
            var end = Math.Min(
                source.Length,
                match.Index + Math.Max(match.Length, 1) + 160);

            var nearby = source.Substring(
                match.Index,
                end - match.Index);

            if (StrategyIdentifierRegex.IsMatch(nearby))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountConditionalStrategyDispatch(string source)
    {
        var count = 0;

        foreach (Match match in ConditionalDispatchRegex.Matches(source))
        {
            var end = Math.Min(
                source.Length,
                match.Index + Math.Max(match.Length, 1) + 160);

            var nearby = source.Substring(
                match.Index,
                end - match.Index);

            if (StrategyIdentifierRegex.IsMatch(nearby))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryExtractClassBody(
        string source,
        Match classMatch,
        string language,
        out string body)
    {
        body = string.Empty;

        if (language == "Python")
        {
            var newline = source.IndexOf(
                '\n',
                classMatch.Index + classMatch.Length);

            if (newline < 0)
            {
                return false;
            }

            var lineStart = source.LastIndexOf(
                '\n',
                Math.Max(0, classMatch.Index - 1));

            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            var classIndent = CountIndent(
                source,
                lineStart,
                classMatch.Index);

            var bodyStart = newline + 1;
            var position = bodyStart;

            while (position < source.Length)
            {
                var lineEnd = source.IndexOf('\n', position);

                if (lineEnd < 0)
                {
                    lineEnd = source.Length;
                }

                var line = source.AsSpan(position, lineEnd - position);
                var firstNonWhitespace = 0;

                while (firstNonWhitespace < line.Length &&
                       line[firstNonWhitespace] is ' ' or '\t' or '\r')
                {
                    firstNonWhitespace++;
                }

                if (firstNonWhitespace < line.Length)
                {
                    var indent = CountIndent(
                        source,
                        position,
                        position + firstNonWhitespace);

                    if (indent <= classIndent)
                    {
                        body = source.Substring(
                            bodyStart,
                            position - bodyStart);

                        return true;
                    }
                }

                position = lineEnd < source.Length
                    ? lineEnd + 1
                    : source.Length;
            }

            body = source.Substring(bodyStart);
            return true;
        }

        var openingBrace = source.IndexOf(
            '{',
            classMatch.Index,
            classMatch.Length);

        if (openingBrace < 0)
        {
            return false;
        }

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
                {
                    body = source.Substring(
                        openingBrace + 1,
                        i - openingBrace - 1);

                    return true;
                }
            }
        }

        return false;
    }

    private static int CountIndent(
        string source,
        int start,
        int end)
    {
        var indent = 0;

        for (var i = start; i < end && i < source.Length; i++)
        {
            indent += source[i] switch
            {
                '\t' => 4,
                ' ' => 1,
                _ => 0
            };
        }

        return indent;
    }

    private bool IsNamingConventionSatisfied(string className)
    {
        var suffix = _policy.StrategySuffix;

        if (string.IsNullOrWhiteSpace(suffix))
        {
            return true;
        }

        return className.EndsWith(
            suffix,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectLanguage(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            ".ts" => "TypeScript",
            _ => "Unknown"
        };

    private static double ComputeCompliance(
        double interfaceAdherence,
        double isolationScore,
        double namingCompliance,
        double conditionalPenalty)
    {
        var score =
            interfaceAdherence * 0.35 +
            isolationScore * 0.25 +
            namingCompliance * 0.15 +
            (1d - conditionalPenalty) * 0.25;

        return Math.Round(score * 100d, 2);
    }

    private static double AverageMetric(
        IEnumerable<ArchitectureEvaluatorResult> results,
        string metricName)
    {
        var values = results
            .Select(result => GetMetric(result, metricName))
            .ToList();

        return values.Count > 0
            ? values.Average()
            : 0d;
    }

    private static double GetMetric(
        ArchitectureEvaluatorResult result,
        string metricName)
    {
        return result.Metrics.GetValueOrDefault(metricName, 0d);
    }
}