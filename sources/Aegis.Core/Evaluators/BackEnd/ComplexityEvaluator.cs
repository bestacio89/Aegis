
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.BackEnd;

/// <summary>
/// Estimates method complexity and size using source-pattern heuristics.
///
/// This evaluator is not a compiler-backed cyclomatic-complexity analyzer.
/// Rule interpretation and compliance decisions belong to the RuleEngine.
/// </summary>
public sealed class ComplexityEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly ComplexityPolicy _policy;

    private static readonly Regex CSharpJavaMethodRegex = new(
        @"^[ \t]*(?:(?:public|private|protected|internal)\s+)(?:(?:static|abstract|virtual|override|sealed|async|final|synchronized|native|default|strictfp|extern|new)\s+)*(?:[\w<>,?.\[\]]+\s+)+(?<name>[A-Za-z_]\w*)\s*\([^;{}]*\)\s*(?:where\s+[^{;]+?\s*)?(?<terminator>\{|=>)",
        RegexOptions.Compiled
        | RegexOptions.Multiline
        | RegexOptions.CultureInvariant);

    private static readonly Regex PythonMethodRegex = new(
        @"^[ \t]*(?:async\s+)?def\s+(?<name>[A-Za-z_]\w*)\s*\([^:\r\n]*\)\s*(?:->\s*[^:\r\n]+)?\s*:",
        RegexOptions.Compiled
        | RegexOptions.Multiline
        | RegexOptions.CultureInvariant);

    private static readonly Regex CSharpDecisionRegex = new(
        @"\b(?:if|for|foreach|while|catch|case)\b|&&|\|\||\?(?![?.])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JavaDecisionRegex = new(
        @"\b(?:if|for|while|catch|case)\b|&&|\|\||\?(?![?.])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonDecisionRegex = new(
        @"\b(?:if|elif|for|while|except)\b|\band\b|\bor\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override string Name => "ComplexityEvaluator";

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

    public ComplexityEvaluator(
        ILogger<ComplexityEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Complexity ?? new ComplexityPolicy();
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

        var language = Context.Language;
        var extensions = GetExtensions(language);

        if (extensions.Length == 0)
        {
            _logger.LogWarning(
                "{Evaluator}: unsupported language {Language}.",
                Name,
                language);

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
                "No eligible source files found for complexity analysis.");

            return [];
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing complexity for {files.Count} files " +
            $"({language}/{Context.Framework}).");

        var results = new List<ArchitectureEvaluatorResult>();
        var analyzedMethods = 0;

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

            var methods = language.Equals(
                "Python",
                StringComparison.OrdinalIgnoreCase)
                ? ExtractPythonMethods(content)
                : ExtractBraceLanguageMethods(content, language);

            foreach (var method in methods)
            {
                token.ThrowIfCancellationRequested();

                analyzedMethods++;

                var metrics = MeasureMethod(
                    method.Content,
                    language);

                var violations = EvaluateViolations(
                    metrics.Complexity,
                    metrics.LineCount);

                if (violations.Count == 0)
                {
                    continue;
                }

                results.Add(
                    CreateViolation(
                        file,
                        method.Name,
                        method.StartLine,
                        metrics.Complexity,
                        metrics.LineCount,
                        violations));
            }
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Complexity evaluation completed: {analyzedMethods} method(s) " +
            $"analyzed, {results.Count} finding(s).");

        return results;
    }

    private List<string> EvaluateViolations(
        int complexity,
        int lines)
    {
        var violations = new List<string>();

        if (complexity > _policy.MaxCyclomaticComplexity)
        {
            violations.Add("CMP001");
        }

        if (lines > _policy.MaxLinesPerMethod)
        {
            violations.Add("CMP002");
        }

        return violations;
    }

    private ArchitectureEvaluatorResult CreateViolation(
        string file,
        string methodName,
        int startLine,
        int complexity,
        int lines,
        IReadOnlyCollection<string> violations)
    {
        var result = CreateResult(
            file,
            "ComplexityViolation");

        result.ProjectName = Context?.ProjectName;
        result.Language = Context?.Language;
        result.Framework = Context?.Framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = Context?.Confidence ?? 0;

        result.Metrics["CyclomaticComplexity"] = complexity;
        result.Metrics["MethodLineCount"] = lines;
        result.Metrics["ComplexityFinding"] = 1;

        result.Metadata["MethodName"] = methodName;
        result.Metadata["StartLine"] = startLine.ToString(CultureInfo.InvariantCulture);
        result.Metadata["Rules"] = string.Join(",", violations);
        result.Metadata["MaxCyclomaticThreshold"] =
            _policy.MaxCyclomaticComplexity.ToString(
                CultureInfo.InvariantCulture);
        result.Metadata["MaxLinesThreshold"] =
            _policy.MaxLinesPerMethod.ToString(
                CultureInfo.InvariantCulture);
        result.Metadata["Language"] = Context?.Language ?? "Unknown";
        result.Metadata["Framework"] = Context?.Framework ?? "Unknown";
        result.Metadata["Layer"] = ResolveLayer(file) ?? "Unknown";
        result.Metadata["MeasurementMethod"] = "SourcePatternHeuristic";
        result.Metadata["Interpretation"] =
            "Estimated complexity and physical method size; not compiler-backed analysis.";

        return result;
    }

    private static (int Complexity, int LineCount) MeasureMethod(
        string methodContent,
        string language)
    {
        var sanitized = language.Equals(
            "Python",
            StringComparison.OrdinalIgnoreCase)
            ? SanitizePythonText(methodContent)
            : SanitizeBraceLanguageText(methodContent);

        var decisionRegex = language switch
        {
            "C#" => CSharpDecisionRegex,
            "Java" => JavaDecisionRegex,
            "Python" => PythonDecisionRegex,
            _ => throw new NotSupportedException(
                $"Unsupported language: {language}")
        };

        // Cyclomatic complexity starts at one and increases for detected
        // decision points. This is an estimate, not an AST calculation.
        var complexity = 1 + decisionRegex.Matches(sanitized).Count;

        var lineCount = sanitized
            .Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line));

        return (complexity, lineCount);
    }

    private static List<MethodRegion> ExtractBraceLanguageMethods(
        string content,
        string language)
    {
        var results = new List<MethodRegion>();
        var sanitized = SanitizeBraceLanguageText(content);

        foreach (Match match in CSharpJavaMethodRegex.Matches(sanitized))
        {
            var name = match.Groups["name"].Value;
            var terminator = match.Groups["terminator"];

            var start = match.Index;
            var bodyStart = terminator.Index;

            string methodContent;

            if (terminator.Value == "=>")
            {
                var end = FindExpressionBodyEnd(sanitized, bodyStart + 2);

                if (end < 0)
                {
                    continue;
                }

                methodContent = content[start..(end + 1)];
            }
            else
            {
                var end = FindMatchingBrace(sanitized, bodyStart);

                if (end < 0)
                {
                    continue;
                }

                methodContent = content[start..(end + 1)];
            }

            results.Add(
                new MethodRegion(
                    name,
                    CountLineNumber(content, start),
                    methodContent));
        }

        return results;
    }

    private static List<MethodRegion> ExtractPythonMethods(string content)
    {
        var results = new List<MethodRegion>();
        var sanitized = SanitizePythonText(content);
        var matches = PythonMethodRegex.Matches(sanitized);

        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var start = match.Index;
            var lineStart = content.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
            var declarationLine = content[lineStart..FindLineEnd(content, start)];
            var baseIndent = CountIndent(declarationLine);
            var nextMethodStart = index + 1 < matches.Count
                ? matches[index + 1].Index
                : content.Length;

            var blockEnd = FindPythonBlockEnd(
                content,
                sanitized,
                start,
                nextMethodStart,
                baseIndent);

            if (blockEnd <= start)
            {
                continue;
            }

            results.Add(
                new MethodRegion(
                    match.Groups["name"].Value,
                    CountLineNumber(content, start),
                    content[start..blockEnd]));
        }

        return results;
    }

    private static int FindPythonBlockEnd(
        string content,
        string sanitized,
        int start,
        int nextMethodStart,
        int baseIndent)
    {
        var position = start;

        while (position < nextMethodStart)
        {
            var lineEnd = FindLineEnd(sanitized, position);
            var line = sanitized[position..lineEnd];

            if (!string.IsNullOrWhiteSpace(line)
                && CountIndent(line) <= baseIndent
                && position > start)
            {
                return position;
            }

            if (lineEnd >= sanitized.Length)
            {
                return content.Length;
            }

            position = lineEnd + 1;
        }

        return nextMethodStart;
    }

    private static int FindMatchingBrace(
        string sanitized,
        int openingBrace)
    {
        var depth = 0;

        for (var index = openingBrace; index < sanitized.Length; index++)
        {
            if (sanitized[index] == '{')
            {
                depth++;
            }
            else if (sanitized[index] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static int FindExpressionBodyEnd(
        string sanitized,
        int expressionStart)
    {
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;

        for (var index = expressionStart; index < sanitized.Length; index++)
        {
            var character = sanitized[index];

            switch (character)
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
                case ';' when parentheses == 0 && brackets == 0 && braces == 0:
                    return index;
            }
        }

        return -1;
    }

    private static int CountLineNumber(string content, int index)
    {
        var line = 1;

        for (var i = 0; i < index && i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static int FindLineEnd(string content, int start)
    {
        var end = content.IndexOf('\n', start);
        return end < 0 ? content.Length : end;
    }

    private static int CountIndent(string line)
    {
        var count = 0;

        while (count < line.Length
               && (line[count] == ' ' || line[count] == '\t'))
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
                case LexicalState.Character:
                    var terminator = state == LexicalState.String ? '"' : '\'';

                    if (current == '\\' && i + 1 < chars.Length)
                    {
                        chars[i] = ' ';

                        if (chars[i + 1] != '\n' && chars[i + 1] != '\r')
                        {
                            chars[i + 1] = ' ';
                        }

                        i++;
                    }
                    else if (current == terminator)
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

    private static string SanitizePythonText(string content)
    {
        var chars = content.ToCharArray();
        var state = LexicalState.Code;
        char quote = '\0';
        var tripleQuoted = false;

        for (var i = 0; i < chars.Length; i++)
        {
            var current = chars[i];

            if (state == LexicalState.Code)
            {
                if (current == '#')
                {
                    state = LexicalState.LineComment;
                    chars[i] = ' ';
                }
                else if (current is '\'' or '"')
                {
                    quote = current;
                    tripleQuoted = i + 2 < chars.Length
                        && chars[i + 1] == current
                        && chars[i + 2] == current;

                    chars[i] = ' ';

                    if (tripleQuoted)
                    {
                        chars[i + 1] = ' ';
                        chars[i + 2] = ' ';
                        i += 2;
                    }

                    state = LexicalState.String;
                }
            }
            else if (state == LexicalState.LineComment)
            {
                if (current == '\n')
                {
                    state = LexicalState.Code;
                }
                else
                {
                    chars[i] = ' ';
                }
            }
            else if (state == LexicalState.String)
            {
                if (current == '\\' && i + 1 < chars.Length)
                {
                    chars[i] = ' ';

                    if (chars[i + 1] != '\n' && chars[i + 1] != '\r')
                    {
                        chars[i + 1] = ' ';
                    }

                    i++;
                    continue;
                }

                if (tripleQuoted
                    && current == quote
                    && i + 2 < chars.Length
                    && chars[i + 1] == quote
                    && chars[i + 2] == quote)
                {
                    chars[i] = chars[i + 1] = chars[i + 2] = ' ';
                    i += 2;
                    state = LexicalState.Code;
                    continue;
                }

                if (!tripleQuoted && current == quote)
                {
                    chars[i] = ' ';
                    state = LexicalState.Code;
                    continue;
                }

                if (current != '\n' && current != '\r')
                {
                    chars[i] = ' ';
                }
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

        var name = Path.GetFileName(file);

        return !name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
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

    private sealed record MethodRegion(
        string Name,
        int StartLine,
        string Content);

    private enum LexicalState
    {
        Code,
        LineComment,
        BlockComment,
        String,
        Character
    }
}
