
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

public sealed class ErrorHandlingEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly ErrorHandlingPolicy _policy;

    private static readonly Regex BraceCatchRegex = new(
        @"\bcatch\b\s*(?:\(\s*(?<type>[^)]*?)\s*\))?\s*(?:when\s*\([^)]*\)\s*)?\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonExceptRegex = new(
        @"^[ \t]*except(?:\*)?(?:[ \t]+(?<type>[^:\r\n]+?))?[ \t]*:",
        RegexOptions.Compiled |
        RegexOptions.Multiline |
        RegexOptions.CultureInvariant);

    private static readonly Regex HandlingRegex = new(
        @"\b(?:throw|raise)\b" +
        @"|\b(?:logger|log|logging|console)\s*\." +
        @"|\b(?:print|trace|debug|info|warn|warning|error|critical)\s*\(",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex ExceptionTypeRegex = new(
        @"[A-Za-z_][A-Za-z0-9_.]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override string Name => "ErrorHandlingEvaluator";

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

    public ErrorHandlingEvaluator(
        ILogger<ErrorHandlingEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.ErrorHandling
            ?? new ErrorHandlingPolicy();
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

        var files = ResolveSourceFiles(
            projectPath,
            ".cs",
            ".java",
            ".py");
        var filesAnalyzed = 0;
        var unreadableFiles = 0;

        var totalCatchBlocks = 0;
        var totalEmptyCatchBlocks = 0;
        var totalGenericCatchBlocks = 0;
        var totalSwallowedExceptions = 0;

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing error handling in {files.Count} source file(s).");

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
                    "{Evaluator}: Could not read source file {FilePath}.",
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

            filesAnalyzed++;

            var language = DetectLanguage(file);
            if (!SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
                continue;

            var framework = ResolveFrameworkForFile(file, projectPath, context);
            var metrics = AnalyzeCatchBlocks(content, language);

            totalCatchBlocks += metrics.TotalCatchBlocks;
            totalEmptyCatchBlocks += metrics.EmptyCatchBlocks;
            totalGenericCatchBlocks += metrics.GenericCatchBlocks;
            totalSwallowedExceptions += metrics.SwallowedExceptions;

            // Preserve file-level evidence only when a catch/except clause
            // exists. The project summary below records the full analysis scope.
            if (metrics.TotalCatchBlocks > 0)
            {
                results.Add(CreateFileResult(file, metrics, language, framework));
            }
        }

        token.ThrowIfCancellationRequested();

        AddSummary(
            results,
            files.Count,
            filesAnalyzed,
            unreadableFiles,
            totalCatchBlocks,
            totalEmptyCatchBlocks,
            totalGenericCatchBlocks,
            totalSwallowedExceptions);

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Error-handling analysis completed: {filesAnalyzed} file(s) analyzed, " +
            $"{totalCatchBlocks} catch/except clause(s), " +
            $"{totalSwallowedExceptions} potential swallowed exception(s).");

        return results;
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string file,
        CatchAnalysis metrics,
        string language,
        string? framework)
    {
        var result = CreateResult(file, "ErrorHandling");

        result.Language = language;
        result.Framework = framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = Context?.Confidence ?? 0;

        result.Metrics["TotalCatchBlocks"] = metrics.TotalCatchBlocks;
        result.Metrics["EmptyCatchBlocks"] = metrics.EmptyCatchBlocks;
        result.Metrics["GenericCatchBlocks"] = metrics.GenericCatchBlocks;
        result.Metrics["SwallowedExceptions"] = metrics.SwallowedExceptions;

        result.Metadata["FilePath"] = file;
        result.Metadata["Language"] = language;
        result.Metadata["Framework"] = framework ?? "Unknown";
        result.Metadata["Layer"] = result.Layer ?? "Unknown";
        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["MeasurementMethod"] =
            "Lexical analysis of catch/except clauses; handling indicators are heuristic.";
        result.Metadata["SwallowedExceptionDefinition"] =
            "Catch/except body contains no recognized rethrow, raise, or logging indicator.";

        var allEvidence = metrics.Evidence;
        result.Evidence = string.Join(
            Environment.NewLine,
            allEvidence.Select(item =>
                $"Line {item.LineNumber}: {item.SourceLine}"));

        result.LineNumber = allEvidence.FirstOrDefault()?.LineNumber;

        AddMetricEvidence(
            result,
            "TotalCatchBlocks",
            allEvidence,
            "No catch/except clauses were detected in this file.");
        AddMetricEvidence(
            result,
            "EmptyCatchBlocks",
            allEvidence.Where(item => item.IsEmpty).ToArray(),
            $"No empty catch/except clauses were detected among {metrics.TotalCatchBlocks} clause(s).");
        AddMetricEvidence(
            result,
            "GenericCatchBlocks",
            allEvidence.Where(item => item.IsGeneric).ToArray(),
            $"No generic catch/except types were detected among {metrics.TotalCatchBlocks} clause(s).");
        AddMetricEvidence(
            result,
            "SwallowedExceptions",
            allEvidence.Where(item => item.IsSwallowed).ToArray(),
            $"No swallowed exception indicators were detected among {metrics.TotalCatchBlocks} clause(s).");

        return result;
    }

    private static void AddMetricEvidence(
        ArchitectureEvaluatorResult result,
        string metricKey,
        IReadOnlyCollection<CatchEvidence> evidence,
        string emptyEvidence)
    {
        var evidenceText = evidence.Count == 0
            ? emptyEvidence
            : string.Join(
                Environment.NewLine,
                evidence.Select(item =>
                    $"Line {item.LineNumber}: {item.SourceLine}"));

        result.Metadata[$"Evidence.{metricKey}"] = evidenceText;
        result.Metadata[$"EvidenceScope.{metricKey}"] = "FileMetrics";

        var first = evidence.FirstOrDefault();
        if (first is null)
            return;

        result.Metadata[$"EvidenceLine.{metricKey}"] =
            first.LineNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata[$"CodeSnippet.{metricKey}"] = first.SourceLine;
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

    private static CatchAnalysis AnalyzeCatchBlocks(
        string content,
        string? language)
    {
        var normalizedLanguage = language?.Trim();

        return normalizedLanguage switch
        {
            "Python" => AnalyzePythonExceptBlocks(content),
            "C#" or "Java" => AnalyzeBraceCatchBlocks(content),
            _ => CatchAnalysis.Empty
        };
    }

    private static CatchAnalysis AnalyzeBraceCatchBlocks(
        string content)
    {
        var sanitized = SanitizeBraceLanguage(content);

        var total = 0;
        var empty = 0;
        var generic = 0;
        var swallowed = 0;
        var evidence = new List<CatchEvidence>();

        foreach (Match match in BraceCatchRegex.Matches(sanitized))
        {
            var openingBrace = match.Index + match.Length - 1;

            var closingBrace = FindMatchingBrace(
                sanitized,
                openingBrace);

            // An unmatched block is not measured as a complete catch body.
            if (closingBrace < 0)
            {
                continue;
            }

            total++;

            var body = sanitized[
                (openingBrace + 1)..closingBrace];

            var exceptionType = match.Groups["type"].Value;

            if (string.IsNullOrWhiteSpace(body))
            {
                empty++;
            }

            if (IsGenericExceptionType(exceptionType))
            {
                generic++;
            }

            var isEmpty = string.IsNullOrWhiteSpace(body);
            var isGeneric = IsGenericExceptionType(exceptionType);
            var isSwallowed = !ContainsHandlingIndicator(body);

            if (isEmpty)
                empty++;
            if (isGeneric)
                generic++;
            if (isSwallowed)
                swallowed++;

            var lineNumber = GetLineNumber(sanitized, match.Index);
            evidence.Add(new CatchEvidence(
                lineNumber,
                GetSourceLine(content, lineNumber),
                isEmpty,
                isGeneric,
                isSwallowed));
        }

        return new CatchAnalysis(
            total,
            empty,
            generic,
            swallowed,
            evidence);
    }

    private static CatchAnalysis AnalyzePythonExceptBlocks(
        string content)
    {
        var sanitized = SanitizePython(content);

        var total = 0;
        var empty = 0;
        var generic = 0;
        var swallowed = 0;
        var evidence = new List<CatchEvidence>();

        foreach (Match match in PythonExceptRegex.Matches(sanitized))
        {
            var bodyStart = match.Index + match.Length;
            var headerIndent = GetIndentationAt(
                sanitized,
                match.Index);

            var bodyEnd = FindPythonBlockEnd(
                sanitized,
                bodyStart,
                headerIndent);

            total++;

            var body = sanitized[bodyStart..bodyEnd];

            if (string.IsNullOrWhiteSpace(body))
            {
                empty++;
            }

            var exceptionType = match.Groups["type"].Value.Trim();

            if (IsGenericExceptionType(exceptionType))
            {
                generic++;
            }

            var isEmpty = string.IsNullOrWhiteSpace(body);
            var isGeneric = IsGenericExceptionType(exceptionType);
            var isSwallowed = !ContainsHandlingIndicator(body);

            if (isEmpty)
                empty++;
            if (isGeneric)
                generic++;
            if (isSwallowed)
                swallowed++;

            var lineNumber = GetLineNumber(sanitized, match.Index);
            evidence.Add(new CatchEvidence(
                lineNumber,
                GetSourceLine(content, lineNumber),
                isEmpty,
                isGeneric,
                isSwallowed));
        }

        return new CatchAnalysis(
            total,
            empty,
            generic,
            swallowed,
            evidence);
    }

    private static bool IsGenericExceptionType(string exceptionType)
    {
        if (string.IsNullOrWhiteSpace(exceptionType))
        {
            // Bare C# catch and Python except clauses catch broadly.
            // Java requires a typed catch parameter.
            return true;
        }

        var identifiers = ExceptionTypeRegex
            .Matches(exceptionType)
            .Select(match => match.Value)
            .ToArray();

        if (identifiers.Length == 0)
        {
            return false;
        }

        // For Python "except Exception as ex", the exception class is
        // the first identifier. For Java multi-catch, inspect all types.
        var candidates = identifiers
            .Where(identifier =>
                !identifier.Equals("as", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return candidates.Any(identifier =>
            identifier.Equals("Exception", StringComparison.OrdinalIgnoreCase)
            || identifier.Equals("Throwable", StringComparison.OrdinalIgnoreCase)
            || identifier.Equals("BaseException", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsHandlingIndicator(string body)
    {
        return HandlingRegex.IsMatch(body);
    }

    private static int FindMatchingBrace(
        string content,
        int openingBrace)
    {
        if (openingBrace < 0
            || openingBrace >= content.Length
            || content[openingBrace] != '{')
        {
            return -1;
        }

        var depth = 0;

        for (var index = openingBrace; index < content.Length; index++)
        {
            if (content[index] == '{')
            {
                depth++;
            }
            else if (content[index] == '}')
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

    private static int FindPythonBlockEnd(
        string content,
        int bodyStart,
        int headerIndent)
    {
        var position = bodyStart;

        while (position < content.Length)
        {
            var lineStart = position;
            var newlineIndex = content.IndexOf('\n', position);

            var lineEnd = newlineIndex >= 0
                ? newlineIndex
                : content.Length;

            var line = content[lineStart..lineEnd];

            if (!string.IsNullOrWhiteSpace(line))
            {
                var indentation = CountIndentation(line);

                if (indentation <= headerIndent)
                {
                    return lineStart;
                }
            }

            position = newlineIndex >= 0
                ? newlineIndex + 1
                : content.Length;
        }

        return content.Length;
    }

    private static int GetIndentationAt(
        string content,
        int position)
    {
        var lineStart = content.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;

        return CountIndentation(
            content[lineStart..position]);
    }

    private static int CountIndentation(string line)
    {
        var count = 0;

        foreach (var character in line)
        {
            if (character == ' ')
            {
                count++;
            }
            else if (character == '\t')
            {
                count += 4;
            }
            else
            {
                break;
            }
        }

        return count;
    }

    private static string SanitizeBraceLanguage(string content)
    {
        var characters = content.ToCharArray();

        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inCharacter = false;
        var inJavaTextBlock = false;
        var verbatimString = false;

        for (var index = 0; index < characters.Length; index++)
        {
            var current = content[index];
            var next = index + 1 < content.Length
                ? content[index + 1]
                : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                }
                else
                {
                    Blank(characters, index);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    Blank(characters, index);
                    Blank(characters, index + 1);
                    index++;
                    inBlockComment = false;
                }
                else
                {
                    Blank(characters, index);
                }

                continue;
            }

            if (inJavaTextBlock)
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
                    inJavaTextBlock = false;
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
                        if (content[index + 1] != '\n')
                        {
                            Blank(characters, index + 1);
                        }

                        index++;
                    }

                    continue;
                }

                if (inString && current == '"' &&
                    (verbatimString
                        ? next != '"'
                        : true))
                {
                    inString = false;
                    verbatimString = false;
                }
                else if (inString && verbatimString
                         && current == '"' && next == '"')
                {
                    Blank(characters, index + 1);
                    index++;
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
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                Blank(characters, index);
                Blank(characters, index + 1);
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
                inJavaTextBlock = true;
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

        return new string(characters);
    }

    private static string SanitizePython(string content)
    {
        var characters = content.ToCharArray();

        var inComment = false;
        var quoteCharacter = '\0';
        var tripleQuoted = false;

        for (var index = 0; index < characters.Length; index++)
        {
            var current = content[index];

            if (inComment)
            {
                if (current == '\n')
                {
                    inComment = false;
                }
                else
                {
                    Blank(characters, index);
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
                        if (content[index + 1] != '\n')
                        {
                            Blank(characters, index + 1);
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

        return new string(characters);
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

    private static string DetectLanguage(string file) =>
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            _ => "Unknown"
        };

    private static void AddMetricEvidence(
        ArchitectureEvaluatorResult result,
        string metricKey,
        IReadOnlyCollection<CatchEvidence> evidence,
        string emptyEvidence)
    {
        var evidenceText = evidence.Count == 0
            ? emptyEvidence
            : string.Join(
                Environment.NewLine,
                evidence.Select(item =>
                    $"Line {item.LineNumber}: {item.SourceLine}"));

        result.Metadata[$"Evidence.{metricKey}"] = evidenceText;
        result.Metadata[$"EvidenceScope.{metricKey}"] = "FileMetrics";

        var first = evidence.FirstOrDefault();
        if (first is null)
            return;

        result.Metadata[$"EvidenceLine.{metricKey}"] =
            first.LineNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata[$"CodeSnippet.{metricKey}"] = first.SourceLine;
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

    private void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        int filesDiscovered,
        int filesAnalyzed,
        int unreadableFiles,
        int totalCatchBlocks,
        int totalEmptyCatchBlocks,
        int totalGenericCatchBlocks,
        int totalSwallowedExceptions)
    {
        var context = Context;

        var summary = new ArchitectureEvaluatorResult(
            Name,
            "ErrorHandlingSummary")
        {
            Category = "ErrorHandlingSummary",
            ProjectName = context?.ProjectName,
            Language = context?.Language,
            Framework = context?.Framework,
            DetectionConfidence = context?.Confidence ?? 0
        };

        summary.Metrics["FilesDiscovered"] = filesDiscovered;
        summary.Metrics["FilesAnalyzed"] = filesAnalyzed;
        summary.Metrics["UnreadableFiles"] = unreadableFiles;

        summary.Metrics["FilesWithCatchBlocks"] = results.Count(
            result => result.Category == "ErrorHandling");

        summary.Metrics["TotalCatchBlocks"] = totalCatchBlocks;
        summary.Metrics["EmptyCatchBlocks"] = totalEmptyCatchBlocks;
        summary.Metrics["GenericCatchBlocks"] = totalGenericCatchBlocks;
        summary.Metrics["SwallowedExceptions"] = totalSwallowedExceptions;

        // Coverage is measured only when at least one catch/except clause was
        // observed. An empty catch population is unknown, not 100% compliant.
        if (totalCatchBlocks > 0)
        {
            summary.Metrics["ErrorHandlingCoverage"] = Math.Round(
                Math.Clamp(
                    1d - totalSwallowedExceptions / (double)totalCatchBlocks,
                    0d,
                    1d) * 100d,
                2);
        }

        summary.Metadata["EvaluationScope"] = ProjectScope;
        summary.Metadata["AnalysisStatus"] = unreadableFiles == 0
            ? "Completed"
            : "Incomplete";
        summary.Metadata["MeasurementMethod"] =
            "Aggregated lexical analysis of supported source files.";
        summary.Metadata["SwallowedExceptionDefinition"] =
            "Potential swallowing: no recognized rethrow, raise, or logging indicator was detected.";
        var analysisComplete = filesAnalyzed + unreadableFiles == filesDiscovered;
        summary.Metadata["AnalysisComplete"] = analysisComplete.ToString();

        if (summary.Metrics.TryGetValue("ErrorHandlingCoverage", out var coverage))
        {
            var evidence = results
                .Where(result => result.Category == "ErrorHandling")
                .SelectMany(result =>
                {
                    var filePath = result.Metadata.GetValueOrDefault("FilePath");
                    var relativePath = string.IsNullOrWhiteSpace(filePath)
                        ? result.Target
                        : Path.GetRelativePath(
                            Context?.RootPath ?? Path.GetDirectoryName(filePath)!,
                            filePath);

                    return (result.Evidence ?? string.Empty)
                        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                        .Select(item => $"{relativePath}: {item}");
                })
                .Take(100)
                .ToArray();

            var evidenceSummary =
                $"{totalCatchBlocks - totalSwallowedExceptions} of {totalCatchBlocks} analyzed catch/except clauses have a recognized handling indicator; coverage {coverage:0.##}%.";

            summary.Evidence = string.Join(
                Environment.NewLine,
                new[] { evidenceSummary }.Concat(evidence));
            summary.Metadata["Evidence.ErrorHandlingCoverage"] = summary.Evidence;
            summary.Metadata["EvidenceSummary.ErrorHandlingCoverage"] = evidenceSummary;
        }

        results.Add(summary);
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

    private sealed record CatchEvidence(
        int LineNumber,
        string SourceLine,
        bool IsEmpty,
        bool IsGeneric,
        bool IsSwallowed);

    private readonly record struct CatchAnalysis(
        int TotalCatchBlocks,
        int EmptyCatchBlocks,
        int GenericCatchBlocks,
        int SwallowedExceptions,
        IReadOnlyCollection<CatchEvidence> Evidence)
    {
        public static CatchAnalysis Empty => new(0, 0, 0, 0, []);
    }
}
