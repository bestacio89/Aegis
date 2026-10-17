
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Collects heuristic structural evidence for the Factory pattern.
///
/// Metrics are scoped to individual factory candidates where possible.
/// These measurements do not establish semantic pattern compliance.
/// Rule interpretation belongs to the RuleEngine.
/// </summary>
public sealed class FactoryPatternEvaluator
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

    private static readonly Regex FactoryClassRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*Factory)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonFactoryClassRegex = new(
        @"^[ \t]*class[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*Factory)\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline);

    private static readonly Regex InterfaceRegex = new(
        @"\binterface\s+(?<name>I[A-Za-z_$][A-Za-z0-9_$]*Factory)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NewObjectRegex = new(
        @"\bnew\s+[A-Za-z_$][A-Za-z0-9_$.<>?,\[\]]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonConstructionRegex = new(
        @"\b[A-Z][A-Za-z0-9_]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConcreteReturnRegex = new(
        @"\breturn\s+new\s+[A-Za-z_$][A-Za-z0-9_$.<>?,\[\]]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonReturnRegex = new(
        @"\breturn\s+[A-Z][A-Za-z0-9_]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InfrastructureTypeRegex = new(
        @"\b(?:HttpClient|FileStream|SqlConnection|DbContext|Repository)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);


    private static readonly Regex StringLiteralRegex = new(
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

    private static readonly Regex PythonCommentRegex = new(
        @"\#[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public FactoryPatternEvaluator(
        ILogger<FactoryPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }

    public override string Name => "FactoryPatternEvaluator";

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
        "Angular",
        "Flask"
    ];

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (Context is null)
            return results;

        if (!_policy.EnforceFactoryPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Factory pattern evaluation disabled.");

            return results;
        }

        var files = ResolveSourceFiles(projectPath, Extensions);
        var readableFiles = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var unreadableFiles = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                readableFiles[file] =
                    await File.ReadAllTextAsync(file, token);
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
                    "Unable to read {FilePath} during factory evaluation.",
                    file);
            }
            catch (UnauthorizedAccessException ex)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    ex,
                    "Access denied to {FilePath} during factory evaluation.",
                    file);
            }
        }

        var sanitizedFiles = readableFiles.ToDictionary(
            pair => pair.Key,
            pair => SanitizeSource(
                pair.Value,
                ResolveLanguage(pair.Key)),
            StringComparer.OrdinalIgnoreCase);

        var interfaces = sanitizedFiles.Values
            .SelectMany(content =>
                InterfaceRegex.Matches(content)
                    .Select(match => match.Groups["name"].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidateMetrics = new List<Dictionary<string, double>>();

        foreach (var source in sanitizedFiles)
        {
            token.ThrowIfCancellationRequested();

            var language = ResolveLanguage(source.Key);
            var classRegex = language == "Python"
                ? PythonFactoryClassRegex
                : FactoryClassRegex;

            foreach (Match match in classRegex.Matches(source.Value))
            {
                token.ThrowIfCancellationRequested();

                var factoryName = match.Groups["name"].Value;

                if (!TryExtractClassBody(
                        source.Value,
                        match,
                        language,
                        out var classBody))
                {
                    _logger.LogDebug(
                        "Could not isolate class body for {ClassName} in {FilePath}.",
                        factoryName,
                        source.Key);

                    continue;
                }

                var expectedInterface = $"I{factoryName}";
                var hasInterface = interfaces.Contains(expectedInterface);

                var instantiations = language == "Python"
                    ? PythonConstructionRegex.Matches(classBody).Count
                    : NewObjectRegex.Matches(classBody).Count;

                var returnsConcrete = language == "Python"
                    ? PythonReturnRegex.IsMatch(classBody)
                    : ConcreteReturnRegex.IsMatch(classBody);

                var infrastructureLeaks =
                    InfrastructureTypeRegex.Matches(classBody).Count;

                var instantiationRatio =
                    instantiations /
                    (double)Math.Max(
                        1,
                        _policy.MaxFactoryInstantiations);

                var compliance = ComputeCompliance(
                    hasInterface ? 1 : 0,
                    instantiationRatio,
                    infrastructureLeaks > 0 ? 1 : 0,
                    returnsConcrete ? 1 : 0);

                var metrics = new Dictionary<string, double>
                {
                    ["AbstractionScore"] = hasInterface ? 1 : 0,
                    ["InstantiationCount"] = instantiations,
                    ["InstantiationRatio"] = instantiationRatio,
                    ["InfrastructureLeakCount"] = infrastructureLeaks,
                    ["ReturnsConcrete"] = returnsConcrete ? 1 : 0,
                    ["FactoryComplianceScore"] = compliance
                };

                candidateMetrics.Add(metrics);

                results.Add(CreateResult(
                    projectPath,
                    source.Key,
                    factoryName,
                    expectedInterface,
                    language,
                    hasInterface,
                    metrics));
            }
        }

        results.Add(CreateSummary(
            projectPath,
            files.Count,
            readableFiles.Count,
            unreadableFiles,
            candidateMetrics));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Factory evaluation completed: {candidateMetrics.Count} candidates, " +
            $"{readableFiles.Count}/{files.Count} readable files, " +
            $"{unreadableFiles} unreadable files.");

        return results;
    }

    private ArchitectureEvaluatorResult CreateResult(
        string projectPath,
        string file,
        string factoryName,
        string expectedInterface,
        string language,
        bool hasInterface,
        Dictionary<string, double> metrics)
    {
        var result = new ArchitectureEvaluatorResult(Name, factoryName)
        {
            ProjectName = Context?.ProjectName ?? string.Empty,
            Language = language,
            Framework = Context?.Framework,
            Layer = ResolveLayer(file),
            DetectionConfidence = Context?.Confidence ?? 0,
            Category = "DesignPattern",
            Metrics = metrics
        };

        result.Metadata[FilePathMetadataKey] = file;
        result.Metadata[EvaluationScopeMetadataKey] = FileScope;
        result.Metadata["FactoryName"] = factoryName;
        result.Metadata["ExpectedInterface"] = expectedInterface;
        result.Metadata["ExpectedInterfaceFound"] = hasInterface.ToString();
        result.Metadata["Language"] = language;
        result.Metadata["Framework"] = Context?.Framework ?? "Unknown";
        result.Metadata["SemanticAnalysisPerformed"] = "false";
        result.Metadata["StructuralAnalysisHeuristic"] = "true";

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        int discoveredFiles,
        int analyzedFiles,
        int unreadableFiles,
        IReadOnlyCollection<Dictionary<string, double>> candidates)
    {
        var averageCompliance = candidates.Count == 0
            ? 0
            : candidates.Average(candidate =>
                candidate.GetValueOrDefault("FactoryComplianceScore"));

        var summary = new ArchitectureEvaluatorResult(
            Name,
            "FactoryPatternSummary")
        {
            ProjectName = Context?.ProjectName ?? string.Empty,
            Category = "DesignPatternSummary",
            Metrics =
            {
                ["DiscoveredFiles"] = discoveredFiles,
                ["AnalyzedFiles"] = analyzedFiles,
                ["UnreadableFiles"] = unreadableFiles,
                ["FactoryCount"] = candidates.Count,
                ["AverageComplianceScore"] = averageCompliance
            }
        };

        summary.Metadata[EvaluationScopeMetadataKey] = ProjectScope;
        summary.Metadata["Evaluator"] = Name;
        summary.Metadata["SemanticAnalysisPerformed"] = "false";
        summary.Metadata["StructuralAnalysisHeuristic"] = "true";

        return summary;
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

    private static bool TryExtractClassBody(
        string source,
        Match classMatch,
        string language,
        out string classBody)
    {
        classBody = string.Empty;

        if (language == "Python")
        {
            var lineEnd = source.IndexOf('\n', classMatch.Index);

            if (lineEnd < 0)
                lineEnd = source.Length;

            var headerIndent = CountIndentation(
                source,
                classMatch.Index);

            var bodyStart = lineEnd < source.Length
                ? lineEnd + 1
                : lineEnd;

            var cursor = bodyStart;

            while (cursor < source.Length)
            {
                var nextLineEnd = source.IndexOf('\n', cursor);

                if (nextLineEnd < 0)
                    nextLineEnd = source.Length;

                var line = source.AsSpan(
                    cursor,
                    nextLineEnd - cursor);

                if (!string.IsNullOrWhiteSpace(line.ToString()))
                {
                    var indentation = CountIndentation(source, cursor);

                    if (indentation <= headerIndent)
                        break;
                }

                cursor = nextLineEnd < source.Length
                    ? nextLineEnd + 1
                    : nextLineEnd;
            }

            classBody = source.Substring(
                bodyStart,
                cursor - bodyStart);

            return true;
        }

        var openingBrace = source.IndexOf(
            '{',
            classMatch.Index + classMatch.Length);

        if (openingBrace < 0)
            return false;

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
                    classBody = source.Substring(
                        openingBrace + 1,
                        i - openingBrace - 1);

                    return true;
                }
            }
        }

        return false;
    }

    private static int CountIndentation(
        string source,
        int position)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, position - 1));

        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var indentation = 0;

        for (var i = lineStart; i < source.Length && i < position; i++)
        {
            if (source[i] == ' ')
            {
                indentation++;
            }
            else if (source[i] == '\t')
            {
                indentation += 4;
            }
            else
            {
                break;
            }
        }

        return indentation;
    }

    private static double ComputeCompliance(
        double abstraction,
        double instantiationRatio,
        double leakRatio,
        double concretePenalty)
    {
        var score =
            abstraction * 0.4 +
            (1 - Math.Min(instantiationRatio, 1)) * 0.25 +
            (1 - leakRatio) * 0.15 +
            (1 - concretePenalty) * 0.2;

        return Math.Round(score * 100, 2);
    }
}
