
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Architecture.Evaluators.Analysis;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Collects heuristic structural evidence for the Factory pattern.
///
/// Metrics are scoped to individual factory candidates where possible.
/// These measurements do not establish semantic pattern compliance.
/// Rule interpretation belongs to the RuleEngine.
/// </summary>
public sealed class FactoryPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    private static readonly string[] Extensions =
    [
        ".cs",
        ".java",
        ".ts",
        ".py"
    ];

    private static readonly Regex FactoryClassRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*Factory)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonFactoryClassRegex = new(
        @"^[ \t]*class[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*Factory)\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline);

    private static readonly Regex InterfaceRegex = new(
        @"\binterface\s+(?<name>I[A-Za-z_$][A-Za-z0-9_$]*Factory)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonProtocolRegex = new(
        @"^[ \t]*class[ \t]+(?<name>I[A-Za-z_][A-Za-z0-9_]*Factory)\b[^:]*\bProtocol\b[^:]*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex NewObjectRegex = new(
        @"\bnew\s+[A-Za-z_$][A-Za-z0-9_$.<>?,\[\]]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonConstructionRegex = new(
        @"\b[A-Z][A-Za-z0-9_]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConcreteReturnRegex = new(
        @"\breturn\s+new\s+[A-Za-z_$][A-Za-z0-9_$.<>?,\[\]]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonReturnRegex = new(
        @"\breturn\s+[A-Z][A-Za-z0-9_]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InfrastructureTypeRegex = new(
        @"\b(?:HttpClient|FileStream|SqlConnection|DbContext|Repository)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);


    public FactoryPatternEvaluator(
        ILogger<FactoryPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }

    public override string Name => "FactoryPatternEvaluator";

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

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (Context is null)
            return results;

        if (!_policy.EnforceFactoryPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Factory pattern evaluation disabled.");

            return results;
        }

        var files = ResolveSourceFiles(projectPath, Extensions);
        var readableFiles = new Dictionary<string, SourceFile>(
            StringComparer.OrdinalIgnoreCase);
        var unreadableFiles = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var source = await File.ReadAllTextAsync(file, token);
                var language = ResolveLanguage(file);
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
                    "Unable to read {FilePath} during factory evaluation.",
                    file);
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFiles++;
                _logger.LogWarning(
                    exception,
                    "Access denied to {FilePath} during factory evaluation.",
                    file);
            }
        }

        var interfaceIndex = BuildInterfaceIndex(readableFiles);
        var candidateMetrics = new List<Dictionary<string, double>>();

        foreach (var entry in readableFiles)
        {
            token.ThrowIfCancellationRequested();

            var file = entry.Key;
            var sourceFile = entry.Value;
            var classRegex = sourceFile.Language == "Python"
                ? PythonFactoryClassRegex
                : FactoryClassRegex;

            foreach (Match match in classRegex.Matches(sourceFile.Sanitized))
            {
                token.ThrowIfCancellationRequested();

                var factoryName = match.Groups["name"].Value;

                if (!TryExtractClassBody(
                        sourceFile.Sanitized,
                        match,
                        sourceFile.Language,
                        out var classBody,
                        out var classBodyStart))
                {
                    _logger.LogDebug(
                        "Could not isolate class body for {ClassName} in {FilePath}.",
                        factoryName,
                        file);

                    continue;
                }

                var classHeader = sourceFile.Sanitized.Substring(
                    match.Index,
                    Math.Max(0, classBodyStart - match.Index));
                var expectedInterface = $"I{factoryName}";
                var interfaceEvidence = interfaceIndex.GetValueOrDefault(
                    BuildInterfaceKey(sourceFile.ProjectScopePath, expectedInterface));
                var hasInterface = HasMatchingAbstraction(
                    expectedInterface,
                    classHeader,
                    sourceFile.Language,
                    interfaceEvidence is not null);

                var instantiationMatches = sourceFile.Language == "Python"
                    ? PythonConstructionRegex.Matches(classBody).Cast<Match>().ToArray()
                    : NewObjectRegex.Matches(classBody).Cast<Match>().ToArray();

                var concreteReturnMatches = sourceFile.Language == "Python"
                    ? PythonReturnRegex.Matches(classBody).Cast<Match>().ToArray()
                    : ConcreteReturnRegex.Matches(classBody).Cast<Match>().ToArray();

                var infrastructureMatches = InfrastructureTypeRegex
                    .Matches(classBody)
                    .Cast<Match>()
                    .ToArray();

                var instantiations = instantiationMatches.Length;
                var returnsConcrete = concreteReturnMatches.Length > 0;
                var infrastructureLeaks = infrastructureMatches.Length;

                var instantiationRatio =
                    instantiations /
                    (double)Math.Max(
                        1,
                        _policy.MaxFactoryInstantiations);

                var compliance = ComputeCompliance(
                    hasInterface ? 1 : 0,
                    instantiationRatio,
                    infrastructureLeaks > 0 ? 1 : 0,
                    returnsConcrete ? 1 : 0);

                var metrics = new Dictionary<string, double>
                {
                    ["AbstractionScore"] = hasInterface ? 1 : 0,
                    ["InstantiationCount"] = instantiations,
                    ["InstantiationRatio"] = instantiationRatio,
                    ["InfrastructureLeakCount"] = infrastructureLeaks,
                    ["ReturnsConcrete"] = returnsConcrete ? 1 : 0,
                    ["FactoryComplianceScore"] = compliance
                };

                candidateMetrics.Add(metrics);

                results.Add(CreateResult(
                    projectPath,
                    file,
                    sourceFile,
                    factoryName,
                    expectedInterface,
                    match,
                    classBody,
                    classBodyStart,
                    interfaceEvidence,
                    hasInterface,
                    instantiationMatches,
                    concreteReturnMatches,
                    infrastructureMatches,
                    metrics));
            }
        }

        results.Add(CreateSummary(
            projectPath,
            files.Count,
            readableFiles.Count,
            unreadableFiles,
            candidateMetrics));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Factory evaluation completed: {candidateMetrics.Count} candidates, " +
            $"{readableFiles.Count}/{files.Count} readable files, " +
            $"{unreadableFiles} unreadable files.");

        return results;
    }

    private ArchitectureEvaluatorResult CreateResult(
        string projectPath,
        string file,
        SourceFile sourceFile,
        string factoryName,
        string expectedInterface,
        Match classMatch,
        string classBody,
        int classBodyStart,
        InterfaceEvidence? interfaceEvidence,
        bool hasInterface,
        IReadOnlyCollection<Match> instantiationMatches,
        IReadOnlyCollection<Match> concreteReturnMatches,
        IReadOnlyCollection<Match> infrastructureMatches,
        Dictionary<string, double> metrics)
    {
        var classLine = GetLineNumber(sourceFile.Sanitized, classMatch.Index);
        var classSnippet = GetSourceLine(sourceFile.Source, classLine);
        var framework = ResolveFrameworkForFile(file, projectPath, Context!);
        var result = base.CreateResult(file, "DesignPattern");

        result.Target = factoryName;
        result.Language = sourceFile.Language;
        result.Framework = framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = Context?.Confidence ?? 0;
        result.Metrics = metrics;

        result.Metadata[FilePathMetadataKey] = file;
        result.Metadata[EvaluationScopeMetadataKey] = FileScope;
        result.Metadata["FactoryName"] = factoryName;
        result.Metadata["ExpectedInterface"] = expectedInterface;
        result.Metadata["ExpectedInterfaceFound"] = hasInterface.ToString().ToLowerInvariant();
        result.Metadata["Language"] = sourceFile.Language;
        result.Metadata["Framework"] = framework ?? "Unknown";
        result.Metadata["ProjectPath"] = sourceFile.ProjectScopePath;
        result.Metadata["SemanticAnalysisPerformed"] = "false";
        result.Metadata["StructuralAnalysisHeuristic"] = "true";
        result.Metadata["AnalysisComplete"] = "true";
        result.Metadata["FactoryScoreInterpretation"] =
            "Diagnostic heuristic only. Object construction and concrete return expressions are expected inside factory implementations and are not proof of a pattern violation.";

        var interfaceEvidenceText = hasInterface && interfaceEvidence is not null
            ? $"Matching abstraction '{expectedInterface}' found at " +
              $"{Path.GetRelativePath(sourceFile.ProjectScopePath, interfaceEvidence.FilePath)}:{interfaceEvidence.LineNumber} — {interfaceEvidence.SourceLine}. " +
              $"Factory declaration: {Path.GetRelativePath(sourceFile.ProjectScopePath, file)}:{classLine} — {classSnippet}"
            : $"No matching abstraction '{expectedInterface}' was found in project scope '{sourceFile.ProjectScopePath}'. " +
              $"Factory declaration: {Path.GetRelativePath(sourceFile.ProjectScopePath, file)}:{classLine} — {classSnippet}";

        AddMetricEvidence(
            result,
            "AbstractionScore",
            interfaceEvidenceText,
            classLine,
            classSnippet);

        AddMatchEvidence(
            result,
            "InstantiationCount",
            instantiationMatches,
            sourceFile,
            classBodyStart,
            classLine,
            classSnippet,
            "No object construction calls were detected inside this factory.");
        AddMatchEvidence(
            result,
            "InstantiationRatio",
            instantiationMatches,
            sourceFile,
            classBodyStart,
            classLine,
            classSnippet,
            "No object construction calls were detected inside this factory.");
        AddMatchEvidence(
            result,
            "InfrastructureLeakCount",
            infrastructureMatches,
            sourceFile,
            classBodyStart,
            classLine,
            classSnippet,
            "No configured infrastructure type indicators were detected inside this factory.");
        AddMatchEvidence(
            result,
            "ReturnsConcrete",
            concreteReturnMatches,
            sourceFile,
            classBodyStart,
            classLine,
            classSnippet,
            "No concrete construction return expressions were detected inside this factory.");

        var compositeEvidence =
            $"Factory '{factoryName}' diagnostic score {metrics["FactoryComplianceScore"]:0.##}; " +
            $"abstraction found: {hasInterface}; instantiations: {instantiationMatches.Count}; " +
            $"infrastructure indicators: {infrastructureMatches.Count}; concrete return expressions: {concreteReturnMatches.Count}.";
        AddMetricEvidence(
            result,
            "FactoryComplianceScore",
            $"{compositeEvidence}{Environment.NewLine}{interfaceEvidenceText}",
            classLine,
            classSnippet);

        result.Evidence = string.Join(
            Environment.NewLine,
            new[]
            {
                interfaceEvidenceText,
                compositeEvidence,
                result.Metadata["Evidence.InstantiationCount"],
                result.Metadata["Evidence.InfrastructureLeakCount"],
                result.Metadata["Evidence.ReturnsConcrete"]
            });
        result.LineNumber = classLine;

        return result;
    }

    private static void AddMatchEvidence(
        ArchitectureEvaluatorResult result,
        string metricKey,
        IReadOnlyCollection<Match> matches,
        SourceFile sourceFile,
        int bodyStart,
        int classLine,
        string classSnippet,
        string emptyDescription)
    {
        var evidence = matches.Count == 0
            ? emptyDescription
            : string.Join(
                Environment.NewLine,
                matches.Take(50).Select(match =>
                {
                    var line = GetLineNumber(
                        sourceFile.Sanitized,
                        bodyStart + match.Index);
                    return $"Line {line}: {GetSourceLine(sourceFile.Source, line)}";
                }));

        var first = matches.FirstOrDefault();
        var lineNumber = first is null
            ? classLine
            : GetLineNumber(sourceFile.Sanitized, bodyStart + first.Index);
        var snippet = first is null
            ? classSnippet
            : GetSourceLine(sourceFile.Source, lineNumber);

        AddMetricEvidence(result, metricKey, evidence, lineNumber, snippet);
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
                : InterfaceRegex;

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
        string expectedInterface,
        string classHeader,
        string language,
        bool interfaceDeclarationExists)
    {
        if (!interfaceDeclarationExists)
            return false;

        if (language == "Python")
        {
            return Regex.IsMatch(
                classHeader,
                $@"\b{Regex.Escape(expectedInterface)}\b",
                RegexOptions.CultureInvariant);
        }

        var relationshipKeyword = language is "Java" or "TypeScript"
            ? @"\bimplements\b"
            : @":";

        return Regex.IsMatch(
            classHeader,
            $@"{relationshipKeyword}\s*[^{{]*\b{Regex.Escape(expectedInterface)}\b",
            RegexOptions.CultureInvariant);
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

    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        int discoveredFiles,
        int analyzedFiles,
        int unreadableFiles,
        IReadOnlyCollection<Dictionary<string, double>> candidates)
    {
        var averageCompliance = candidates.Count == 0
            ? 0
            : candidates.Average(candidate =>
                candidate.GetValueOrDefault("FactoryComplianceScore"));

        var summary = new ArchitectureEvaluatorResult(
            Name,
            "FactoryPatternSummary")
        {
            ProjectName = Context?.ProjectName ?? string.Empty,
            Category = "DesignPatternSummary",
            Metrics =
            {
                ["DiscoveredFiles"] = discoveredFiles,
                ["AnalyzedFiles"] = analyzedFiles,
                ["UnreadableFiles"] = unreadableFiles,
                ["FactoryCount"] = candidates.Count,
                ["AverageComplianceScore"] = averageCompliance
            }
        };

        summary.Metadata[EvaluationScopeMetadataKey] = ProjectScope;
        summary.Metadata["Evaluator"] = Name;
        summary.Metadata["SemanticAnalysisPerformed"] = "false";
        summary.Metadata["StructuralAnalysisHeuristic"] = "true";
        var analysisComplete = unreadableFiles == 0 && analyzedFiles == discoveredFiles;
        summary.Metadata["AnalysisComplete"] = analysisComplete.ToString().ToLowerInvariant();
        summary.Metadata["AnalysisStatus"] = analysisComplete
            ? "Completed"
            : "CompletedWithDiagnostics";

        return summary;
    }

    private string ResolveLayer(string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(layer =>
                layer.Files.Contains(
                    file,
                    StringComparer.OrdinalIgnoreCase))
            ?.Name
            ?? "Unknown";
    }

    private static string ResolveLanguage(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".ts" => "TypeScript",
            ".py" => "Python",
            _ => "Unknown"
        };
    }

    private static bool TryExtractClassBody(
        string source,
        Match classMatch,
        string language,
        out string classBody,
        out int bodyStart)
    {
        classBody = string.Empty;
        bodyStart = 0;

        if (language == "Python")
        {
            var headerEnd = source.IndexOf('\n', classMatch.Index);
            if (headerEnd < 0)
                return false;

            bodyStart = headerEnd + 1;
            var classIndentation = CountIndentation(source, classMatch.Index);
            var cursor = bodyStart;

            while (cursor < source.Length)
            {
                var lineEnd = source.IndexOf('\n', cursor);
                if (lineEnd < 0)
                    lineEnd = source.Length;

                var firstNonWhitespace = cursor;
                while (firstNonWhitespace < lineEnd
                    && char.IsWhiteSpace(source[firstNonWhitespace]))
                {
                    firstNonWhitespace++;
                }

                if (firstNonWhitespace < lineEnd
                    && CountIndentation(source, firstNonWhitespace) <= classIndentation)
                {
                    break;
                }

                if (lineEnd == source.Length)
                {
                    cursor = lineEnd;
                    break;
                }

                cursor = lineEnd + 1;
            }

            classBody = source[bodyStart..Math.Min(cursor, source.Length)];
            return !string.IsNullOrWhiteSpace(classBody);
        }

        var openingBrace = source.IndexOf(
            '{',
            classMatch.Index + classMatch.Length);
        if (openingBrace < 0)
            return false;

        var closingBrace = FindMatchingBrace(source, openingBrace);
        if (closingBrace < 0)
            return false;

        bodyStart = openingBrace + 1;
        classBody = source.Substring(
            bodyStart,
            closingBrace - bodyStart);

        return true;
    }

    private static int FindMatchingBrace(string source, int openingBrace)
    {
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return index;
        }

        return -1;
    }

    private static int CountIndentation(
        string source,
        int position)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, position - 1));

        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var indentation = 0;

        for (var i = lineStart; i < source.Length && i < position; i++)
        {
            if (source[i] == ' ')
            {
                indentation++;
            }
            else if (source[i] == '\t')
            {
                indentation += 4;
            }
            else
            {
                break;
            }
        }

        return indentation;
    }

    private static double ComputeCompliance(
        double abstraction,
        double instantiationRatio,
        double leakRatio,
        double concretePenalty)
    {
        var score =
            abstraction * 0.4 +
            (1 - Math.Min(instantiationRatio, 1)) * 0.25 +
            (1 - leakRatio) * 0.15 +
            (1 - concretePenalty) * 0.2;

        return Math.Round(score * 100, 2);
    }

    private sealed record SourceFile(
        string Source,
        string Sanitized,
        string Language,
        string ProjectScopePath);

    private sealed record InterfaceEvidence(
        string FilePath,
        int LineNumber,
        string SourceLine);

}
