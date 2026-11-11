
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
        "LanguageAgnostic"
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

        // Maintainability is language-specific, not framework-specific.
        // Resolve every supported source file independently in mixed repositories.
        var files = ResolveSourceFiles(
            projectPath,
            ".cs",
            ".java",
            ".py");

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
        double totalMaintainabilityScore = 0;

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

            var language = DetectLanguage(file);
            if (!SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
                continue;

            var framework = ResolveFrameworkForFile(file, projectPath, context);
            var metrics = AnalyzeMaintainability(content, language);

            analyzedFiles++;

            totalDecisionPoints += metrics.DecisionPointCount;
            totalCommentDensity += metrics.CommentDensity;
            totalNonEmptyLines += metrics.NonEmptyLineCount;
            totalCommentLines += metrics.CommentLineCount;
            totalMaintainabilityScore += metrics.MaintainabilityScore;

            results.Add(CreateFileResult(file, metrics, language, framework));
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
                totalCommentLines,
                totalMaintainabilityScore));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Maintainability evaluation completed: {analyzedFiles} file(s) analyzed, " +
            $"{unreadableFiles} unreadable, {results.Count - 1} file result(s).");

        return results;
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string file,
        MaintainabilityMetrics metrics,
        string language,
        string? framework)
    {
        var result = CreateResult(file, "Maintainability");

        result.Language = language;
        result.Framework = framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = Context?.Confidence ?? 0;

        result.Metrics["Complexity"] = metrics.DecisionPointCount;
        result.Metrics["DecisionPointCount"] = metrics.DecisionPointCount;
        result.Metrics["LineCount"] = metrics.NonEmptyLineCount;
        result.Metrics["CommentCount"] = metrics.CommentLineCount;
        result.Metrics["CommentDensity"] = metrics.CommentDensity;
        result.Metrics["MaintainabilityScore"] = metrics.MaintainabilityScore;

        result.Metadata["FilePath"] = file;
        result.Metadata["Language"] = language;
        result.Metadata["Framework"] = framework ?? "Unknown";
        result.Metadata["Layer"] = result.Layer ?? "Unknown";
        result.Metadata["EvaluationScope"] = FileScope;

        result.Metadata["ComplexityMeasurement"] =
            "Lexical decision-point count for the entire file; not method-level cyclomatic complexity.";

        result.Metadata["CommentDensityMeasurement"] =
            "Percentage of non-empty physical lines containing comment text.";
        result.Metadata["MaintainabilityMeasurement"] =
            "Aegis heuristic (0-100): 100 minus weighted lexical decision points, excess lines beyond policy limit, and a bounded penalty for comment density below policy minimum. This is not a compiler-backed or industry-standard maintainability index.";
        result.Metadata["AnalysisComplete"] = "true";
        result.Metadata["AnalysisStatus"] = "Completed";
        result.Evidence = string.Join(Environment.NewLine, metrics.Evidence);
        result.Metadata["Evidence.MaintainabilityScore"] = result.Evidence;
        result.Metadata["EvidenceSummary.MaintainabilityScore"] = metrics.EvidenceSummary;
        result.Metadata["EvidenceScope.MaintainabilityScore"] = "FileMetrics";
        result.Metadata["EvidenceTruncated.MaintainabilityScore"] =
            metrics.EvidenceTruncated.ToString();

        if (metrics.FirstEvidenceLine is > 0)
        {
            result.LineNumber = metrics.FirstEvidenceLine;
            result.Metadata["EvidenceLine.MaintainabilityScore"] =
                metrics.FirstEvidenceLine.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            result.Metadata["CodeSnippet.MaintainabilityScore"] =
                GetSourceLine(metrics.Source, metrics.FirstEvidenceLine.Value);
        }

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        int filesDiscovered,
        int analyzedFiles,
        int unreadableFiles,
        double totalDecisionPoints,
        double totalCommentDensity,
        double totalNonEmptyLines,
        double totalCommentLines,
        double totalMaintainabilityScore)
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
        summary.Metrics["AverageMaintainabilityScore"] =
            analyzedFiles > 0 && unreadableFiles == 0
                ? Math.Round(totalMaintainabilityScore / analyzedFiles, 2)
                : -1;

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

        var analysisComplete = filesDiscovered > 0
            && analyzedFiles == filesDiscovered
            && unreadableFiles == 0;
        summary.Metadata["AnalysisComplete"] =
            analysisComplete.ToString().ToLowerInvariant();
        summary.Metadata["AnalysisStatus"] =
            analysisComplete ? "Completed" : "Incomplete";
        summary.Metadata["MaintainabilityMeasurement"] =
            "Aegis heuristic; average is unavailable (-1) when any discovered file was unreadable.";

        return summary;
    }

    private MaintainabilityMetrics AnalyzeMaintainability(
        string content,
        string? language)
    {
        var sanitized = SanitizeSource(
            content,
            language,
            out var commentLines);
        var commentLineCount = commentLines.Count;

        var nonEmptyLineCount = content
            .Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line));

        var commentDensity = nonEmptyLineCount == 0
            ? 0
            : (double)commentLineCount / nonEmptyLineCount * 100.0;

        var decisionRegex = GetDecisionRegex(language);
        var decisionMatches = decisionRegex?.Matches(sanitized)
            .Cast<Match>()
            .ToArray() ?? [];
        var decisionPointCount = decisionMatches.Length;

        var roundedCommentDensity = Math.Round(commentDensity, 2);
        var complexityPenalty = decisionPointCount * Math.Max(0, _policy.ComplexityWeight);
        var excessLines = Math.Max(0, nonEmptyLineCount - _policy.MaxLinesPerFile);
        var linePenalty = excessLines * Math.Max(0, _policy.LineWeight);
        var commentPenalty = _policy.RequireCommentDensityCheck
            && roundedCommentDensity < _policy.MinCommentDensity
                ? Math.Min(
                    20.0,
                    (_policy.MinCommentDensity - roundedCommentDensity) * 2.0)
                : 0.0;
        var score = Math.Clamp(
            100.0 - complexityPenalty - linePenalty - commentPenalty,
            0.0,
            100.0);

        var roundedScore = Math.Round(score, 2);
        var evidenceSummary =
            $"Maintainability score {roundedScore:0.##}/100; " +
            $"decision points {decisionPointCount} (penalty {complexityPenalty:0.##}); " +
            $"non-empty lines {nonEmptyLineCount}, maximum {_policy.MaxLinesPerFile} " +
            $"(penalty {linePenalty:0.##}); comment density {roundedCommentDensity:0.##}% " +
            $"against minimum {_policy.MinCommentDensity:0.##}% (penalty {commentPenalty:0.##}).";

        var evidence = new List<string> { evidenceSummary };
        foreach (var match in decisionMatches.Take(50))
        {
            var lineNumber = GetLineNumber(sanitized, match.Index);
            evidence.Add(
                $"Line {lineNumber}: decision point '{match.Value}' — {GetSourceLine(content, lineNumber)}");
        }

        if (commentPenalty > 0)
        {
            evidence.AddRange(commentLines
                .Order()
                .Take(10)
                .Select(lineNumber =>
                    $"Line {lineNumber}: comment — {GetSourceLine(content, lineNumber)}"));
        }

        return new MaintainabilityMetrics(
            decisionPointCount,
            nonEmptyLineCount,
            commentLineCount,
            roundedCommentDensity,
            roundedScore,
            evidence,
            evidenceSummary,
            decisionMatches.Length > 0
                ? GetLineNumber(sanitized, decisionMatches[0].Index)
                : null,
            content,
            decisionMatches.Length > 50);
    }

    private static Regex? GetDecisionRegex(string? language) =>
        language?.Trim() switch
        {
            "Python" => PythonDecisionRegex,
            "C#" or "Java" => BraceLanguageDecisionRegex,
            _ => null
        };

    private static string DetectLanguage(string file) =>
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            _ => "Unknown"
        };

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
        out IReadOnlyCollection<int> commentLines)
    {
        var characters = content.ToCharArray();
        var discoveredCommentLines = new HashSet<int>();

        if (language?.Trim() == "Python")
        {
            SanitizePython(content, characters, discoveredCommentLines);
        }
        else
        {
            SanitizeBraceLanguage(content, characters, discoveredCommentLines);
        }

        commentLines = discoveredCommentLines;

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
                    commentLines.Add(line + 1);
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
        double CommentDensity,
        double MaintainabilityScore,
        IReadOnlyCollection<string> Evidence,
        string EvidenceSummary,
        int? FirstEvidenceLine,
        string Source,
        bool EvidenceTruncated);
}
