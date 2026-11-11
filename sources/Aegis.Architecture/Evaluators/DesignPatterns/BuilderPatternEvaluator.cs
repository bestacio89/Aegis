using System.Text;
using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators.Analysis;

using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Evaluates builder-like classes and exposes lexical evidence about
/// construction methods, fluent returns, state mutation, and immutability
/// indicators.
///
/// This evaluator does not prove that a class correctly implements the
/// Builder pattern or that a constructed object is immutable.
/// RuleEngine is responsible for interpreting the resulting evidence.
/// </summary>
public sealed class BuilderPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    private static readonly Regex ClassDeclarationRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BuildMethodRegex = new(
        @"\b(?:Build|build|BuildAsync|buildAsync)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FluentReturnRegex = new(
        @"\breturn\s+(?:this|self)\s*;?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MutationRegex = new(
        @"(?<![\w.])(?:this\.|self\.)?[A-Za-z_][A-Za-z0-9_]*(?:\s*\[[^\]\r\n]+\])?\s*(?:=(?!=|>)|\+=|-=|\*=|/=|%=|&=|\|=|\+\+|--)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReadonlyIndicatorRegex = new(
        @"\b(?:readonly|final|Immutable|ImmutableArray|ImmutableList|ImmutableDictionary|FrozenSet|FrozenDictionary)\b|Object\s*\.\s*freeze\s*\(|frozen\s*=\s*True",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FluentSignatureRegex = new(
        @"(?m)^\s*(?:(?:public|private|protected|internal|static|async|virtual|override|final|synchronized)\s+)*(?:[\w.<>,?\[\]]+\s+)?[A-Za-z_][A-Za-z0-9_]*\s*\([^;{}]*\)\s*(?::\s*[\w.<>,?\[\]]+)?\s*(?:\{|=>|:)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override string Name => "BuilderPatternEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript",
        "Python"
    ];

    public override string[] SupportedFrameworks =>
    [
        "LanguageAgnostic"
    ];

    public BuilderPatternEvaluator(
        ILogger<BuilderPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.DesignPatterns
            ?? new DesignPatternPolicy();
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

        if (!_policy.EnforceBuilderPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Builder pattern evaluation disabled by policy.");

            return results;
        }

        var files = ResolveSourceFiles(
                projectPath,
                ".cs",
                ".java",
                ".ts",
                ".py")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var analyzedFiles = 0;
        var unreadableFiles = 0;
        var candidateTypes = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                unreadableFiles++;

                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    $"Unable to read source file '{file}'.",
                    ex);

                continue;
            }

            analyzedFiles++;

            token.ThrowIfCancellationRequested();

            var sanitized = SourceCodeMasker.Mask(content);

            var isPython = file.EndsWith(
                ".py",
                StringComparison.OrdinalIgnoreCase);

            foreach (Match match in ClassDeclarationRegex.Matches(sanitized))
            {
                token.ThrowIfCancellationRequested();

                var className = match.Groups["name"].Value;

                var classBlock = isPython
                    ? ExtractPythonClassBlock(sanitized, match.Index)
                    : ExtractBraceClassBlock(sanitized, match.Index);

                if (string.IsNullOrWhiteSpace(classBlock))
                {
                    continue;
                }

                var buildMatches = BuildMethodRegex.Matches(classBlock)
                    .Cast<Match>()
                    .ToArray();
                var fluentReturnMatches = FluentReturnRegex.Matches(classBlock)
                    .Cast<Match>()
                    .ToArray();
                var fluentSignatureMatches = FluentSignatureRegex.Matches(classBlock)
                    .Cast<Match>()
                    .ToArray();
                var mutationMatches = MutationRegex.Matches(classBlock)
                    .Cast<Match>()
                    .ToArray();
                var readonlyMatches = ReadonlyIndicatorRegex.Matches(classBlock)
                    .Cast<Match>()
                    .ToArray();

                var buildMethodCount = buildMatches.Length;
                var fluentReturnCount = fluentReturnMatches.Length;
                var fluentSignatureCount = fluentSignatureMatches.Length;
                var mutationCount = mutationMatches.Length;
                var readonlyIndicatorCount = readonlyMatches.Length;
                var language = DetectLanguage(file);
                var framework = ResolveFrameworkForFile(file, projectPath, context);
                var classBodyStart = GetClassBodyStart(
                    sanitized,
                    match.Index,
                    isPython);

                var nameSuggestsBuilder = className.EndsWith(
                    "Builder",
                    StringComparison.OrdinalIgnoreCase);

                // Avoid treating every class with a method named Build as
                // a builder. Name-based candidates are retained even when
                // the implementation has no recognizable Build method.
                var hasBuilderEvidence =
                    nameSuggestsBuilder
                    || (buildMethodCount > 0 && fluentReturnCount > 0);

                if (!hasBuilderEvidence)
                {
                    continue;
                }

                var hasBuildMethod = buildMethodCount > 0;
                var hasFluentReturns = fluentReturnCount > 0;
                var hasMutationSignals = mutationCount > 0;
                var hasReadonlySignals = readonlyIndicatorCount > 0;

                results.Add(
                    CreateTypeResult(
                        file,
                        className,
                        hasBuildMethod,
                        buildMethodCount,
                        fluentReturnCount,
                        fluentSignatureCount,
                        mutationCount,
                        readonlyIndicatorCount,
                        nameSuggestsBuilder,
                        hasFluentReturns,
                        hasMutationSignals,
                        hasReadonlySignals,
                        language,
                        framework,
                        content,
                        sanitized,
                        match,
                        classBodyStart,
                        buildMatches,
                        fluentReturnMatches,
                        mutationMatches,
                        readonlyMatches));

                candidateTypes++;
            }
        }

        token.ThrowIfCancellationRequested();

        results.Add(
            CreateSummary(
                discoveredFileCount: files.Count,
                analyzedFileCount: analyzedFiles,
                unreadableFileCount: unreadableFiles,
                candidateTypeCount: candidateTypes,
                candidateResults: results));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Builder pattern evaluation completed. " +
            $"Files discovered: {files.Count}; " +
            $"Files analyzed: {analyzedFiles}; " +
            $"Unreadable files: {unreadableFiles}; " +
            $"Builder candidates: {candidateTypes}.");

        return results;
    }

    private ArchitectureEvaluatorResult CreateTypeResult(
        string file,
        string className,
        bool hasBuildMethod,
        int buildMethodCount,
        int fluentReturnCount,
        int fluentSignatureCount,
        int mutationCount,
        int readonlyIndicatorCount,
        bool nameSuggestsBuilder,
        bool hasFluentReturns,
        bool hasMutationSignals,
        bool hasReadonlySignals,
        string language,
        string? framework,
        string source,
        string sanitizedSource,
        Match classMatch,
        int classBodyStart,
        IReadOnlyCollection<Match> buildMatches,
        IReadOnlyCollection<Match> fluentReturnMatches,
        IReadOnlyCollection<Match> mutationMatches,
        IReadOnlyCollection<Match> readonlyMatches)
    {
        var result = CreateResult(file, "DesignPattern");

        result.Target = className;
        result.Language = language;
        result.Framework = framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = context?.Confidence ?? 0;
        result.Category = "DesignPattern";

        result.Metrics["HasBuildMethod"] = hasBuildMethod ? 1 : 0;
        result.Metrics["BuildMethodCount"] = buildMethodCount;
        result.Metrics["FluentReturnCount"] = fluentReturnCount;
        result.Metrics["FluentSignatureCount"] = fluentSignatureCount;
        result.Metrics["MutationCount"] = mutationCount;
        result.Metrics["ReadonlyIndicatorCount"] = readonlyIndicatorCount;
        result.Metrics["NameSuggestsBuilder"] = nameSuggestsBuilder ? 1 : 0;
        result.Metrics["HasFluentReturns"] = hasFluentReturns ? 1 : 0;
        result.Metrics["HasMutationSignals"] = hasMutationSignals ? 1 : 0;
        result.Metrics["HasReadonlySignals"] = hasReadonlySignals ? 1 : 0;

        // Score only confidently identified builder candidates. Build() and
        // fluent chaining are independently required signals; mutation without
        // a fluent return is reported separately rather than treating every
        // state mutation as a violation.
        // Fluent chaining is a style choice, not a mandatory Builder pattern
        // requirement. A named builder with a Build method is structurally valid.
        result.Metrics["BuilderPatternComplianceIndex"] =
            hasBuildMethod ? 100d : 0d;
        result.Metrics["BuilderMutabilityViolationCount"] =
            hasMutationSignals && !hasFluentReturns ? 1d : 0d;

        result.Metadata["FilePath"] = file;
        result.Metadata["BuilderClassName"] = className;
        result.Metadata["Language"] = language;
        result.Metadata["Framework"] = framework ?? "Unknown";
        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["MeasurementMethod"] =
            "Lexical analysis of builder-like class declarations and member-body signals.";
        result.Metadata["PatternVerified"] = false.ToString();
        result.Metadata["ImmutabilityVerified"] = false.ToString();
        result.Metadata["SemanticAnalysisPerformed"] = false.ToString();
        result.Metadata["AnalysisComplete"] = bool.TrueString;
        result.Metadata["BuilderPatternMeasurement"] =
            "A builder candidate requires a Build method. Fluent chaining is reported as an informational convention and is not required for compliance.";
        result.Metadata["BuilderMutabilityInterpretation"] =
            "Diagnostic only. Internal mutation is normal during builder configuration and does not prove externally exposed mutable state.";

        var classLine = GetLineNumber(sanitizedSource, classMatch.Index);
        var classSnippet = GetSourceLine(source, classLine);
        var buildEvidence = BuildMatchEvidence(
            "Build method",
            buildMatches,
            sanitizedSource,
            source,
            classBodyStart);
        var fluentEvidence = BuildMatchEvidence(
            "Fluent return",
            fluentReturnMatches,
            sanitizedSource,
            source,
            classBodyStart);
        var mutationEvidence = BuildMatchEvidence(
            "State mutation",
            mutationMatches,
            sanitizedSource,
            source,
            classBodyStart);
        var readonlyEvidence = BuildMatchEvidence(
            "Readonly/immutable indicator",
            readonlyMatches,
            sanitizedSource,
            source,
            classBodyStart);

        var complianceEvidence =
            $"Builder '{className}' has {buildMethodCount} Build method(s); " +
            $"fluent returns {fluentReturnCount} (informational); compliance index " +
            $"{result.Metrics["BuilderPatternComplianceIndex"]:0.##}/100.";
        AddMetricEvidence(
            result,
            "BuilderPatternComplianceIndex",
            $"{complianceEvidence}{Environment.NewLine}{buildEvidence}{Environment.NewLine}{fluentEvidence}",
            classLine,
            classSnippet);
        AddMetricEvidence(
            result,
            "BuilderMutabilityViolationCount",
            $"Mutation signals: {mutationCount}; readonly/immutable indicators: {readonlyIndicatorCount}. " +
            "This is a lexical diagnostic and does not establish externally visible mutability." +
            $"{Environment.NewLine}{mutationEvidence}{Environment.NewLine}{readonlyEvidence}",
            mutationMatches.Count > 0
                ? GetLineNumber(sanitizedSource, classBodyStart + mutationMatches.First().Index)
                : classLine,
            mutationMatches.Count > 0
                ? GetSourceLine(
                    source,
                    GetLineNumber(sanitizedSource, classBodyStart + mutationMatches.First().Index))
                : classSnippet);

        result.Evidence = string.Join(
            Environment.NewLine,
            new[] { complianceEvidence, buildEvidence, fluentEvidence, mutationEvidence, readonlyEvidence });
        result.LineNumber = classLine;

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        int discoveredFileCount,
        int analyzedFileCount,
        int unreadableFileCount,
        int candidateTypeCount,
        IEnumerable<ArchitectureEvaluatorResult> candidateResults)
    {
        var context = Context;
        var candidates = candidateResults.ToList();

        var result = new ArchitectureEvaluatorResult(
            Name,
            "BuilderPatternSummary")
        {
            ProjectName = context?.ProjectName,
            Language = context?.Language,
            Framework = context?.Framework,
            DetectionConfidence = context?.Confidence ?? 0,
            Category = "DesignPatternSummary"
        };

        result.Metrics["DiscoveredFileCount"] = discoveredFileCount;
        result.Metrics["AnalyzedFileCount"] = analyzedFileCount;
        result.Metrics["UnreadableFileCount"] = unreadableFileCount;
        result.Metrics["BuilderCandidateCount"] = candidateTypeCount;

        result.Metrics["BuildMethodCount"] = candidates.Sum(
            candidate => candidate.Metrics.GetValueOrDefault("BuildMethodCount"));

        result.Metrics["FluentReturnCount"] = candidates.Sum(
            candidate => candidate.Metrics.GetValueOrDefault("FluentReturnCount"));

        result.Metrics["MutationCount"] = candidates.Sum(
            candidate => candidate.Metrics.GetValueOrDefault("MutationCount"));

        result.Metrics["ReadonlyIndicatorCount"] = candidates.Sum(
            candidate => candidate.Metrics.GetValueOrDefault("ReadonlyIndicatorCount"));

        result.Metrics["CandidatesWithBuildMethod"] = candidates.Count(
            candidate => candidate.Metrics.GetValueOrDefault("HasBuildMethod") > 0);

        result.Metrics["CandidatesWithFluentReturns"] = candidates.Count(
            candidate => candidate.Metrics.GetValueOrDefault("HasFluentReturns") > 0);

        result.Metrics["CandidatesWithMutationSignals"] = candidates.Count(
            candidate => candidate.Metrics.GetValueOrDefault("HasMutationSignals") > 0);

        result.Metrics["CandidatesWithReadonlySignals"] = candidates.Count(
            candidate => candidate.Metrics.GetValueOrDefault("HasReadonlySignals") > 0);

        result.Metadata["EvaluationScope"] = ProjectScope;
        result.Metadata["MeasurementMethod"] =
            "Project-level aggregation of lexical builder-pattern candidate evidence.";
        result.Metadata["PatternVerified"] = false.ToString();
        result.Metadata["ImmutabilityVerified"] = false.ToString();
        var analysisComplete = unreadableFileCount == 0
            && analyzedFileCount == discoveredFileCount;
        result.Metadata["AnalysisComplete"] = analysisComplete.ToString();
        result.Metadata["AnalysisStatus"] = analysisComplete
            ? "Completed"
            : "CompletedWithDiagnostics";

        return result;
    }

    private static int GetClassBodyStart(
        string source,
        int classIndex,
        bool isPython)
    {
        if (isPython)
        {
            var headerEnd = source.IndexOf('\n', classIndex);
            return headerEnd < 0 ? source.Length : headerEnd + 1;
        }

        var openingBrace = source.IndexOf('{', classIndex);
        return openingBrace < 0 ? classIndex : openingBrace + 1;
    }

    private static string BuildMatchEvidence(
        string label,
        IReadOnlyCollection<Match> matches,
        string sanitizedSource,
        string source,
        int bodyStart)
    {
        if (matches.Count == 0)
            return $"No {label.ToLowerInvariant()} signals were detected.";

        return string.Join(
            Environment.NewLine,
            matches.Take(50).Select(match =>
            {
                var line = GetLineNumber(sanitizedSource, bodyStart + match.Index);
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

    private string? ResolveLayer(string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(layer =>
                layer.Files.Contains(
                    file,
                    StringComparer.OrdinalIgnoreCase))
            ?.Name;
    }

    private static string ExtractBraceClassBlock(
        string content,
        int start)
    {
        var openingBrace = content.IndexOf('{', start);

        if (openingBrace < 0)
        {
            return string.Empty;
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
                    return content[(openingBrace + 1)..index];
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractPythonClassBlock(
        string content,
        int classIndex)
    {
        var lineStart = content.LastIndexOf('\n', classIndex);

        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var headerEnd = content.IndexOf('\n', classIndex);

        if (headerEnd < 0)
        {
            return string.Empty;
        }

        var headerIndent = CountLeadingWhitespace(
            content,
            lineStart,
            classIndex);

        var bodyStart = headerEnd + 1;
        var position = bodyStart;

        while (position < content.Length)
        {
            var lineEnd = content.IndexOf('\n', position);

            if (lineEnd < 0)
            {
                lineEnd = content.Length;
            }

            var line = content.AsSpan(position, lineEnd - position);

            if (!IsWhitespaceOnly(line))
            {
                var indentation = CountLeadingWhitespace(line);

                if (indentation <= headerIndent)
                {
                    return content[bodyStart..position];
                }
            }

            position = lineEnd + 1;
        }

        return bodyStart <= content.Length
            ? content[bodyStart..]
            : string.Empty;
    }

    private static int CountLeadingWhitespace(
        string content,
        int lineStart,
        int end)
    {
        var count = 0;

        for (var index = lineStart;
             index < end && content[index] is ' ' or '\t';
             index++)
        {
            count++;
        }

        return count;
    }

    private static int CountLeadingWhitespace(
        ReadOnlySpan<char> line)
    {
        var count = 0;

        while (count < line.Length && line[count] is ' ' or '\t')
        {
            count++;
        }

        return count;
    }

    private static bool IsWhitespaceOnly(ReadOnlySpan<char> line)
    {
        foreach (var character in line)
        {
            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Masks comments and string/character literals while preserving newlines
    /// and source offsets. This supports common C#, Java, TypeScript, and
    /// Python syntax but is not a complete language lexer.
    /// </summary>
    private static string SanitizeCode(
        string content,
        string extension)
    {
        var isPython = extension.Equals(
            ".py",
            StringComparison.OrdinalIgnoreCase);

        var buffer = new StringBuilder(content);
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inCharacter = false;
        var verbatimString = false;
        var rawQuoteCount = 0;

        for (var index = 0; index < content.Length; index++)
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
                else if (current != '\r')
                {
                    buffer[index] = ' ';
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    buffer[index] = ' ';
                    buffer[index + 1] = ' ';
                    index++;
                    inBlockComment = false;
                    continue;
                }

                if (current is not '\r' and not '\n')
                {
                    buffer[index] = ' ';
                }

                continue;
            }

            if (inString)
            {
                if (rawQuoteCount > 0)
                {
                    if (current == '"'
                        && CountConsecutiveQuotes(content, index) >= rawQuoteCount)
                    {
                        for (var offset = 0; offset < rawQuoteCount; offset++)
                        {
                            buffer[index + offset] = ' ';
                        }

                        index += rawQuoteCount - 1;
                        inString = false;
                        rawQuoteCount = 0;
                        continue;
                    }

                    if (current is not '\r' and not '\n')
                    {
                        buffer[index] = ' ';
                    }

                    continue;
                }

                if (verbatimString && current == '"' && next == '"')
                {
                    buffer[index] = ' ';
                    buffer[index + 1] = ' ';
                    index++;
                    continue;
                }

                if (!verbatimString && current == '\\')
                {
                    buffer[index] = ' ';

                    if (index + 1 < content.Length)
                    {
                        if (content[index + 1] is not '\r' and not '\n')
                        {
                            buffer[index + 1] = ' ';
                        }

                        index++;
                    }

                    continue;
                }

                if (current == '"')
                {
                    buffer[index] = ' ';
                    inString = false;
                    verbatimString = false;
                    continue;
                }

                if (current is not '\r' and not '\n')
                {
                    buffer[index] = ' ';
                }

                continue;
            }

            if (inCharacter)
            {
                if (current == '\\')
                {
                    buffer[index] = ' ';

                    if (index + 1 < content.Length)
                    {
                        if (content[index + 1] is not '\r' and not '\n')
                        {
                            buffer[index + 1] = ' ';
                        }

                        index++;
                    }

                    continue;
                }

                if (current == '\'')
                {
                    buffer[index] = ' ';
                    inCharacter = false;
                    continue;
                }

                if (current is not '\r' and not '\n')
                {
                    buffer[index] = ' ';
                }

                continue;
            }

            if (isPython && current == '#')
            {
                buffer[index] = ' ';
                inLineComment = true;
                continue;
            }

            if (!isPython && current == '/' && next == '/')
            {
                buffer[index] = ' ';
                buffer[index + 1] = ' ';
                index++;
                inLineComment = true;
                continue;
            }

            if (!isPython && current == '/' && next == '*')
            {
                buffer[index] = ' ';
                buffer[index + 1] = ' ';
                index++;
                inBlockComment = true;
                continue;
            }

            if (current == '"')
            {
                var quoteCount = CountConsecutiveQuotes(content, index);

                if (quoteCount >= 3)
                {
                    rawQuoteCount = quoteCount;
                    inString = true;

                    for (var offset = 0; offset < quoteCount; offset++)
                    {
                        buffer[index + offset] = ' ';
                    }

                    index += quoteCount - 1;
                    continue;
                }

                verbatimString = !isPython
                    && index > 0
                    && content[index - 1] == '@';

                inString = true;
                buffer[index] = ' ';
                continue;
            }

            if (current == '\'')
            {
                inCharacter = true;
                buffer[index] = ' ';
            }
        }

        return buffer.ToString();
    }

    private static int CountConsecutiveQuotes(
        string content,
        int startIndex)
    {
        var count = 0;

        for (var index = startIndex;
             index < content.Length && content[index] == '"';
             index++)
        {
            count++;
        }

        return count;
    }
}