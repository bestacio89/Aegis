using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Architecture.Evaluators.Analysis;
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
        "LanguageAgnostic"
    ];

    private static readonly Regex InterfaceDeclarationRegex = new(
        @"\binterface\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonProtocolRegex = new(
        @"^[ \t]*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^)]*\bProtocol\b[^)]*\)\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

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

    private static readonly string[] ExternalTransportIndicators =
    [
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

        var readableFiles = new Dictionary<string, SourceFile>(
            StringComparer.OrdinalIgnoreCase);
        var unreadableFiles = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var source = await File.ReadAllTextAsync(file, token);
                var language = DetectLanguage(file);
                readableFiles[file] = new SourceFile(
                    source,
                    SourceCodeMasker.Mask(source),
                    language,
                    ResolveProjectScopePath(file, projectPath));
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
                    "{Evaluator} could not read {FilePath}.",
                    Name,
                    file);
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFiles++;
                _logger.LogWarning(
                    exception,
                    "{Evaluator} has no access to {FilePath}.",
                    Name,
                    file);
            }
        }

        var interfaceIndex = BuildInterfaceIndex(readableFiles);
        var analyzedFiles = readableFiles.Count;

        foreach (var entry in readableFiles)
        {
            token.ThrowIfCancellationRequested();

            var file = entry.Key;
            var sourceFile = entry.Value;
            var classRegex = sourceFile.Language == "Python"
                ? PythonClassRegex
                : BraceClassRegex;

            foreach (Match classMatch in classRegex.Matches(sourceFile.Sanitized))
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
                        sourceFile.Sanitized,
                        classMatch,
                        sourceFile.Language,
                        out var classBody,
                        out var classBodyStart))
                {
                    _logger.LogDebug(
                        "{Evaluator} could not isolate class body for {ClassName} in {FilePath}.",
                        Name,
                        repositoryName,
                        file);
                    continue;
                }

                var classHeader = sourceFile.Sanitized.Substring(
                    classMatch.Index,
                    classMatch.Length);
                var expectedInterfaceName = $"I{repositoryName}";
                var interfaceKey = BuildInterfaceKey(
                    sourceFile.ProjectScopePath,
                    expectedInterfaceName);
                var matchingInterface = interfaceIndex.GetValueOrDefault(interfaceKey);

                var hasMatchingInterface = HasMatchingAbstraction(
                    expectedInterfaceName,
                    classHeader,
                    sourceFile.Language,
                    matchingInterface is not null);

                var methodCount = CountMethods(classBody, sourceFile.Language);
                var fieldCount = CountFields(classBody, sourceFile.Language);
                var asyncMatches = FindAsyncMatches(classBody, sourceFile.Language);
                var hasAsyncSignal = asyncMatches.Count > 0;

                var leakMatches = FindLeakMatches(classBody);
                var leakIndicators = leakMatches
                    .Select(match => match.Indicator)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var hasPersistenceOrTransportSignals = leakMatches.Count > 0;

                var namingCompliance = string.IsNullOrWhiteSpace(
                    _policy.RepositorySuffix)
                    ? repositoryName.EndsWith(
                        "Repository",
                        StringComparison.Ordinal)
                    : repositoryName.EndsWith(
                        _policy.RepositorySuffix,
                        StringComparison.Ordinal);

                var complexity = ComputeComplexity(methodCount, fieldCount);
                var interfaceScore = hasMatchingInterface ? 1d : 0d;
                var asyncScore = hasAsyncSignal ? 1d : 0d;
                var leakScore = hasPersistenceOrTransportSignals ? 1d : 0d;
                var namingScore = namingCompliance ? 1d : 0d;
                var compliance = ComputeCompliance(
                    interfaceScore,
                    leakScore,
                    namingScore,
                    complexity);

                var metrics = new Dictionary<string, double>
                {
                    ["InterfaceAdherence"] = interfaceScore,
                    ["AsyncCompliance"] = asyncScore,
                    ["LeakRisk"] = leakScore,
                    ["NamingCompliance"] = namingScore,
                    ["RepositoryComplexity"] = complexity,
                    ["RepositoryComplianceScore"] = compliance,
                    ["MethodCount"] = methodCount,
                    ["FieldCount"] = fieldCount,
                    ["PersistenceTransportIndicatorCount"] = leakMatches.Count
                };

                results.Add(CreateRepositoryResult(
                    projectPath,
                    file,
                    sourceFile,
                    repositoryName,
                    classMatch,
                    classBody,
                    classBodyStart,
                    metrics,
                    hasMatchingInterface,
                    expectedInterfaceName,
                    matchingInterface,
                    hasAsyncSignal,
                    asyncMatches,
                    leakIndicators,
                    leakMatches));
            }
        }

        var repositoryResults = results.ToArray();
        var averageCompliance = AverageMetric(repositoryResults, "RepositoryComplianceScore");
        var averageComplexity = AverageMetric(repositoryResults, "RepositoryComplexity");
        var averageAsyncSignalRate = AverageMetric(repositoryResults, "AsyncCompliance");
        var leakCount = repositoryResults.Count(result => GetMetric(result, "LeakRisk") > 0);
        var repositoryCount = repositoryResults.Length;
        var leakRate = repositoryCount > 0 ? leakCount / (double)repositoryCount : 0d;
        var overallHealth = repositoryCount > 0
            ? averageCompliance * (1d - leakRate)
            : 0d;

        var summary = new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = ArchitectureRuleCategory.DesignPatterns.ToString(),
            ProjectName = Context?.ProjectName
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
        summary.Metadata["PolicyEnabled"] = _policy.EnforceRepositoryPattern.ToString();
        summary.Metadata["SemanticAnalysisPerformed"] = bool.FalseString;
        summary.Metadata["ComplexityMeasure"] =
            "Normalized method-and-field count; not cyclomatic complexity.";
        summary.Metadata["EmptyRepositorySet"] = (repositoryCount == 0).ToString();
        var analysisComplete = unreadableFiles == 0 && analyzedFiles == files.Count;
        summary.Metadata["AnalysisComplete"] = analysisComplete.ToString();
        summary.Metadata["AnalysisStatus"] = analysisComplete
            ? "Completed"
            : "CompletedWithDiagnostics";

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

    private ArchitectureEvaluatorResult CreateRepositoryResult(
        string projectPath,
        string file,
        SourceFile sourceFile,
        string repositoryName,
        Match classMatch,
        string classBody,
        int classBodyStart,
        Dictionary<string, double> metrics,
        bool hasMatchingInterface,
        string expectedInterfaceName,
        InterfaceEvidence? matchingInterface,
        bool hasAsyncSignal,
        IReadOnlyCollection<Match> asyncMatches,
        IReadOnlyCollection<string> leakIndicators,
        IReadOnlyCollection<LeakMatch> leakMatches)
    {
        var classLine = GetLineNumber(sourceFile.Sanitized, classMatch.Index);
        var classSnippet = GetSourceLine(sourceFile.Source, classLine);
        var relativePath = Path.GetRelativePath(sourceFile.ProjectScopePath, file);

        var result = CreateResult(file, ArchitectureRuleCategory.DesignPatterns.ToString());
        result.Target = repositoryName;
        result.Language = sourceFile.Language;
        result.Framework = ResolveFrameworkForFile(file, projectPath, Context!);
        result.Layer = ResolveLayer(file);
        result.Metrics = metrics;

        result.Metadata["FilePath"] = file;
        result.Metadata["RepositoryName"] = repositoryName;
        result.Metadata["Language"] = sourceFile.Language;
        result.Metadata["Framework"] = result.Framework ?? "Unknown";
        result.Metadata["HasMatchingAbstraction"] = hasMatchingInterface.ToString();
        result.Metadata["HasAsyncSignal"] = hasAsyncSignal.ToString();
        result.Metadata["HasPersistenceOrTransportSignals"] = (leakMatches.Count > 0).ToString();
        result.Metadata["ExternalTransportIndicators"] = string.Join(", ", leakIndicators);
        result.Metadata["ExternalTransportInterpretation"] =
            "Database access is expected inside repository implementations; only external transport clients contribute to leak risk.";
        result.Metadata["AsyncSignalInterpretation"] =
            "Informational only. Synchronous repository methods are not automatically non-compliant.";
        result.Metadata["NamingConventionSatisfied"] =
            metrics["NamingCompliance"] > 0 ? bool.TrueString : bool.FalseString;
        result.Metadata["ComplexityMeasure"] =
            "Normalized method-and-field count; not cyclomatic complexity.";
        result.Metadata["SemanticAnalysisPerformed"] = bool.FalseString;
        result.Metadata["InterfaceImplementationVerified"] = bool.FalseString;
        result.Metadata["PolicyEnabled"] = _policy.EnforceRepositoryPattern.ToString();
        result.Metadata["EvaluationScope"] = BaseArchitectureEvaluator.FileScope;
        result.Metadata["AnalysisComplete"] = bool.TrueString;

        var interfaceEvidence = hasMatchingInterface && matchingInterface is not null
            ? $"Matching abstraction '{expectedInterfaceName}' found at " +
              $"{Path.GetRelativePath(sourceFile.ProjectScopePath, matchingInterface.FilePath)}:" +
              $"{matchingInterface.LineNumber} — {matchingInterface.SourceLine}. Repository declaration: {relativePath}:{classLine} — {classSnippet}"
            : $"No matching abstraction '{expectedInterfaceName}' was found in project scope '{sourceFile.ProjectScopePath}'. " +
              $"Repository declaration: {relativePath}:{classLine} — {classSnippet}";

        AddMetricEvidence(
            result,
            "InterfaceAdherence",
            interfaceEvidence,
            classLine,
            classSnippet);

        var asyncEvidence = asyncMatches.Count == 0
            ? "No asynchronous API signals were detected in this repository class."
            : string.Join(
                Environment.NewLine,
                asyncMatches.Take(50).Select(match =>
                {
                    var line = GetLineNumber(sourceFile.Sanitized, classBodyStart + match.Index);
                    return $"Line {line}: {GetSourceLine(sourceFile.Source, line)}";
                }));
        AddMetricEvidence(
            result,
            "AsyncCompliance",
            asyncEvidence,
            asyncMatches.Count > 0
                ? GetLineNumber(sourceFile.Sanitized, classBodyStart + asyncMatches.First().Index)
                : classLine,
            asyncMatches.Count > 0
                ? GetSourceLine(
                    sourceFile.Source,
                    GetLineNumber(sourceFile.Sanitized, classBodyStart + asyncMatches[0].Index))
                : classSnippet);

        var leakEvidence = leakMatches.Count == 0
            ? "No recognized persistence or transport indicators were detected in this repository class."
            : string.Join(
                Environment.NewLine,
                leakMatches.Take(50).Select(match =>
                {
                    var line = GetLineNumber(sourceFile.Sanitized, classBodyStart + match.Match.Index);
                    return $"Line {line}: {match.Indicator} — {GetSourceLine(sourceFile.Source, line)}";
                }));
        AddMetricEvidence(
            result,
            "LeakRisk",
            leakEvidence,
            leakMatches.Count > 0
                ? GetLineNumber(sourceFile.Sanitized, classBodyStart + leakMatches.First().Match.Index)
                : classLine,
            leakMatches.Count > 0
                ? GetSourceLine(
                    sourceFile.Source,
                    GetLineNumber(sourceFile.Sanitized, classBodyStart + leakMatches[0].Match.Index))
                : classSnippet);

        AddMetricEvidence(
            result,
            "NamingCompliance",
            $"Repository type '{repositoryName}' evaluated against suffix '{_policy.RepositorySuffix}'.",
            classLine,
            classSnippet);

        var complexityEvidence =
            $"Repository '{repositoryName}' has {metrics["MethodCount"]} methods and {metrics["FieldCount"]} fields; " +
            $"normalized repository complexity is {metrics["RepositoryComplexity"]:0.##}.";
        AddMetricEvidence(
            result,
            "RepositoryComplexity",
            complexityEvidence,
            classLine,
            classSnippet);

        var complianceEvidence =
            $"Repository compliance score {metrics["RepositoryComplianceScore"]:0.##}/100; " +
            $"interface adherence {metrics["InterfaceAdherence"]:0.##}; async signal {metrics["AsyncCompliance"]:0.##}; " +
            $"persistence/transport signal {metrics["LeakRisk"]:0.##}; naming {metrics["NamingCompliance"]:0.##}; " +
            $"complexity {metrics["RepositoryComplexity"]:0.##}.";
        AddMetricEvidence(
            result,
            "RepositoryComplianceScore",
            complianceEvidence,
            classLine,
            classSnippet);

        result.Evidence = string.Join(
            Environment.NewLine,
            new[]
            {
                interfaceEvidence,
                asyncEvidence,
                leakEvidence,
                complexityEvidence,
                complianceEvidence
            });

        result.LineNumber = classLine;
        return result;
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

    private static Dictionary<string, InterfaceEvidence> BuildInterfaceIndex(
        IReadOnlyDictionary<string, SourceFile> readableFiles)
    {
        var index = new Dictionary<string, InterfaceEvidence>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in readableFiles)
        {
            var sourceFile = entry.Value;
            var regex = sourceFile.Language == "Python"
                ? PythonProtocolRegex
                : InterfaceDeclarationRegex;

            foreach (Match match in regex.Matches(sourceFile.Sanitized))
            {
                var interfaceName = match.Groups["name"].Value;
                var line = GetLineNumber(sourceFile.Sanitized, match.Index);
                var key = BuildInterfaceKey(sourceFile.ProjectScopePath, interfaceName);

                index.TryAdd(
                    key,
                    new InterfaceEvidence(entry.Key, line, GetSourceLine(sourceFile.Source, line)));
            }
        }

        return index;
    }

    private static string BuildInterfaceKey(string projectPath, string interfaceName)
    {
        var normalizedProject = Path.GetFullPath(projectPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();

        return $"{normalizedProject}|{interfaceName.ToUpperInvariant()}";
    }

    private string ResolveProjectScopePath(string file, string projectPath)
    {
        var owningModule = Context?.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .OrderByDescending(module => module.Path.Length)
            .FirstOrDefault(module => IsSameOrDescendantPath(file, module.Path));

        return owningModule?.Path ?? Path.GetFullPath(projectPath);
    }

    private static bool HasMatchingAbstraction(
        string expectedInterfaceName,
        string classHeader,
        string language,
        bool interfaceDeclarationExists)
    {
        if (!interfaceDeclarationExists)
            return false;

        var relationshipKeyword = language switch
        {
            "Java" => @"implements",
            "TypeScript" => @"implements",
            "Python" => string.Empty,
            _ => @":"
        };

        return language == "Python"
            ? Regex.IsMatch(
                classHeader,
                $@"{Regex.Escape(expectedInterfaceName)}",
                RegexOptions.CultureInvariant)
            : Regex.IsMatch(
                classHeader,
                $@"{relationshipKeyword}s*[^{{]*{Regex.Escape(expectedInterfaceName)}",
                RegexOptions.CultureInvariant);
    }

    private static IReadOnlyCollection<Match> FindAsyncMatches(
        string classBody,
        string language)
    {
        var regex = language == "Python"
            ? PythonAsyncSignalRegex
            : AsyncSignalRegex;

        return regex.Matches(classBody).Cast<Match>().ToArray();
    }

    private static IReadOnlyCollection<LeakMatch> FindLeakMatches(string classBody)
    {
        var matches = new List<LeakMatch>();
        foreach (var indicator in ExternalTransportIndicators)
        {
            matches.AddRange(
                Regex.Matches(
                        classBody,
                        Regex.Escape(indicator),
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    .Cast<Match>()
                    .Select(match => new LeakMatch(indicator, match)));
        }

        return matches.OrderBy(match => match.Match.Index).ToArray();
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

    private static bool TryExtractClassBody(
        string source,
        Match classMatch,
        string language,
        out string body,
        out int bodyStart)
    {
        body = string.Empty;
        bodyStart = 0;

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

            bodyStart = newline + 1;
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
                    bodyStart = openingBrace + 1;
                    body = source.Substring(
                        bodyStart,
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

    private static string DetectLanguage(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            ".ts" => "TypeScript",
            _ => "Unknown"
        };

    private sealed record SourceFile(
        string Source,
        string Sanitized,
        string Language,
        string ProjectScopePath);

    private sealed record InterfaceEvidence(
        string FilePath,
        int LineNumber,
        string SourceLine);

    private sealed record LeakMatch(
        string Indicator,
        Match Match);

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
        double persistenceTransportSignal,
        double namingCompliance,
        double complexity)
    {
        var score =
            interfaceAdherence * 0.30 +
            (1d - persistenceTransportSignal) * 0.35 +
            namingCompliance * 0.15 +
            (1d - complexity) * 0.20;

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