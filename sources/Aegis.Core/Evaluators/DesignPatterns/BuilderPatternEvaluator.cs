using System.Text;
using System.Text.RegularExpressions;

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
        "ASP.NET",
        "Spring",
        "Angular",
        "FastAPI",
        "DDD",
        "Hexagonal",
        "Microservices",
        "EventDriven"
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

            var sanitized = SanitizeCode(
                content,
                Path.GetExtension(file));

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

                var buildMethodCount = BuildMethodRegex
                    .Matches(classBlock)
                    .Count;

                var fluentReturnCount = FluentReturnRegex
                    .Matches(classBlock)
                    .Count;

                var fluentSignatureCount = FluentSignatureRegex
                    .Matches(classBlock)
                    .Count;

                var mutationCount = MutationRegex
                    .Matches(classBlock)
                    .Count;

                var readonlyIndicatorCount = ReadonlyIndicatorRegex
                    .Matches(classBlock)
                    .Count;

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
                        hasReadonlySignals));

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
        bool hasReadonlySignals)
    {
        var context = Context;

        var result = CreateResult(file, "DesignPattern");

        result.ProjectName = context?.ProjectName;
        result.Language = context?.Language;
        result.Framework = context?.Framework;
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

        result.Metadata["FilePath"] = file;
        result.Metadata["BuilderClassName"] = className;
        result.Metadata["Language"] = context?.Language ?? "Unknown";
        result.Metadata["Framework"] = context?.Framework ?? "Unknown";
        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["MeasurementMethod"] =
            "Lexical analysis of builder-like class declarations and member-body signals.";
        result.Metadata["PatternVerified"] = false.ToString();
        result.Metadata["ImmutabilityVerified"] = false.ToString();
        result.Metadata["SemanticAnalysisPerformed"] = false.ToString();

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

        return result;
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