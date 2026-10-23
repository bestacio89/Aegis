
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
/// Collects heuristic structural measurements for potential God Classes.
///
/// Measurements are scoped to individual classes where the source syntax
/// can be isolated. RuleEngine determines severity and remediation.
/// This evaluator does not perform semantic or AST analysis.
/// </summary>
public sealed class GodClassEvaluator
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

    private static readonly Regex ClassRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonClassRegex = new(
        @"^[ \t]*class[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline);

    // Matches common quoted strings, character literals, C# verbatim strings,
    // and TypeScript template literals. This is intentionally a lexical
    // approximation; it is not a complete language lexer.
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

    private static readonly Regex CSharpJavaMethodRegex = new(
        """
        \b(?:(?:public|private|protected|internal|static|virtual|override|abstract|final|synchronized|async|extern|native)\s+)+
        (?:<[^>{}]+>\s*)?
        [A-Za-z_$][A-Za-z0-9_.$<>,?\[\]]*\s+
        [A-Za-z_$][A-Za-z0-9_$]*\s*\(
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex TypeScriptMethodRegex = new(
        """
        ^[ \t]*
        (?:(?:public|private|protected|static|async|override|abstract|readonly)\s+)*
        [A-Za-z_$][A-Za-z0-9_$]*\s*
        (?:<[^>{}]+>\s*)?
        \([^;{}]*\)\s*
        (?::\s*[^;{}=]+)?\s*
        \{
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace |
        RegexOptions.Multiline);

    private static readonly Regex PythonMethodRegex = new(
        @"^[ \t]*def[ \t]+[A-Za-z_][A-Za-z0-9_]*[ \t]*\(",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline);

    private static readonly Regex CSharpPropertyRegex = new(
        """
        \b(?:(?:public|private|protected|internal|static|virtual|override|abstract|readonly|required|new)\s+)+
        [A-Za-z_$][A-Za-z0-9_.$<>,?\[\]]*\s+
        [A-Za-z_$][A-Za-z0-9_$]*\s*
        \{[^{}]*(?:\bget\b|\bset\b|\binit\b)[^{}]*\}
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex CSharpJavaFieldRegex = new(
        """
        \b(?:(?:public|private|protected|internal|static|readonly|final|volatile|transient)\s+)+
        [A-Za-z_$][A-Za-z0-9_.$<>,?\[\]]*\s+
        [A-Za-z_$][A-Za-z0-9_$]*\s*(?:=|;)
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex TypeScriptPropertyRegex = new(
        """
        ^[ \t]*
        (?:(?:public|private|protected|readonly|static|override)\s+)*
        [A-Za-z_$][A-Za-z0-9_$]*\s*\??\s*:\s*
        [^=;(){}]+
        (?:=[^;]+)?;?
        $
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace |
        RegexOptions.Multiline);

    private static readonly Regex PythonPropertyRegex = new(
        @"^[ \t]*@[A-Za-z_][A-Za-z0-9_.]*\.setter[ \t]*$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline);

    public GodClassEvaluator(
        ILogger<GodClassEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }

    public override string Name => "GodClassEvaluator";

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

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (Context is null)
            return results;

        if (!_policy.DetectGodClasses)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "God class evaluation disabled.");

            return results;
        }

        var files = ResolveSourceFiles(projectPath, Extensions);
        var analyzedFiles = 0;
        var unreadableFiles = 0;
        var classMetrics = new List<Dictionary<string, double>>();

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
                    "Unable to read {FilePath} during God Class evaluation.",
                    file);

                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    ex,
                    "Access denied to {FilePath} during God Class evaluation.",
                    file);

                continue;
            }

            analyzedFiles++;

            var language = ResolveLanguage(file);
            var sanitized = SanitizeSource(content, language);
            var classRegex = language == "Python"
                ? PythonClassRegex
                : ClassRegex;

            foreach (Match match in classRegex.Matches(sanitized))
            {
                token.ThrowIfCancellationRequested();

                var className = match.Groups["name"].Value;

                if (!TryExtractClassBody(
                        sanitized,
                        match,
                        language,
                        out var classBody))
                {
                    _logger.LogDebug(
                        "Could not isolate class {ClassName} in {FilePath}.",
                        className,
                        file);

                    continue;
                }

                var methods = CountMethods(classBody, language);
                var properties = CountProperties(classBody, language);
                var lines = CountLines(classBody);
                var layer = ResolveLayer(file);
                var scaling = GetScalingFactor(layer);

                var maxMethods = Math.Max(
                    1,
                    (int)(_policy.MaxMethodsPerClass * scaling));

                var maxLines = Math.Max(
                    1,
                    (int)(_policy.MaxLinesPerClass * scaling));

                var lineRatio = lines / (double)maxLines;
                var methodRatio = methods / (double)maxMethods;

                var propertyRatio = methods + properties == 0
                    ? 0
                    : properties / (double)(methods + properties);

                var methodDensity = methods / (double)Math.Max(1, lines);

                // A God Class signal should require both excessive size and
                // excessive behavioral surface. A data-heavy seeder can be
                // large without centralizing many responsibilities; property
                // count alone is not evidence of a God Class.
                var severity = ComputeGodFactor(lineRatio, methodRatio);

                var metrics = new Dictionary<string, double>
                {
                    ["LineCount"] = lines,
                    ["MethodCount"] = methods,
                    ["PropertyCount"] = properties,
                    ["MethodDensity"] = methodDensity,
                    ["PropertyRatio"] = propertyRatio,
                    ["LineLimitRatio"] = lineRatio,
                    ["MethodLimitRatio"] = methodRatio,
                    ["GodClassSeverity"] = severity,
                    ["GodClassComplianceScore"] = Math.Round(
                        (1 - Math.Min(severity, 1)) * 100,
                        2)
                };

                classMetrics.Add(metrics);

                results.Add(CreateClassResult(
                    projectPath,
                    file,
                    className,
                    language,
                    layer,
                    metrics));
            }
        }

        results.Add(CreateSummary(
            projectPath,
            files.Count,
            analyzedFiles,
            unreadableFiles,
            classMetrics));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"God Class evaluation completed: {classMetrics.Count} classes across " +
            $"{analyzedFiles}/{files.Count} readable source files; " +
            $"{unreadableFiles} files unreadable.");

        return results;
    }

    private ArchitectureEvaluatorResult CreateClassResult(
        string projectPath,
        string file,
        string className,
        string language,
        string layer,
        Dictionary<string, double> metrics)
    {
        var result = new ArchitectureEvaluatorResult(Name, className)
        {
            Target = file,
            Category = ArchitectureRuleCategory.DesignPatterns.ToString(),
            Layer = layer,
            Metrics = metrics
        };

        result.Metadata[FilePathMetadataKey] = file;
        result.Metadata[EvaluationScopeMetadataKey] = FileScope;
        result.Metadata["ClassName"] = className;
        result.Metadata["MetricUnit.GodClassSeverity"] = "MinimumOfLineAndMethodLimitRatios";
        result.Metadata["Language"] = language;
        result.Metadata["Layer"] = layer;
        result.Metadata["SemanticAnalysisPerformed"] = "false";
        result.Metadata["StructuralAnalysisHeuristic"] = "true";

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        int discoveredFiles,
        int analyzedFiles,
        int unreadableFiles,
        IReadOnlyCollection<Dictionary<string, double>> classMetrics)
    {
        var averageCompliance = classMetrics.Count == 0
            ? 0
            : classMetrics.Average(metrics =>
                metrics.GetValueOrDefault("GodClassComplianceScore"));

        var averageSeverity = classMetrics.Count == 0
            ? 0
            : classMetrics.Average(metrics =>
                metrics.GetValueOrDefault("GodClassSeverity"));

        var summary = new ArchitectureEvaluatorResult(
            Name,
            "GodClassSummary")
        {
            Target = projectPath,
            Category = ArchitectureRuleCategory.DesignPatterns.ToString(),
            Metrics =
            {
                ["DiscoveredFiles"] = discoveredFiles,
                ["AnalyzedFiles"] = analyzedFiles,
                ["UnreadableFiles"] = unreadableFiles,
                ["ClassCount"] = classMetrics.Count,
                ["AverageComplianceScore"] = averageCompliance,
                ["AverageSeverity"] = averageSeverity
            }
        };

        // No analyzed classes means density is unknown, not zero.
        if (classMetrics.Count > 0)
        {
            summary.Metrics["GodClassDensity"] =
                classMetrics.Count(metrics =>
                    metrics.GetValueOrDefault("GodClassSeverity") > 1.0d)
                / (double)classMetrics.Count;
        }

        summary.Metadata[EvaluationScopeMetadataKey] = ProjectScope;
        summary.Metadata["Evaluator"] = Name;
        summary.Metadata["AnalysisComplete"] =
            (analyzedFiles + unreadableFiles == discoveredFiles).ToString();
        summary.Metadata["AnalysisStatus"] =
            unreadableFiles == 0 ? "Completed" : "Incomplete";
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

    private double GetScalingFactor(string layer)
    {
        if (_policy.LayerScaling.TryGetValue(layer, out var scale) &&
            double.IsFinite(scale) &&
            scale > 0)
        {
            return scale;
        }

        return 1.0;
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
            // Python triple-quoted docstrings and multiline strings are not
            // fully handled by this lexical sanitizer.
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

    private static int CountLines(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return 0;

        return body.Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line));
    }

    private static int CountMethods(
        string classBody,
        string language)
    {
        return language switch
        {
            "Python" => PythonMethodRegex.Matches(classBody).Count,
            "TypeScript" => TypeScriptMethodRegex.Matches(classBody).Count,
            _ => CSharpJavaMethodRegex.Matches(classBody).Count
        };
    }

    private static int CountProperties(
        string classBody,
        string language)
    {
        return language switch
        {
            "Python" => PythonPropertyRegex.Matches(classBody).Count,
            "TypeScript" => TypeScriptPropertyRegex.Matches(classBody).Count,
            "C#" => CSharpPropertyRegex.Matches(classBody).Count,
            _ => CSharpJavaFieldRegex.Matches(classBody).Count
        };
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

            var headerIndent = CountIndentation(source, classMatch.Index);
            var bodyStart = lineEnd < source.Length ? lineEnd + 1 : lineEnd;
            var cursor = bodyStart;

            while (cursor < source.Length)
            {
                var nextLineEnd = source.IndexOf('\n', cursor);

                if (nextLineEnd < 0)
                    nextLineEnd = source.Length;

                var line = source.AsSpan(cursor, nextLineEnd - cursor);

                if (!IsBlank(line))
                {
                    var indentation = CountIndentation(source, cursor);

                    if (indentation <= headerIndent)
                        break;
                }

                cursor = nextLineEnd < source.Length
                    ? nextLineEnd + 1
                    : nextLineEnd;
            }

            classBody = source.Substring(bodyStart, cursor - bodyStart);
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

    private static bool IsBlank(ReadOnlySpan<char> line)
    {
        foreach (var character in line)
        {
            if (!char.IsWhiteSpace(character))
                return false;
        }

        return true;
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
                indentation++;
            else if (source[i] == '\t')
                indentation += 4;
            else
                break;
        }

        return indentation;
    }

    private static double ComputeGodFactor(
        double lineRatio,
        double methodRatio)
    {
        if (!double.IsFinite(lineRatio) || !double.IsFinite(methodRatio))
            return 0;

        // Ratios are measured against the configured limits. The minimum
        // prevents a single oversized dimension from being treated as a
        // God Class when the other dimension remains within its limit.
        return Math.Max(0, Math.Min(lineRatio, methodRatio));
    }
}
