using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Evaluates lexical signals associated with Singleton implementations.
///
/// Detects:
/// - Static instance members
/// - getInstance-style accessors
/// - Private constructors
/// - Lazy initialization signals
/// - Thread-safety mechanisms
/// - Singleton naming conventions
///
/// This evaluator does not prove runtime uniqueness or thread safety.
/// Results are heuristic indicators, not semantic verification.
/// </summary>
public sealed class SingletonPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "SingletonPatternEvaluator";

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
        "Flask",
        "FastAPI",
        "CleanArchitecture",
        "Hexagonal",
        "DDD",
        "Microservices"
    ];

    private static readonly Regex StringLiteralRegex = new(
        """
        @(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`
        """,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommentRegex = new(
        @"//[^\r\n]*|/\*[\s\S]*?\*/|#[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BraceClassRegex = new(
        """
        \bclass\s+(?<name>[A-Za-z_$]\w*)
        [^{;]*\{
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex PythonClassRegex = new(
        """
        ^[ \t]*class\s+(?<name>[A-Za-z_]\w*)
        (?:\s*\([^)]*\))?\s*:
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex StaticInstanceMemberRegex = new(
        """
        \bstatic\b
        [^;{}\r\n]*
        \b(?:instance|_instance|INSTANCE|Instance|singleton)\b
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex GetInstanceRegex = new(
        @"\bgetInstance\s*\(",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex LazyInitializationRegex = new(
        """
        \bLazy\s*<
        |\blazy\b
        |\bgetInstance\s*\([^)]*\)\s*\{[^}]*\bnew\b
        |\b__new__\s*\(
        """,
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex ThreadSafetyRegex = new(
        """
        \block\s*\(
        |\bsynchronized\b
        |\bvolatile\b
        |\bInterlocked\.
        |\bAtomicReference\b
        |\bthreading\.Lock\b
        |\bRLock\s*\(
        """,
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex PythonSingletonRegex = new(
        """
        \b__new__\s*\(
        |\bmetaclass\s*=\s*Singleton\b
        |\bclass\s+\w+\s*\(\s*Singleton\s*\)
        """,
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex PythonInstanceCacheRegex = new(
        """
        \b(?:_instance|instance)\b
        |\bcls\.__dict__
        |\bhasattr\s*\(\s*cls
        """,
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    public SingletonPatternEvaluator(
        ILogger<SingletonPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceSingletonPattern)
        {
            _logger.LogInformation(
                "{Evaluator} disabled by policy.",
                Name);

            return results;
        }

        var files = ResolveSourceFiles(
            projectPath,
            ".cs",
            ".java",
            ".py",
            ".ts");

        var analyzedFiles = 0;
        var unreadableFiles = 0;
        var candidateCount = 0;

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
                    "{Evaluator} could not read {FilePath}.",
                    Name,
                    file);

                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    ex,
                    "{Evaluator} has no access to {FilePath}.",
                    Name,
                    file);

                continue;
            }

            analyzedFiles++;

            var language = DetectLanguage(file);
            var sanitized = RemoveCommentsAndStrings(content);

            var classRegex = language == "Python"
                ? PythonClassRegex
                : BraceClassRegex;

            foreach (Match classMatch in classRegex.Matches(sanitized))
            {
                token.ThrowIfCancellationRequested();

                var className = classMatch.Groups["name"].Value;

                if (!TryExtractClassBody(
                    sanitized,
                    classMatch,
                    language,
                    out var classBody))
                {
                    _logger.LogDebug(
                        "{Evaluator} could not isolate class body for {ClassName} in {FilePath}.",
                        Name,
                        className,
                        file);

                    continue;
                }

                var classHeader = sanitized.Substring(
                    classMatch.Index,
                    classMatch.Length);

                var hasStaticInstance =
                    StaticInstanceMemberRegex.IsMatch(classBody);

                var hasGetInstance =
                    GetInstanceRegex.IsMatch(classBody);

                var hasPrivateConstructor =
                    HasPrivateConstructor(classBody, className, language);

                var hasPythonSingletonSignals =
                    language == "Python" &&
                    PythonSingletonRegex.IsMatch(classHeader + "\n" + classBody) &&
                    PythonInstanceCacheRegex.IsMatch(classBody);

                // Avoid classifying a consumer of getInstance() as a
                // Singleton implementation without implementation signals.
                var isCandidate =
                    hasStaticInstance ||
                    (hasGetInstance && hasPrivateConstructor) ||
                    hasPythonSingletonSignals;

                if (!isCandidate)
                {
                    continue;
                }

                candidateCount++;

                var hasThreadSafety =
                    ThreadSafetyRegex.IsMatch(classBody);

                var hasLazyInitialization =
                    LazyInitializationRegex.IsMatch(classBody);

                var namingValid = IsNamingConventionSatisfied(className);

                // These are detected signals, not proof of thread safety,
                // lazy behavior, or exactly-one-instance semantics.
                var threadSafetyScore = hasThreadSafety ? 1d : 0d;
                var lazyInitializationScore = hasLazyInitialization ? 1d : 0d;
                var instanceDisciplineScore =
                    hasStaticInstance || hasPythonSingletonSignals ? 1d : 0d;
                var namingAdherenceScore = namingValid ? 1d : 0d;

                var complianceScore = ComputeCompliance(
                    threadSafetyScore,
                    lazyInitializationScore,
                    instanceDisciplineScore,
                    namingAdherenceScore);

                var result = new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        ArchitectureRuleCategory.DesignPatterns.ToString()
                };

                result.Metrics["IsSingleton"] = 1;
                result.Metrics["ThreadSafetyScore"] = threadSafetyScore;
                result.Metrics["LazyInitializationScore"] =
                    lazyInitializationScore;
                result.Metrics["InstanceDisciplineScore"] =
                    instanceDisciplineScore;
                result.Metrics["NamingAdherenceScore"] =
                    namingAdherenceScore;
                result.Metrics["SingletonComplianceScore"] =
                    complianceScore;

                result.Metrics["HasStaticInstanceMember"] =
                    hasStaticInstance ? 1 : 0;
                result.Metrics["HasGetInstanceAccessor"] =
                    hasGetInstance ? 1 : 0;
                result.Metrics["HasPrivateConstructor"] =
                    hasPrivateConstructor ? 1 : 0;

                result.Metadata[BaseArchitectureEvaluator.FilePathMetadataKey] =
                    file;
                result.Metadata[BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
                    BaseArchitectureEvaluator.FileScope;
                result.Metadata["ClassName"] = className;
                result.Metadata["Language"] = language;
                result.Metadata["ThreadSafetySignalDetected"] =
                    hasThreadSafety.ToString();
                result.Metadata["LazyInitializationSignalDetected"] =
                    hasLazyInitialization.ToString();
                result.Metadata["NamingConventionSatisfied"] =
                    namingValid.ToString();
                result.Metadata["SemanticAnalysisPerformed"] =
                    bool.FalseString;
                result.Metadata["RuntimeUniquenessVerified"] =
                    bool.FalseString;
                result.Metadata["ThreadSafetyVerified"] =
                    bool.FalseString;
                result.Metadata["PolicySingletonSuffix"] =
                    _policy.SingletonSuffix ?? string.Empty;

                results.Add(result);
            }
        }

        // Snapshot file-level findings before adding the project summary.
        var singletonResults = results.ToList();

        var singletonCount = singletonResults.Count;

        var averageScore = AverageMetric(
            singletonResults,
            "SingletonComplianceScore");

        var averageThreadSafety = AverageMetric(
            singletonResults,
            "ThreadSafetyScore");

        var averageLazyInitialization = AverageMetric(
            singletonResults,
            "LazyInitializationScore");

        var averageInstanceDiscipline = AverageMetric(
            singletonResults,
            "InstanceDisciplineScore");

        var namingViolations = singletonResults.Count(
            result => GetMetric(result, "NamingAdherenceScore") < 1d);

        var overallHealth = singletonCount > 0
            ? averageScore *
              (1d - namingViolations / (double)singletonCount)
            : 0d;

        var summary = new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            Category =
                ArchitectureRuleCategory.DesignPatterns.ToString()
        };

        summary.Metrics["DiscoveredFiles"] = files.Count;
        summary.Metrics["AnalyzedFiles"] = analyzedFiles;
        summary.Metrics["UnreadableFiles"] = unreadableFiles;
        summary.Metrics["SingletonCount"] = singletonCount;
        summary.Metrics["AverageComplianceScore"] = averageScore;
        summary.Metrics["AverageThreadSafetySignal"] = averageThreadSafety;
        summary.Metrics["AverageLazyInitializationSignal"] =
            averageLazyInitialization;
        summary.Metrics["AverageInstanceDisciplineSignal"] =
            averageInstanceDiscipline;
        summary.Metrics["NamingViolationCount"] = namingViolations;
        summary.Metrics["OverallSingletonHealth"] = overallHealth;

        summary.Metadata[BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
            BaseArchitectureEvaluator.ProjectScope;
        summary.Metadata["Evaluator"] = Name;
        summary.Metadata["PolicyEnabled"] =
            _policy.EnforceSingletonPattern.ToString();
        summary.Metadata["SemanticAnalysisPerformed"] =
            bool.FalseString;
        summary.Metadata["RuntimeUniquenessVerified"] =
            bool.FalseString;
        summary.Metadata["EmptyCandidateSet"] =
            (singletonCount == 0).ToString();

        results.Add(summary);

        _logger.LogInformation(
            "{Evaluator} completed: {DiscoveredFiles} files discovered, " +
            "{AnalyzedFiles} readable files, {CandidateCount} Singleton candidates, " +
            "{UnreadableFiles} unreadable files.",
            Name,
            files.Count,
            analyzedFiles,
            candidateCount,
            unreadableFiles);

        return results;
    }

    private static string RemoveCommentsAndStrings(string source)
    {
        // Preserve offsets and line breaks for class-body extraction.
        var withoutStrings = StringLiteralRegex.Replace(
            source,
            match => MaskPreservingLineBreaks(match.Value));

        return CommentRegex.Replace(
            withoutStrings,
            match => MaskPreservingLineBreaks(match.Value));
    }

    private static string MaskPreservingLineBreaks(string value)
    {
        var chars = value.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is not '\r' and not '\n')
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    private static bool TryExtractClassBody(
        string source,
        Match classMatch,
        string language,
        out string body)
    {
        body = string.Empty;

        if (language == "Python")
        {
            var bodyStartSearch = classMatch.Index + classMatch.Length;

            var newline = source.IndexOf('\n', bodyStartSearch);

            if (newline < 0)
            {
                return false;
            }

            var lineStart = source.LastIndexOf(
                '\n',
                Math.Max(0, classMatch.Index - 1));

            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            var classIndent = CountIndent(
                source,
                lineStart,
                classMatch.Index);

            var bodyStart = newline + 1;
            var position = bodyStart;

            while (position < source.Length)
            {
                var lineEnd = source.IndexOf('\n', position);

                if (lineEnd < 0)
                {
                    lineEnd = source.Length;
                }

                var line = source.AsSpan(position, lineEnd - position);
                var firstNonWhitespace = 0;

                while (firstNonWhitespace < line.Length &&
                       line[firstNonWhitespace] is ' ' or '\t' or '\r')
                {
                    firstNonWhitespace++;
                }

                if (firstNonWhitespace < line.Length)
                {
                    var indent = CountIndent(
                        source,
                        position,
                        position + firstNonWhitespace);

                    if (indent <= classIndent)
                    {
                        body = source.Substring(
                            bodyStart,
                            position - bodyStart);

                        return true;
                    }
                }

                position = lineEnd < source.Length
                    ? lineEnd + 1
                    : source.Length;
            }

            body = source.Substring(bodyStart);
            return true;
        }

        var openingBrace = source.IndexOf(
            '{',
            classMatch.Index,
            classMatch.Length);

        if (openingBrace < 0)
        {
            return false;
        }

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
                    body = source.Substring(
                        openingBrace + 1,
                        i - openingBrace - 1);

                    return true;
                }
            }
        }

        return false;
    }

    private static int CountIndent(
        string source,
        int start,
        int end)
    {
        var indent = 0;

        for (var i = start; i < end && i < source.Length; i++)
        {
            indent += source[i] switch
            {
                '\t' => 4,
                ' ' => 1,
                _ => 0
            };
        }

        return indent;
    }

    private static bool HasPrivateConstructor(
        string classBody,
        string className,
        string language)
    {
        if (language == "Python")
        {
            // Python has no private constructor in the C#/Java sense.
            return false;
        }

        var escapedName = Regex.Escape(className);

        var constructorRegex = new Regex(
            $@"\bprivate\s+{escapedName}\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        return constructorRegex.IsMatch(classBody);
    }

    private bool IsNamingConventionSatisfied(string className)
    {
        var suffix = _policy.SingletonSuffix;

        if (string.IsNullOrWhiteSpace(suffix))
        {
            // No configured suffix means no naming requirement.
            return true;
        }

        return className.EndsWith(
            suffix,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectLanguage(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            ".ts" => "TypeScript",
            _ => "Unknown"
        };

    private static double ComputeCompliance(
        double threadSafetySignal,
        double lazyInitializationSignal,
        double instanceDisciplineSignal,
        double namingAdherence)
    {
        var score =
            threadSafetySignal * 0.35 +
            lazyInitializationSignal * 0.25 +
            instanceDisciplineSignal * 0.30 +
            namingAdherence * 0.10;

        return Math.Round(score * 100d, 2);
    }

    private static double AverageMetric(
        IEnumerable<ArchitectureEvaluatorResult> results,
        string metricName)
    {
        var values = results
            .Select(result => GetMetric(result, metricName))
            .ToList();

        return values.Count > 0
            ? values.Average()
            : 0d;
    }

    private static double GetMetric(
        ArchitectureEvaluatorResult result,
        string metricName)
    {
        return result.Metrics.GetValueOrDefault(metricName, 0d);
    }
}