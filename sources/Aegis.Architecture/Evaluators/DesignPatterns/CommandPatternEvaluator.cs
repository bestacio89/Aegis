using System.Text;
using System.Text.RegularExpressions;

using Aegis.Architecture.Diagnostics;
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
/// Evaluates command-like types and their relationship to handler-like types.
///
/// Produces lexical evidence about:
/// - Command and handler declarations
/// - Handler pairing
/// - Method-like declarations
/// - Configured forbidden dependency references
/// - Configured handler invocation hints
///
/// This evaluator does not resolve types, namespaces, dependency injection,
/// or call graphs. Its compliance score is a heuristic, not proof of
/// correct Command pattern implementation.
/// </summary>
public sealed class CommandPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    private static readonly Regex ClassDeclarationRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MethodDeclarationRegex = new(
        @"(?m)^\s*(?:(?:public|private|protected|internal|static|abstract|virtual|override|final|async|synchronized|default)\s+)*(?:[\w.<>,?\[\]]+\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonMethodDeclarationRegex = new(
        @"(?m)^\s*def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonClassDeclarationRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\([^)]*\))?\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override string Name => "CommandPatternEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript",
        "JavaScript"
    ];

    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "NestJS",
        "FastAPI"
    ];

    public CommandPatternEvaluator(
        ILogger<CommandPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Architecture.DesignPatterns
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

        if (!_policy.EnforceCommandPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Command pattern evaluation disabled by policy.");

            return results;
        }

        var files = ResolveSourceFiles(
                projectPath,
                ResolveExtensions())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "No supported source files were found.");

            return results;
        }

        var sourceFiles = new List<SourceFile>();
        var unreadableFileCount = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var content = await File.ReadAllTextAsync(file, token);
                var sanitized = SanitizeCode(
                    content,
                    Path.GetExtension(file));

                sourceFiles.Add(
                    new SourceFile(
                        file,
                        sanitized,
                        IsPythonFile(file)));
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
                unreadableFileCount++;

                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    $"Unable to read source file '{file}'.",
                    ex);
            }
        }

        token.ThrowIfCancellationRequested();

        // Discover handlers across the analyzed source set before evaluating
        // commands, so source-file ordering does not affect pairing.
        var handlers = DiscoverHandlers(sourceFiles);

        var commandResults = new List<ArchitectureEvaluatorResult>();
        var commandCount = 0;

        foreach (var source in sourceFiles)
        {
            token.ThrowIfCancellationRequested();

            foreach (var declaration in FindCommandDeclarations(source))
            {
                token.ThrowIfCancellationRequested();

                var commandName = declaration.Name;
                var expectedHandler = GetExpectedHandlerName(commandName);

                var hasHandler = expectedHandler is not null
                    && handlers.Contains(expectedHandler);

                var body = declaration.Body;

                var methodCount = CountMethods(
                    body,
                    source.IsPython);

                var forbiddenDependencyCount = CountConfiguredOccurrences(
                    body,
                    _policy.ForbiddenInCommand);

                var handlerInvocationCount = CountHandlerInvocations(body);

                var compliance = ComputeCompliance(
                    hasHandler,
                    methodCount,
                    forbiddenDependencyCount);

                commandResults.Add(
                    CreateResult(
                        source.Path,
                        commandName,
                        expectedHandler ?? string.Empty,
                        hasHandler,
                        methodCount,
                        forbiddenDependencyCount,
                        handlerInvocationCount,
                        compliance));

                commandCount++;
            }
        }

        results.AddRange(commandResults);

        // Only command-level results are passed to the summary.
        results.Add(
            CreateSummary(
                discoveredFileCount: files.Count,
                analyzedFileCount: sourceFiles.Count,
                unreadableFileCount: unreadableFileCount,
                commandResults: commandResults));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Command pattern evaluation completed. " +
            $"Files discovered: {files.Count}; " +
            $"Files analyzed: {sourceFiles.Count}; " +
            $"Unreadable files: {unreadableFileCount}; " +
            $"Command candidates: {commandCount}.");

        return results;
    }

    private string[] ResolveExtensions()
    {
        return Context?.Language?.Trim() switch
        {
            "C#" =>
            [
                ".cs"
            ],

            "Java" =>
            [
                ".java"
            ],

            "Python" =>
            [
                ".py"
            ],

            "TypeScript" =>
            [
                ".ts",
                ".tsx"
            ],

            "JavaScript" =>
            [
                ".js",
                ".jsx",
                ".mjs",
                ".cjs"
            ],

            _ =>
            [
                ".cs",
                ".java",
                ".py",
                ".ts",
                ".tsx",
                ".js",
                ".jsx",
                ".mjs",
                ".cjs"
            ]
        };
    }

    private static HashSet<string> DiscoverHandlers(
        IEnumerable<SourceFile> files)
    {
        var handlers = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var source in files)
        {
            var regex = source.IsPython
                ? PythonClassDeclarationRegex
                : ClassDeclarationRegex;

            foreach (Match match in regex.Matches(source.Content))
            {
                var name = match.Groups["name"].Value;

                if (name.EndsWith(
                        "Handler",
                        StringComparison.OrdinalIgnoreCase))
                {
                    handlers.Add(name);
                }
            }
        }

        return handlers;
    }

    private static IEnumerable<ClassDeclaration> FindCommandDeclarations(
        SourceFile source)
    {
        var regex = source.IsPython
            ? PythonClassDeclarationRegex
            : ClassDeclarationRegex;

        foreach (Match match in regex.Matches(source.Content))
        {
            var className = match.Groups["name"].Value;

            if (!className.EndsWith(
                    "Command",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var body = source.IsPython
                ? ExtractPythonClassBlock(source.Content, match.Index)
                : ExtractBraceClassBlock(source.Content, match.Index);

            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            yield return new ClassDeclaration(className, body);
        }
    }

    private static string? GetExpectedHandlerName(string commandName)
    {
        const string commandSuffix = "Command";

        if (!commandName.EndsWith(
                commandSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return commandName[..^commandSuffix.Length] + "Handler";
    }

    private int CountHandlerInvocations(string content)
    {
        if (_policy.HandlerInvocationHints is null)
        {
            return 0;
        }

        var count = 0;

        foreach (var hint in _policy.HandlerInvocationHints)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            // Treat configured hints as literal names, not executable regex.
            var pattern = $@"\b{Regex.Escape(hint)}\s*\(";

            count += Regex.Matches(
                content,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
        }

        return count;
    }

    private int CountConfiguredOccurrences(
        string content,
        IEnumerable<string>? forbiddenValues)
    {
        if (forbiddenValues is null)
        {
            return 0;
        }

        var count = 0;

        foreach (var forbidden in forbiddenValues)
        {
            if (string.IsNullOrWhiteSpace(forbidden))
            {
                continue;
            }

            // Count literal occurrences, not merely whether a policy entry
            // appears somewhere in the class.
            var startIndex = 0;

            while (startIndex < content.Length)
            {
                var index = content.IndexOf(
                    forbidden,
                    startIndex,
                    StringComparison.OrdinalIgnoreCase);

                if (index < 0)
                {
                    break;
                }

                count++;
                startIndex = index + forbidden.Length;
            }
        }

        return count;
    }

    private static int CountMethods(
        string body,
        bool isPython)
    {
        var regex = isPython
            ? PythonMethodDeclarationRegex
            : MethodDeclarationRegex;

        var count = 0;

        foreach (Match match in regex.Matches(body))
        {
            var name = match.Groups["name"].Value;

            if (name.Equals("if", StringComparison.Ordinal)
                || name.Equals("for", StringComparison.Ordinal)
                || name.Equals("while", StringComparison.Ordinal)
                || name.Equals("switch", StringComparison.Ordinal)
                || name.Equals("catch", StringComparison.Ordinal)
                || name.Equals("return", StringComparison.Ordinal))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private ArchitectureEvaluatorResult CreateResult(
        string file,
        string commandName,
        string expectedHandler,
        bool hasHandler,
        int methods,
        int forbidden,
        int invocations,
        double compliance)
    {
        var context = Context;

        var result = CreateResult(file, "DesignPattern");

        result.ProjectName = context?.ProjectName;
        result.Language = context?.Language;
        result.Framework = context?.Framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = context?.Confidence ?? 0;
        result.Category = nameof(ArchitectureRuleCategory.DesignPatterns);

        result.Metrics["HasHandlerPair"] = hasHandler ? 1 : 0;
        result.Metrics["MethodCount"] = methods;
        result.Metrics["ForbiddenDependencyCount"] = forbidden;
        result.Metrics["HandlerInvocationCount"] = invocations;
        result.Metrics["CommandComplianceScore"] = compliance;

        result.Metadata["FilePath"] = file;
        result.Metadata["CommandName"] = commandName;
        result.Metadata["ExpectedHandler"] = expectedHandler;
        result.Metadata["ArchitectureStyle"] =
            context?.ArchitectureStyle ?? "Unknown";
        result.Metadata["Layer"] = ResolveLayer(file) ?? "Unknown";
        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["MeasurementMethod"] =
            "Lexical command-class analysis; handler pairing uses class names across analyzed files.";
        result.Metadata["ComplianceScoreIsHeuristic"] = true.ToString();
        result.Metadata["TypeResolutionPerformed"] = false.ToString();
        result.Metadata["CallGraphResolved"] = false.ToString();

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        int discoveredFileCount,
        int analyzedFileCount,
        int unreadableFileCount,
        IReadOnlyCollection<ArchitectureEvaluatorResult> commandResults)
    {
        var context = Context;

        var result = new ArchitectureEvaluatorResult(
            Name,
            "CommandPatternSummary")
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
        result.Metrics["CommandCount"] = commandResults.Count;

        result.Metrics["CommandsWithHandlerPair"] = commandResults.Count(
            item => item.Metrics.GetValueOrDefault("HasHandlerPair") > 0);

        result.Metrics["TotalForbiddenDependencyOccurrences"] =
            commandResults.Sum(
                item => item.Metrics.GetValueOrDefault("ForbiddenDependencyCount"));

        result.Metrics["TotalHandlerInvocationHints"] =
            commandResults.Sum(
                item => item.Metrics.GetValueOrDefault("HandlerInvocationCount"));

        result.Metrics["AverageCommandCompliance"] = commandResults.Count == 0
            ? 0
            : commandResults.Average(
                item => item.Metrics.GetValueOrDefault("CommandComplianceScore"));

        result.Metadata["EvaluationScope"] = ProjectScope;
        result.Metadata["MeasurementMethod"] =
            "Project-level aggregation of command candidate results.";
        result.Metadata["ComplianceScoreIsHeuristic"] = true.ToString();

        return result;
    }

    private double ComputeCompliance(
        bool hasHandler,
        int methods,
        int forbidden)
    {
        var methodThreshold = Math.Max(
            1,
            _policy.MaxMethodsPerCommand);

        var methodRatio = Math.Min(
            1.0,
            methods / (double)methodThreshold);

        // Handler invocation count is reported separately. It is not
        // automatically penalized because handler calls can be legitimate.
        var forbiddenPenalty = Math.Min(1.0, forbidden / 3.0);

        var score =
            (hasHandler ? 0.5 : 0.0)
            + ((1.0 - methodRatio) * 0.25)
            + ((1.0 - forbiddenPenalty) * 0.25);

        return Math.Round(score * 100.0, 2);
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

        var headerIndent = CountIndentation(content, lineStart, classIndex);
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
                var indentation = CountIndentation(line);

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

    private static int CountIndentation(
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

    private static int CountIndentation(ReadOnlySpan<char> line)
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
    /// Masks comments and literals while preserving offsets and line breaks.
    /// This is a best-effort lexical sanitizer, not a full language lexer.
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

    private static bool IsPythonFile(string file)
    {
        return file.EndsWith(
            ".py",
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SourceFile(
        string Path,
        string Content,
        bool IsPython);

    private sealed record ClassDeclaration(
        string Name,
        string Body);
}