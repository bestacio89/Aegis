
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

public sealed class MaintainabilityEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly MaintainabilityPolicy _policy;

    private static readonly Regex BraceLanguageDecisionRegex = new(
        @"\b(?:if|for|while|case|catch)\b" +
        @"|\b(?:else\s+if|elseif)\b" +
        @"|\b(?:switch)\b" +
        @"|&&|\|\||\?(?!\?)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex PythonDecisionRegex = new(
        @"\b(?:if|elif|for|while|except|and|or)\b" +
        @"|\b(?:match|case)\b" +
        @"|(?<!:)(?<!\?)\?(?!\?)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    public override string Name => "MaintainabilityEvaluator";

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

    public MaintainabilityEvaluator(
        ILogger<MaintainabilityEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Maintainability
            ?? new MaintainabilityPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var results = new List<ArchitectureEvaluatorResult>();

        if (Context is not { } context)
        {
            return results;
        }

        var extensions = GetExtensions(context.Language);

        if (extensions.Length == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                $"Unsupported language for maintainability analysis: {context.Language}.");

            return results;
        }

        // Reuse the shared source-file filtering and exclusion rules.
        var files = ResolveSourceFiles(projectPath, extensions);

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing maintainability for {files.Count} files " +
            $"({context.Language}/{context.Framework}).");

        var analyzedFiles = 0;
        var unreadableFiles = 0;

        double totalDecisionPoints = 0;
        double totalCommentDensity = 0;
        double totalNonEmptyLines = 0;
        double totalCommentLines = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
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
                    "{Evaluator}: Unable to read source file {FilePath}.",
                    Name,
                    file);

                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    exception,
                    "{Evaluator}: Access denied reading source file {FilePath}.",
                    Name,
                    file);

                continue;
            }

            token.ThrowIfCancellationRequested();

            var metrics = AnalyzeMaintainability(
                content,
                context.Language);

            analyzedFiles++;

            totalDecisionPoints += metrics.DecisionPointCount;
            totalCommentDensity += metrics.CommentDensity;
            totalNonEmptyLines += metrics.NonEmptyLineCount;
            totalCommentLines += metrics.CommentLineCount;

            results.Add(CreateFileResult(file, metrics));
        }

        token.ThrowIfCancellationRequested();

        results.Add(
            CreateSummary(
                files.Count,
                analyzedFiles,
                unreadableFiles,
                totalDecisionPoints,
                totalCommentDensity,
                totalNonEmptyLines,
                totalCommentLines));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Maintainability evaluation completed: {analyzedFiles} file(s) analyzed, " +
            $"{unreadableFiles} unreadable, {results.Count - 1} file result(s).");

        return results;
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string file,
        MaintainabilityMetrics metrics)
    {
        var result = CreateResult(file, "Maintainability");

        result.ProjectName = Context?.ProjectName;
        result.Language = Context?.Language;
        result.Framework = Context?.Framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = Context?.Confidence ?? 0;

        result.Metrics["Complexity"] = metrics.DecisionPointCount;
        result.Metrics["DecisionPointCount"] = metrics.DecisionPointCount;
        result.Metrics["LineCount"] = metrics.NonEmptyLineCount;
        result.Metrics["CommentCount"] = metrics.CommentLineCount;
        result.Metrics["CommentDensity"] = metrics.CommentDensity;

        result.Metadata["FilePath"] = file;
        result.Metadata["Language"] = Context?.Language ?? "Unknown";
        result.Metadata["Framework"] = Context?.Framework ?? "Unknown";
        result.Metadata["Layer"] = result.Layer ?? "Unknown";
        result.Metadata["EvaluationScope"] = FileScope;

        result.Metadata["ComplexityMeasurement"] =
            "Lexical decision-point count for the entire file; not method-level cyclomatic complexity.";

        result.Metadata["CommentDensityMeasurement"] =
            "Percentage of non-empty physical lines containing comment text.";

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        int filesDiscovered,
        int analyzedFiles,
        int unreadableFiles,
        double totalDecisionPoints,
        double totalCommentDensity,
        double totalNonEmptyLines,
        double totalCommentLines)
    {
        var context = Context;

        var summary = new ArchitectureEvaluatorResult(
            Name,
            "MaintainabilitySummary")
        {
            Category = "MaintainabilitySummary",
            ProjectName = context?.ProjectName,
            Language = context?.Language,
            Framework = context?.Framework,
            DetectionConfidence = context?.Confidence ?? 0
        };

        summary.Metrics["FilesDiscovered"] = filesDiscovered;
        summary.Metrics["AnalyzedFiles"] = analyzedFiles;
        summary.Metrics["UnreadableFiles"] = unreadableFiles;

        summary.Metrics["TotalDecisionPoints"] = totalDecisionPoints;
        summary.Metrics["AverageComplexity"] =
            analyzedFiles == 0
                ? 0
                : Math.Round(totalDecisionPoints / analyzedFiles, 2);

        summary.Metrics["TotalNonEmptyLines"] = totalNonEmptyLines;
        summary.Metrics["TotalCommentLines"] = totalCommentLines;

        // This is a weighted project-wide ratio, unlike the average of
        // per-file percentages, which gives tiny files equal influence.
        summary.Metrics["AverageCommentDensity"] =
            totalNonEmptyLines == 0
                ? 0
                : Math.Round(
                    totalCommentLines / totalNonEmptyLines * 100.0,
                    2);

        summary.Metadata["EvaluationScope"] = ProjectScope;
        summary.Metadata["Language"] = context?.Language ?? "Unknown";
        summary.Metadata["Framework"] = context?.Framework ?? "Unknown";

        summary.Metadata["ComplexityMeasurement"] =
            "Sum of file-level lexical decision-point counts.";

        summary.Metadata["CommentDensityMeasurement"] =
            "Project-wide comment-line count divided by non-empty physical source lines.";

        summary.Metadata["AnalysisComplete"] =
            (analyzedFiles + unreadableFiles == filesDiscovered).ToString();

        return summary;
    }

    private static MaintainabilityMetrics AnalyzeMaintainability(
        string content,
        string? language)
    {
        var sanitized = SanitizeSource(
            content,
            language,
            out var commentLineCount);

        var nonEmptyLineCount = content
            .Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line));

        var commentDensity = nonEmptyLineCount == 0
            ? 0
            : (double)commentLineCount / nonEmptyLineCount * 100.0;

        var decisionPointCount = CountDecisionPoints(
            sanitized,
            language);

        return new MaintainabilityMetrics(
            decisionPointCount,
            nonEmptyLineCount,
            commentLineCount,
            Math.Round(commentDensity, 2));
    }

    private static int CountDecisionPoints(
        string sanitized,
        string? language)
    {
        var regex = language?.Trim() switch
        {
            "Python" => PythonDecisionRegex,
            "C#" or "Java" => BraceLanguageDecisionRegex,
            _ => null
        };

        return regex?.Matches(sanitized).Count ?? 0;
    }

    private static string[] GetExtensions(string? language)
    {
        return language?.Trim() switch
        {
            "C#" => [".cs"],
            "Java" => [".java"],
            "Python" => [".py"],
            _ => []
        };
    }

    private string? ResolveLayer(string file)
    {
        return Context?.Layers
            .FirstOrDefault(layer =>
                layer.Files.Contains(
                    file,
                    StringComparer.OrdinalIgnoreCase))
            ?.Name;
    }

    private static string SanitizeSource(
        string content,
        string? language,
        out int commentLineCount)
    {
        var characters = content.ToCharArray();
        var commentLines = new HashSet<int>();

        if (language?.Trim() == "Python")
        {
            SanitizePython(content, characters, commentLines);
        }
        else
        {
            SanitizeBraceLanguage(content, characters, commentLines);
        }

        commentLineCount = commentLines.Count;

        return new string(characters);
    }

    private static void SanitizeBraceLanguage(
        string content,
        char[] characters,
        HashSet<int> commentLines)
    {
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inCharacter = false;
        var inTextBlock = false;
        var verbatimString = false;
        var line = 0;

        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];
            var next = index + 1 < content.Length
                ? content[index + 1]
                : '\0';

            if (current == '\n')
            {
                line++;
            }

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                }
                else
                {
                    Blank(characters, index);
                    commentLines.Add(line);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    Blank(characters, index);
                    Blank(characters, index + 1);
                    commentLines.Add(line);
                    index++;
                    inBlockComment = false;
                }
                else
                {
                    if (current is not '\r' and not '\n')
                    {
                        commentLines.Add(line);
                    }

                    Blank(characters, index);
                }

                continue;
            }

            if (inTextBlock)
            {
                if (current == '"'
                    && index + 2 < content.Length
                    && content[index + 1] == '"'
                    && content[index + 2] == '"')
                {
                    Blank(characters, index);
                    Blank(characters, index + 1);
                    Blank(characters, index + 2);
                    index += 2;
                    inTextBlock = false;
                }
                else
                {
                    Blank(characters, index);
                }

                continue;
            }

            if (inString || inCharacter)
            {
                Blank(characters, index);

                if (!verbatimString && current == '\\')
                {
                    if (index + 1 < content.Length)
                    {
                        Blank(characters, index + 1);

                        if (content[index + 1] == '\n')
                        {
                            line++;
                        }

                        index++;
                    }

                    continue;
                }

                if (inString && verbatimString
                    && current == '"' && next == '"')
                {
                    Blank(characters, index + 1);
                    index++;
                    continue;
                }

                if (inString && current == '"')
                {
                    inString = false;
                    verbatimString = false;
                }
                else if (inCharacter && current == '\'')
                {
                    inCharacter = false;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                Blank(characters, index);
                Blank(characters, index + 1);
                commentLines.Add(line);
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                Blank(characters, index);
                Blank(characters, index + 1);
                commentLines.Add(line);
                index++;
                inBlockComment = true;
                continue;
            }

            if (current == '"'
                && index + 2 < content.Length
                && content[index + 1] == '"'
                && content[index + 2] == '"')
            {
                Blank(characters, index);
                Blank(characters, index + 1);
                Blank(characters, index + 2);
                index += 2;
                inTextBlock = true;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                verbatimString =
                    index > 0 && content[index - 1] == '@';

                Blank(characters, index);
                continue;
            }

            if (current == '\'')
            {
                inCharacter = true;
                Blank(characters, index);
            }
        }
    }

    private static void SanitizePython(
        string content,
        char[] characters,
        HashSet<int> commentLines)
    {
        var inComment = false;
        var quoteCharacter = '\0';
        var tripleQuoted = false;
        var line = 0;

        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];

            if (current == '\n')
            {
                line++;
            }

            if (inComment)
            {
                if (current == '\n')
                {
                    inComment = false;
                }
                else
                {
                    Blank(characters, index);
                    commentLines.Add(line);
                }

                continue;
            }

            if (quoteCharacter != '\0')
            {
                if (current == '\\')
                {
                    Blank(characters, index);

                    if (index + 1 < content.Length)
                    {
                        Blank(characters, index + 1);

                        if (content[index + 1] == '\n')
                        {
                            line++;
                        }

                        index++;
                    }

                    continue;
                }

                if (tripleQuoted
                    && current == quoteCharacter
                    && index + 2 < content.Length
                    && content[index + 1] == quoteCharacter
                    && content[index + 2] == quoteCharacter)
                {
                    Blank(characters, index);
                    Blank(characters, index + 1);
                    Blank(characters, index + 2);
                    index += 2;
                    quoteCharacter = '\0';
                    tripleQuoted = false;
                    continue;
                }

                if (!tripleQuoted && current == quoteCharacter)
                {
                    Blank(characters, index);
                    quoteCharacter = '\0';
                    continue;
                }

                Blank(characters, index);
                continue;
            }

            if (current == '#')
            {
                Blank(characters, index);
                commentLines.Add(line);
                inComment = true;
                continue;
            }

            if (current is '\'' or '"')
            {
                quoteCharacter = current;

                tripleQuoted =
                    index + 2 < content.Length
                    && content[index + 1] == current
                    && content[index + 2] == current;

                Blank(characters, index);

                if (tripleQuoted)
                {
                    Blank(characters, index + 1);
                    Blank(characters, index + 2);
                    index += 2;
                }
            }
        }
    }

    private static void Blank(char[] characters, int index)
    {
        if (index >= 0
            && index < characters.Length
            && characters[index] is not '\r' and not '\n')
        {
            characters[index] = ' ';
        }
    }

    private sealed record MaintainabilityMetrics(
        int DecisionPointCount,
        int NonEmptyLineCount,
        int CommentLineCount,
        double CommentDensity);
}
