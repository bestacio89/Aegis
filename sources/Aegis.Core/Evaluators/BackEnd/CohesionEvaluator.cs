
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.BackEnd;

/// <summary>
/// Estimates class-level structural indicators using source-pattern heuristics.
///
/// Member count and method-to-field ratio are not semantic cohesion metrics.
/// Results should be interpreted as structural review signals, not proof
/// that a type violates the Single Responsibility Principle.
/// </summary>
public sealed class CohesionEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly CohesionPolicy _policy;

    private static readonly Regex ClassRegex = new(
        @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CSharpJavaFieldRegex = new(
        @"^[ \t]*(?:(?:public|private|protected)\s+)?(?:(?:static|readonly|final|volatile|transient)\s+)*(?:[\w<>,?.\[\]]+\s+)+([A-Za-z_]\w*)\s*(?:=[^;]*)?;",
        RegexOptions.Compiled
        | RegexOptions.Multiline
        | RegexOptions.CultureInvariant);

    private static readonly Regex CSharpJavaMethodRegex = new(
        @"^[ \t]*(?:(?:public|private|protected)\s+)(?:(?:static|abstract|final|virtual|override|async|synchronized|native|default|strictfp)\s+)*(?:[\w<>,?.\[\]]+\s+)+([A-Za-z_]\w*)\s*\(",
        RegexOptions.Compiled
        | RegexOptions.Multiline
        | RegexOptions.CultureInvariant);

    private static readonly Regex PythonClassRegex = new(
        @"^[ \t]*class\s+([A-Za-z_]\w*)\b[^:\r\n]*:",
        RegexOptions.Compiled
        | RegexOptions.Multiline
        | RegexOptions.CultureInvariant);

    private static readonly Regex PythonMethodRegex = new(
        @"^[ \t]+(?:async\s+)?def\s+([A-Za-z_]\w*)\s*\(",
        RegexOptions.Compiled
        | RegexOptions.Multiline
        | RegexOptions.CultureInvariant);

    private static readonly Regex PythonFieldAssignmentRegex = new(
        @"\bself\.([A-Za-z_]\w*)\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override string Name => "CohesionEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python"
    ];

    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring Boot",
        "FastAPI"
    ];

    public CohesionEvaluator(
        ILogger<CohesionEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Cohesion ?? new CohesionPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (Context is null)
        {
            _logger.LogWarning(
                "{Evaluator}: no architecture context is available.",
                Name);

            return [];
        }

        var extensions = GetExtensions(Context.Language);

        if (extensions.Length == 0)
        {
            _logger.LogWarning(
                "{Evaluator}: unsupported language {Language}.",
                Name,
                Context.Language);

            return [];
        }

        var files = ResolveSourceFiles(projectPath, extensions)
            .Where(IsEligibleSourceFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "No eligible source files found for cohesion analysis.");

            return [];
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing structural cohesion indicators for {files.Count} files " +
            $"({Context.Language}/{Context.Framework}).");

        var results = new List<ArchitectureEvaluatorResult>();
        var analyzedTypes = 0;

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
            catch (IOException exception)
            {
                _logger.LogWarning(
                    exception,
                    "{Evaluator}: could not read {FilePath}.",
                    Name,
                    file);

                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogWarning(
                    exception,
                    "{Evaluator}: access denied for {FilePath}.",
                    Name,
                    file);

                continue;
            }

            token.ThrowIfCancellationRequested();

            var language = Context.Language;

            if (language.Equals("Python", StringComparison.OrdinalIgnoreCase))
            {
                AnalyzePythonFile(file, content, results, ref analyzedTypes, token);
            }
            else
            {
                AnalyzeBraceLanguageFile(
                    file,
                    content,
                    language,
                    results,
                    ref analyzedTypes,
                    token);
            }
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Cohesion evaluation completed: {analyzedTypes} type(s) analyzed, " +
            $"{results.Count} structural finding(s).");

        return results;
    }

    private void AnalyzeBraceLanguageFile(
        string file,
        string content,
        string language,
        ICollection<ArchitectureEvaluatorResult> results,
        ref int analyzedTypes,
        CancellationToken token)
    {
        foreach (Match match in ClassRegex.Matches(content))
        {
            token.ThrowIfCancellationRequested();

            var className = match.Groups[1].Value;

            var classBlock = ExtractBraceBlock(
                content,
                match.Index,
                language);

            if (string.IsNullOrWhiteSpace(classBlock))
            {
                continue;
            }

            var counts = CountBraceLanguageMembers(classBlock);
            analyzedTypes++;

            EvaluateAndAddFinding(
                file,
                className,
                counts.FieldCount,
                counts.MethodCount,
                results);
        }
    }

    private void AnalyzePythonFile(
        string file,
        string content,
        ICollection<ArchitectureEvaluatorResult> results,
        ref int analyzedTypes,
        CancellationToken token)
    {
        var matches = PythonClassRegex.Matches(content);

        foreach (Match match in matches)
        {
            token.ThrowIfCancellationRequested();

            var className = match.Groups[1].Value;
            var classBlock = ExtractPythonClassBlock(content, match.Index);

            if (string.IsNullOrWhiteSpace(classBlock))
            {
                continue;
            }

            var counts = CountPythonMembers(classBlock);
            analyzedTypes++;

            EvaluateAndAddFinding(
                file,
                className,
                counts.FieldCount,
                counts.MethodCount,
                results);
        }
    }

    private void EvaluateAndAddFinding(
        string file,
        string className,
        int fieldCount,
        int methodCount,
        ICollection<ArchitectureEvaluatorResult> results)
    {
        var memberCount = fieldCount + methodCount;

        // The ratio is undefined when no fields were detected.
        // Do not interpret the absence of fields as an automatic violation.
        var ratioDefined = fieldCount > 0;
        var ratio = ratioDefined
            ? (double)methodCount / fieldCount
            : 0.0;

        var violations = EvaluateViolations(
            memberCount,
            ratio,
            ratioDefined);

        if (violations.Count == 0)
        {
            return;
        }

        results.Add(
            CreateViolation(
                file,
                className,
                fieldCount,
                methodCount,
                memberCount,
                ratio,
                ratioDefined,
                violations));
    }

    private List<string> EvaluateViolations(
        int memberCount,
        double ratio,
        bool ratioDefined)
    {
        var violations = new List<string>();

        if (memberCount > _policy.MaxMembersPerClass)
        {
            violations.Add("COH001");
        }

        if (ratioDefined && ratio > _policy.MaxMethodFieldRatio)
        {
            violations.Add("COH002");
        }

        return violations;
    }

    private ArchitectureEvaluatorResult CreateViolation(
        string file,
        string className,
        int fieldCount,
        int methodCount,
        int memberCount,
        double ratio,
        bool ratioDefined,
        IReadOnlyCollection<string> rules)
    {
        var result = CreateResult(
            file,
            nameof(ArchitectureRuleCategory.Architecture));

        result.Metrics["FieldCount"] = fieldCount;
        result.Metrics["MethodCount"] = methodCount;
        result.Metrics["MemberCount"] = memberCount;
        result.Metrics["MethodFieldRatio"] = ratio;
        result.Metrics["MethodFieldRatioDefined"] = ratioDefined ? 1 : 0;

        // This represents a heuristic finding, not an independently measured
        // compliance percentage. RuleEngine should make the rule decision.
        result.Metrics["StructuralReviewFinding"] = 1;

        result.Metadata["ClassName"] = className;
        result.Metadata["Rules"] = string.Join(",", rules);
        result.Metadata["MaxMembersPerClass"] =
            _policy.MaxMembersPerClass.ToString(CultureInfo.InvariantCulture);
        result.Metadata["MaxMethodFieldRatio"] =
            _policy.MaxMethodFieldRatio.ToString(CultureInfo.InvariantCulture);
        result.Metadata["Language"] = Context?.Language ?? "Unknown";
        result.Metadata["Framework"] = Context?.Framework ?? "Unknown";
        result.Metadata["Layer"] = ResolveLayer(file) ?? "Unknown";
        result.Metadata["MeasurementMethod"] = "SourcePatternHeuristic";
        result.Metadata["Interpretation"] =
            "Structural indicator only; not a semantic cohesion measurement.";
        result.Metadata["RatioDefined"] = ratioDefined.ToString();

        return result;
    }

    private static (int FieldCount, int MethodCount)
        CountBraceLanguageMembers(string classBlock)
    {
        // Remove comments and literals before counting to reduce false
        // matches in examples, comments, and string contents.
        var sanitized = SanitizeBraceLanguageText(classBlock);

        var fields = CSharpJavaFieldRegex.Matches(sanitized).Count;
        var methods = CSharpJavaMethodRegex.Matches(sanitized).Count;

        return (fields, methods);
    }

    private static (int FieldCount, int MethodCount)
        CountPythonMembers(string classBlock)
    {
        var methods = PythonMethodRegex.Matches(classBlock)
            .Cast<Match>()
            .Count(match =>
            {
                var name = match.Groups[1].Value;

                // Exclude special methods from this structural count.
                return !name.StartsWith("__", StringComparison.Ordinal)
                    || !name.EndsWith("__", StringComparison.Ordinal);
            });

        var fields = PythonFieldAssignmentRegex.Matches(classBlock)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return (fields, methods);
    }

    private static string ExtractBraceBlock(
        string content,
        int declarationStart,
        string language)
    {
        var braceStart = content.IndexOf('{', declarationStart);

        if (braceStart < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        var state = LexicalState.Code;
        var sanitized = SanitizeBraceLanguageText(content);

        // Use the sanitized copy for brace matching so braces inside common
        // comments and strings do not affect the nesting depth.
        for (var i = braceStart; i < sanitized.Length; i++)
        {
            if (sanitized[i] == '{')
            {
                depth++;
            }
            else if (sanitized[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return content[declarationStart..(i + 1)];
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractPythonClassBlock(
        string content,
        int declarationStart)
    {
        var lineEnd = content.IndexOf('\n', declarationStart);

        if (lineEnd < 0)
        {
            lineEnd = content.Length;
        }

        var declarationLine = content[declarationStart..lineEnd];
        var classIndent = CountLeadingWhitespace(declarationLine);
        var blockStart = declarationStart;
        var lines = content[blockStart..].Split('\n');

        var builder = new StringBuilder();
        var consumed = 0;
        var firstLine = true;

        foreach (var lineWithPossibleCarriageReturn in lines)
        {
            var line = lineWithPossibleCarriageReturn.TrimEnd('\r');

            if (!firstLine && !string.IsNullOrWhiteSpace(line))
            {
                var indent = CountLeadingWhitespace(line);

                if (indent <= classIndent && !line.TrimStart().StartsWith('#'))
                {
                    break;
                }
            }

            builder.AppendLine(line);
            consumed += lineWithPossibleCarriageReturn.Length + 1;
            firstLine = false;
        }

        return builder.ToString();
    }

    private static int CountLeadingWhitespace(string line)
    {
        var count = 0;

        while (count < line.Length && char.IsWhiteSpace(line[count]))
        {
            count++;
        }

        return count;
    }

    private static string SanitizeBraceLanguageText(string content)
    {
        var chars = content.ToCharArray();
        var state = LexicalState.Code;

        for (var i = 0; i < chars.Length; i++)
        {
            var current = chars[i];
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';

            switch (state)
            {
                case LexicalState.Code:
                    if (current == '/' && next == '/')
                    {
                        chars[i] = chars[i + 1] = ' ';
                        i++;
                        state = LexicalState.LineComment;
                    }
                    else if (current == '/' && next == '*')
                    {
                        chars[i] = chars[i + 1] = ' ';
                        i++;
                        state = LexicalState.BlockComment;
                    }
                    else if (current == '"')
                    {
                        chars[i] = ' ';
                        state = LexicalState.String;
                    }
                    else if (current == '\'')
                    {
                        chars[i] = ' ';
                        state = LexicalState.Character;
                    }

                    break;

                case LexicalState.LineComment:
                    if (current == '\n')
                    {
                        state = LexicalState.Code;
                    }
                    else
                    {
                        chars[i] = ' ';
                    }

                    break;

                case LexicalState.BlockComment:
                    if (current == '*' && next == '/')
                    {
                        chars[i] = chars[i + 1] = ' ';
                        i++;
                        state = LexicalState.Code;
                    }
                    else if (current != '\n' && current != '\r')
                    {
                        chars[i] = ' ';
                    }

                    break;

                case LexicalState.String:
                    if (current == '\\' && i + 1 < chars.Length)
                    {
                        chars[i] = ' ';
                        if (chars[i + 1] != '\n' && chars[i + 1] != '\r')
                        {
                            chars[i + 1] = ' ';
                        }

                        i++;
                    }
                    else if (current == '"')
                    {
                        chars[i] = ' ';
                        state = LexicalState.Code;
                    }
                    else if (current != '\n' && current != '\r')
                    {
                        chars[i] = ' ';
                    }

                    break;

                case LexicalState.Character:
                    if (current == '\\' && i + 1 < chars.Length)
                    {
                        chars[i] = ' ';
                        if (chars[i + 1] != '\n' && chars[i + 1] != '\r')
                        {
                            chars[i + 1] = ' ';
                        }

                        i++;
                    }
                    else if (current == '\'')
                    {
                        chars[i] = ' ';
                        state = LexicalState.Code;
                    }
                    else if (current != '\n' && current != '\r')
                    {
                        chars[i] = ' ';
                    }

                    break;
            }
        }

        return new string(chars);
    }

    private static string[] GetExtensions(string language)
    {
        return language switch
        {
            "C#" => [".cs"],
            "Java" => [".java"],
            "Python" => [".py"],
            _ => []
        };
    }

    private static bool IsEligibleSourceFile(string file)
    {
        var segments = file.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("Generated", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("Test", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("Tests", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var fileName = Path.GetFileName(file);

        return !fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveLayer(string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(
                layer => layer.Files.Contains(
                    file,
                    StringComparer.OrdinalIgnoreCase))
            ?.Name;
    }

    private enum LexicalState
    {
        Code,
        LineComment,
        BlockComment,
        String,
        Character
    }
}
