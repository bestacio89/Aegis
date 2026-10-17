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
/// Evaluates repository-pattern signals and data-access isolation.
///
/// Detects:
/// - Repository class declarations
/// - Matching repository abstraction declarations
/// - Async-related method signals
/// - Potential persistence or transport dependencies
/// - Repository naming conventions
/// - Approximate class-level method and field counts
///
/// Analysis is lexical and heuristic. It does not prove interface
/// implementation, asynchronous correctness, or architectural violations.
/// </summary>
public sealed class RepositoryPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "RepositoryPatternEvaluator";

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
        "Django",
        "FastAPI",
        "Flask",
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
        (?:\s*<[^>{}]*>)?
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

    private static readonly Regex BraceMethodRegex = new(
        """
        ^[ \t]*
        (?:(?:public|private|protected|static|async|override|virtual|
             final|abstract|synchronized|extern|readonly|default)\s+)*
        [A-Za-z_$][\w.$<>,?\[\]| ]*
        \s+[A-Za-z_$]\w*\s*
        \([^;{}]*\)\s*(?:\{|=>|$)
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex PythonMethodRegex = new(
        @"^[ \t]*(?:async[ \t]+)?def[ \t]+[A-Za-z_]\w*[ \t]*\(",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline);

    private static readonly Regex BraceFieldRegex = new(
        """
        ^[ \t]*
        (?:(?:private|protected|public|static|readonly|final|const|let|var)
          [ \t]+)+
        [^()\r\n;{}]+(?:;|=[^=])
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex PythonFieldRegex = new(
        @"^[ \t]*self\.[A-Za-z_]\w*[ \t]*=",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline);

    private static readonly Regex AsyncSignalRegex = new(
        """
        \basync\b
        |\bTask(?:\s*<|\s*(?:\[\])?\b)
        |\bCompletableFuture\s*<
        |\bCompletionStage\s*<
        |\bFuture\s*<
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex PythonAsyncSignalRegex = new(
        @"\basync[ \t]+def\b|\bawait\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] PersistenceAndTransportIndicators =
    [
        "SqlConnection",
        "DbContext",
        "EntityManager",
        "cursor.execute",
        "HttpClient",
        "axios",
        "fetch(",
        "requests.",
        "urllib.",
        "httpx."
    ];

    public RepositoryPatternEvaluator(
        ILogger<RepositoryPatternEvaluator> logger,
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

        if (!_policy.EnforceRepositoryPattern)
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

        _logger.LogInformation(
            "Running {Evaluator} on {Count} source files.",
            Name,
            files.Count);

        var unreadableFiles = 0;
        var analyzedFiles = 0;

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

                var repositoryName = classMatch.Groups["name"].Value;

                if (!repositoryName.EndsWith(
                    "Repository",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryExtractClassBody(
                    sanitized,
                    classMatch,
                    language,
                    out var classBody))
                {
                    _logger.LogDebug(
                        "{Evaluator} could not isolate class body for {ClassName} in {FilePath}.",
                        Name,
                        repositoryName,
                        file);

                    continue;
                }

                var classHeader = sanitized.Substring(
                    classMatch.Index,
                    classMatch.Length);

                var hasMatchingInterface = HasMatchingAbstraction(
                    repositoryName,
                    classHeader,
                    sanitized,
                    language);

                var methodCount = CountMethods(classBody, language);
                var fieldCount = CountFields(classBody, language);
                var hasAsyncSignal = HasAsyncSignal(classBody, language);

                var leakIndicators = FindLeakIndicators(classBody);
                var hasPersistenceOrTransportSignals =
                    leakIndicators.Count > 0;

                var namingCompliance = string.IsNullOrWhiteSpace(
                    _policy.RepositorySuffix)
                    ? repositoryName.EndsWith(
                        "Repository",
                        StringComparison.Ordinal)
                    : repositoryName.EndsWith(
                        _policy.RepositorySuffix,
                        StringComparison.Ordinal);

                var complexity = ComputeComplexity(
                    methodCount,
                    fieldCount);

                var interfaceScore = hasMatchingInterface ? 1d : 0d;
                var asyncScore = hasAsyncSignal ? 1d : 0d;
                var leakScore = hasPersistenceOrTransportSignals ? 1d : 0d;
                var namingScore = namingCompliance ? 1d : 0d;

                var compliance = ComputeCompliance(
                    interfaceScore,
                    asyncScore,
                    leakScore,
                    namingScore,
                    complexity);

                var result = new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        ArchitectureRuleCategory.DesignPatterns.ToString()
                };

                result.Metrics["InterfaceAdherence"] = interfaceScore;
                result.Metrics["AsyncCompliance"] = asyncScore;
                result.Metrics["LeakRisk"] = leakScore;
                result.Metrics["NamingCompliance"] = namingScore;
                result.Metrics["RepositoryComplexity"] = complexity;
                result.Metrics["RepositoryComplianceScore"] = compliance;
                result.Metrics["MethodCount"] = methodCount;
                result.Metrics["FieldCount"] = fieldCount;
                result.Metrics["PersistenceTransportIndicatorCount"] =
                    leakIndicators.Count;

                result.Metadata[BaseArchitectureEvaluator.FilePathMetadataKey] =
                    file;
                result.Metadata[BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
                    BaseArchitectureEvaluator.FileScope;
                result.Metadata["RepositoryName"] = repositoryName;
                result.Metadata["Language"] = language;
                result.Metadata["HasMatchingAbstraction"] =
                    hasMatchingInterface.ToString();
                result.Metadata["HasAsyncSignal"] =
                    hasAsyncSignal.ToString();
                result.Metadata["HasPersistenceOrTransportSignals"] =
                    hasPersistenceOrTransportSignals.ToString();
                result.Metadata["PersistenceTransportIndicators"] =
                    string.Join(", ", leakIndicators);
                result.Metadata["NamingConventionSatisfied"] =
                    namingCompliance.ToString();
                result.Metadata["ComplexityMeasure"] =
                    "Normalized method-and-field count; not cyclomatic complexity.";
                result.Metadata["SemanticAnalysisPerformed"] =
                    bool.FalseString;
                result.Metadata["InterfaceImplementationVerified"] =
                    bool.FalseString;
                result.Metadata["PolicyEnabled"] =
                    _policy.EnforceRepositoryPattern.ToString();

                results.Add(result);
            }
        }

        // Snapshot file-level findings before adding the project summary.
        var repositoryResults = results.ToList();

        var averageCompliance = AverageMetric(
            repositoryResults,
            "RepositoryComplianceScore");

        var averageComplexity = AverageMetric(
            repositoryResults,
            "RepositoryComplexity");

        var averageAsyncSignalRate = AverageMetric(
            repositoryResults,
            "AsyncCompliance");

        var leakCount = repositoryResults.Count(
            result => GetMetric(result, "LeakRisk") > 0);

        var repositoryCount = repositoryResults.Count;

        var leakRate = repositoryCount > 0
            ? leakCount / (double)repositoryCount
            : 0d;

        var overallHealth = repositoryCount > 0
            ? averageCompliance * (1d - leakRate)
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
        summary.Metrics["RepositoryCount"] = repositoryCount;
        summary.Metrics["AverageComplianceScore"] = averageCompliance;
        summary.Metrics["AverageComplexity"] = averageComplexity;
        summary.Metrics["AverageAsyncSignalRate"] = averageAsyncSignalRate;
        summary.Metrics["LeakCount"] = leakCount;
        summary.Metrics["LeakRate"] = leakRate;
        summary.Metrics["OverallRepositoryHealth"] = overallHealth;

        summary.Metadata[BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
            BaseArchitectureEvaluator.ProjectScope;
        summary.Metadata["Evaluator"] = Name;
        summary.Metadata["PolicyEnabled"] =
            _policy.EnforceRepositoryPattern.ToString();
        summary.Metadata["SemanticAnalysisPerformed"] =
            bool.FalseString;
        summary.Metadata["ComplexityMeasure"] =
            "Normalized method-and-field count; not cyclomatic complexity.";
        summary.Metadata["EmptyRepositorySet"] =
            (repositoryCount == 0).ToString();

        results.Add(summary);

        _logger.LogInformation(
            "{Evaluator} completed: {RepositoryCount} repository candidates, " +
            "{AnalyzedFiles} readable files, {UnreadableFiles} unreadable files.",
            Name,
            repositoryCount,
            analyzedFiles,
            unreadableFiles);

        return results;
    }

    private static string RemoveCommentsAndStrings(string source)
    {
        // Preserve offsets and line breaks so class-body extraction still
        // operates against the original source positions.
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
            var declarationEnd = classMatch.Index + classMatch.Length;

            if (declarationEnd >= source.Length)
            {
                return false;
            }

            var newline = source.IndexOf('\n', declarationEnd);

            if (newline < 0)
            {
                return false;
            }

            var declarationLineStart = source.LastIndexOf(
                '\n',
                Math.Max(0, classMatch.Index - 1));

            declarationLineStart = declarationLineStart < 0
                ? 0
                : declarationLineStart + 1;

            var classIndent = CountIndent(
                source,
                declarationLineStart,
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

                // Blank lines do not terminate the class body.
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

    private static bool HasMatchingAbstraction(
        string repositoryName,
        string classHeader,
        string source,
        string language)
    {
        // This verifies a name/relationship signal only. It does not resolve
        // symbols or prove that the class implements the abstraction.
        var expectedInterfaceName = $"I{repositoryName}";

        var interfaceDeclaration = Regex.IsMatch(
            source,
            $@"\binterface\s+{Regex.Escape(expectedInterfaceName)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!interfaceDeclaration)
        {
            return false;
        }

        var relationshipKeyword = language switch
        {
            "Java" => @"\bimplements\b",
            "TypeScript" => @"\bimplements\b",
            "Python" => string.Empty,
            _ => @":"
        };

        if (language == "Python")
        {
            // Python uses base classes rather than an implements keyword.
            return Regex.IsMatch(
                classHeader,
                $@"\b{Regex.Escape(expectedInterfaceName)}\b",
                RegexOptions.CultureInvariant);
        }

        return Regex.IsMatch(
            classHeader,
            $@"{relationshipKeyword}\s*[^{{]*\b{Regex.Escape(expectedInterfaceName)}\b",
            RegexOptions.CultureInvariant);
    }

    private static int CountMethods(string classBody, string language)
    {
        var regex = language == "Python"
            ? PythonMethodRegex
            : BraceMethodRegex;

        return regex.Matches(classBody).Count;
    }

    private static int CountFields(string classBody, string language)
    {
        var regex = language == "Python"
            ? PythonFieldRegex
            : BraceFieldRegex;

        return regex.Matches(classBody).Count;
    }

    private static bool HasAsyncSignal(
        string classBody,
        string language)
    {
        return language == "Python"
            ? PythonAsyncSignalRegex.IsMatch(classBody)
            : AsyncSignalRegex.IsMatch(classBody);
    }

    private static List<string> FindLeakIndicators(string classBody)
    {
        var found = new List<string>();

        foreach (var indicator in PersistenceAndTransportIndicators)
        {
            if (classBody.Contains(
                indicator,
                StringComparison.OrdinalIgnoreCase))
            {
                found.Add(indicator);
            }
        }

        return found;
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

    private static double ComputeComplexity(
        int methodCount,
        int fieldCount)
    {
        var normalized = Math.Min(
            1d,
            methodCount / 30d + fieldCount / 50d);

        return Math.Round(normalized, 2);
    }

    private static double ComputeCompliance(
        double interfaceAdherence,
        double asyncSignal,
        double persistenceTransportSignal,
        double namingCompliance,
        double complexity)
    {
        var score =
            interfaceAdherence * 0.25 +
            asyncSignal * 0.15 +
            (1d - persistenceTransportSignal) * 0.35 +
            namingCompliance * 0.10 +
            (1d - complexity) * 0.15;

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