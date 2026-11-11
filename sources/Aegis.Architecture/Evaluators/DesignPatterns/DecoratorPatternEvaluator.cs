
using Aegis.Architecture.Evaluators;
using Aegis.Architecture.Evaluators.Analysis;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

public sealed class DecoratorPatternEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    private static readonly string[] SourceExtensions =
    [
        ".cs",
        ".java",
        ".ts"
    ];

    private static readonly string[] ComponentRoleSuffixes =
    [
        "Service",
        "Repository",
        "Handler",
        "Controller",
        "Component",
        "Provider",
        "Client",
        "Gateway",
        "Factory",
        "Strategy",
        "Validator",
        "Processor",
        "Adapter",
        "Manager"
    ];

    private static readonly Regex ClassDeclarationRegex = new(
        @"\bclass\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*(?<header>[^{;]*)\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AbstractionReferenceRegex = new(
        @"\b(?:implements|extends)\s+[A-Za-z_$][A-Za-z0-9_$.<>?,\s\[\]]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CSharpAbstractionReferenceRegex = new(
        @":\s*[^{}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MemberDeclarationRegex = new(
        @"\b(?:(?:private|protected|public|readonly|static|final|const|let|var)\s+)*" +
        @"(?<type>[A-Za-z_$][A-Za-z0-9_$.<>?,\s\[\]]*)\s+" +
        @"(?<name>_?[A-Za-z_$][A-Za-z0-9_$]*)\s*(?:[;=,])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConstructorKeywordRegex = new(
        @"\bconstructor\s*\([^)]*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CSharpConstructorRegex = new(
        @"\b(?:public|protected|internal)\s+[A-Za-z_$][A-Za-z0-9_$]*\s*\([^)]*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DelegationRegex = new(
        @"\b(?<member>_?[A-Za-z_$][A-Za-z0-9_$]*)\s*\.\s*" +
        @"(?<method>[A-Za-z_$][A-Za-z0-9_$]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public DecoratorPatternEvaluator(
        ILogger<DecoratorPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }

    public override string Name => "DecoratorPatternEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript"
    ];

    public override string[] SupportedFrameworks =>
    [
        "LanguageAgnostic"
    ];

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        if (!_policy.EnforceDecoratorPattern)
        {
            _logger.LogDebug(
                "{Evaluator} skipped because decorator enforcement is disabled.",
                Name);

            return [];
        }

        var results = new List<ArchitectureEvaluatorResult>();

        var sourceFiles = ResolveSourceFiles(projectPath, SourceExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var analyzedFiles = 0;
        var unreadableFiles = 0;
        var candidateCount = 0;
        var structurallySupportedCount = 0;
        var namingApplicableCount = 0;
        var namingViolationCount = 0;

        foreach (var file in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string source;

            try
            {
                source = await File.ReadAllTextAsync(file, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException exception)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    exception,
                    "{Evaluator} could not read source file {FilePath}.",
                    Name,
                    file);

                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    exception,
                    "{Evaluator} has no permission to read source file {FilePath}.",
                    Name,
                    file);

                continue;
            }

            analyzedFiles++;

            var language = ResolveLanguage(file);
            var sanitizedSource = SourceCodeMasker.Mask(source);

            foreach (Match classMatch in ClassDeclarationRegex.Matches(sanitizedSource))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var className = classMatch.Groups["name"].Value;
                var header = classMatch.Groups["header"].Value;

                // The declaration match includes the opening brace.
                var openingBraceIndex = classMatch.Index + classMatch.Length - 1;
                var bodyStart = openingBraceIndex + 1;
                var bodyEnd = FindMatchingBrace(
                    sanitizedSource,
                    openingBraceIndex);

                if (bodyEnd < bodyStart)
                {
                    continue;
                }

                var body = sanitizedSource[bodyStart..bodyEnd];

                var analysis = AnalyzeClass(
                    className,
                    header,
                    body,
                    language);

                if (!analysis.IsDecoratorCandidate)
                {
                    continue;
                }

                candidateCount++;

                if (analysis.HasAbstractionReference
                    && analysis.HasWrappedComponent
                    && analysis.HasDelegationCalls)
                {
                    structurallySupportedCount++;
                }

                // Structural heuristics can identify wrappers that are not
                // intended decorators. Enforce the naming convention only when
                // the class name explicitly signals decorator intent.
                if (analysis.HasDecoratorSuffix)
                {
                    namingApplicableCount++;

                    if (!analysis.HasValidDecoratorName)
                    {
                        namingViolationCount++;
                    }
                }

                var result = CreateClassResult(
                    projectPath,
                    file,
                    className,
                    language,
                    source,
                    sanitizedSource,
                    classMatch,
                    bodyStart,
                    body,
                    analysis);

                results.Add(result);
            }
        }

        var summaryResult = CreateSummary(
            projectPath,
            sourceFiles.Length,
            analyzedFiles,
            unreadableFiles,
            candidateCount,
            structurallySupportedCount,
            namingApplicableCount,
            namingViolationCount);

        results.Add(summaryResult);

        _logger.LogInformation(
            "{Evaluator} analyzed {AnalyzedFiles}/{SourceFiles} files; " +
            "found {Candidates} decorator candidates, {NamingViolations} naming violations, " +
            "and {StructuralMatches} structurally supported candidates.",
            Name,
            analyzedFiles,
            sourceFiles.Length,
            candidateCount,
            namingViolationCount,
            structurallySupportedCount);

        return results;
    }

    private static DecoratorAnalysis AnalyzeClass(
        string className,
        string header,
        string body,
        string language)
    {
        var members = FindMembers(body);

        var componentMembers = members
            .Where(member =>
                IsComponentRole(member.Type)
                || IsComponentRole(member.Name))
            .ToArray();

        var hasAbstractionReference = HasAbstractionReference(
            header,
            language);

        var hasConstructorInjection =
            ConstructorKeywordRegex.IsMatch(body)
            || CSharpConstructorRegex.IsMatch(body);

        var delegatedMembers = FindDelegatedMembers(body);

        // A component is considered wrapped only when a member that looks
        // like a component is also used as a delegation receiver.
        var wrappedMembers = componentMembers
            .Where(member =>
                delegatedMembers.Contains(member.Name)
                || delegatedMembers.Contains(member.Name.TrimStart('_')))
            .ToArray();

        var hasWrappedComponent = wrappedMembers.Length > 0;
        var hasDelegationCalls = hasWrappedComponent;

        var hasDecoratorSuffix = className.EndsWith(
            "Decorator",
            StringComparison.Ordinal);

        var wrappedRole = GetWrappedRole(className);

        var hasValidDecoratorName =
            hasDecoratorSuffix
            && wrappedRole is not null;

        // The decorator contract is the shared abstraction plus an actual
        // wrapped-component delegation. Constructor injection is preferred,
        // but property/setter injection is also valid and remains advisory.
        var structuralSignals = 0;

        if (hasAbstractionReference)
            structuralSignals++;

        if (hasWrappedComponent)
            structuralSignals++;

        var isDecoratorCandidate =
            hasDecoratorSuffix
            || (structuralSignals == 2 && hasWrappedComponent);

        var evidenceCount = structuralSignals;
        var structuralScore = evidenceCount / 2.0 * 100.0;

        return new DecoratorAnalysis(
            IsDecoratorCandidate: isDecoratorCandidate,
            HasDecoratorSuffix: hasDecoratorSuffix,
            HasValidDecoratorName: hasValidDecoratorName,
            WrappedRole: wrappedRole ?? string.Empty,
            HasAbstractionReference: hasAbstractionReference,
            HasWrappedComponent: hasWrappedComponent,
            HasConstructorInjection: hasConstructorInjection,
            HasDelegationCalls: hasDelegationCalls,
            ComponentMemberCount: componentMembers.Length,
            DelegatedMemberCount: wrappedMembers.Length,
            StructuralEvidenceCount: evidenceCount,
            StructuralComplianceScore: structuralScore);
    }

    private static bool HasAbstractionReference(
        string header,
        string language)
    {
        return language.Equals("C#", StringComparison.OrdinalIgnoreCase)
            ? CSharpAbstractionReferenceRegex.IsMatch(header)
            : AbstractionReferenceRegex.IsMatch(header);
    }

    private static List<ComponentMember> FindMembers(string body)
    {
        var members = new List<ComponentMember>();

        foreach (Match match in MemberDeclarationRegex.Matches(body))
        {
            var type = NormalizeTypeName(match.Groups["type"].Value);
            var name = match.Groups["name"].Value;

            if (string.IsNullOrWhiteSpace(type)
                || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            members.Add(new ComponentMember(type, name));
        }

        return members;
    }

    private static HashSet<string> FindDelegatedMembers(string body)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in DelegationRegex.Matches(body))
        {
            members.Add(match.Groups["member"].Value);
        }

        return members;
    }

    private static bool IsComponentRole(string value)
    {
        var normalized = NormalizeTypeName(value);

        return ComponentRoleSuffixes.Any(suffix =>
            normalized.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static string? GetWrappedRole(string className)
    {
        if (!className.EndsWith("Decorator", StringComparison.Ordinal))
        {
            return null;
        }

        var wrappedName = className[..^"Decorator".Length];

        return ComponentRoleSuffixes.FirstOrDefault(suffix =>
            wrappedName.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static string NormalizeTypeName(string value)
    {
        var normalized = value.Trim();

        var genericStart = normalized.IndexOf('<');

        if (genericStart >= 0)
        {
            normalized = normalized[..genericStart];
        }

        var lastDot = normalized.LastIndexOf('.');

        if (lastDot >= 0 && lastDot < normalized.Length - 1)
        {
            normalized = normalized[(lastDot + 1)..];
        }

        return normalized.TrimEnd('?', '[', ']').Trim();
    }

    private ArchitectureEvaluatorResult CreateClassResult(
        string projectPath,
        string file,
        string className,
        string language,
        string source,
        string sanitizedSource,
        Match classMatch,
        int bodyStart,
        string body,
        DecoratorAnalysis analysis)
    {
        var framework = ResolveFrameworkForFile(file, projectPath, Context!);
        var result = CreateResult(file, ArchitectureRuleCategory.DesignPatterns.ToString());

        result.Target = className;
        result.Language = language;
        result.Framework = framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = Context?.Confidence ?? 0;

        result.Metrics["HasAbstractionReference"] =
            analysis.HasAbstractionReference ? 1 : 0;

        result.Metrics["HasWrappedComponent"] =
            analysis.HasWrappedComponent ? 1 : 0;

        result.Metrics["HasConstructorInjection"] =
            analysis.HasConstructorInjection ? 1 : 0;

        result.Metrics["HasDelegationCalls"] =
            analysis.HasDelegationCalls ? 1 : 0;

        result.Metrics["HasDecoratorSuffix"] =
            analysis.HasDecoratorSuffix ? 1 : 0;

        if (analysis.HasDecoratorSuffix)
        {
            result.Metrics["HasValidDecoratorName"] =
                analysis.HasValidDecoratorName ? 1 : 0;

            // This metric is absent when naming compliance is not applicable.
            result.Metrics["DecoratorNamingCompliance"] =
                analysis.HasValidDecoratorName ? 100 : 0;
        }

        result.Metrics["ComponentMemberCount"] =
            analysis.ComponentMemberCount;

        result.Metrics["DelegatedMemberCount"] =
            analysis.DelegatedMemberCount;

        result.Metrics["StructuralEvidenceCount"] =
            analysis.StructuralEvidenceCount;

        result.Metrics["StructuralComplianceScore"] =
            analysis.StructuralComplianceScore;

        // Delegation compliance is independent of naming intent. Keep it
        // separate from the legacy composite score, which includes naming.
        result.Metrics["DecoratorDelegationComplianceScore"] =
            analysis.StructuralComplianceScore;

        // The legacy composite score is emitted only when naming intent makes
        // that dimension applicable; the rule engine uses the delegation-only
        // metric for AEG-DES-DEC001.
        if (analysis.HasDecoratorSuffix)
        {
            result.Metrics["DecoratorComplianceScore"] =
                analysis.HasValidDecoratorName
                    ? analysis.StructuralComplianceScore
                    : analysis.StructuralComplianceScore * 0.75;
        }

        result.Metadata[BaseArchitectureEvaluator.FilePathMetadataKey] = file;
        result.Metadata[BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
            BaseArchitectureEvaluator.FileScope;

        result.Metadata["ClassName"] = className;
        result.Metadata["Language"] = language;
        result.Metadata["DecoratorCandidate"] =
            analysis.IsDecoratorCandidate.ToString();

        result.Metadata["NamingConventionCompliant"] = analysis.HasDecoratorSuffix
            ? analysis.HasValidDecoratorName.ToString()
            : "NotApplicable";

        result.Metadata["WrappedComponentRole"] = analysis.WrappedRole;
        result.Metadata["SemanticAnalysisPerformed"] = "false";
        result.Metadata["NamingConventionVerified"] =
            analysis.HasDecoratorSuffix.ToString();
        result.Metadata["StructuralAnalysisHeuristic"] = "true";
        result.Metadata["ConstructorInjectionIsAdvisory"] =
            "A decorator may receive its wrapped component through constructor, property, or setter injection.";
        result.Metadata["Layer"] = result.Layer ?? "Unclassified";
        result.Metadata["Framework"] = framework ?? "Unknown";
        result.Metadata["AnalysisComplete"] = bool.TrueString;

        var classLine = GetLineNumber(sanitizedSource, classMatch.Index);
        var classSnippet = GetSourceLine(source, classLine);
        var memberMatches = MemberDeclarationRegex.Matches(body)
            .Cast<Match>()
            .ToArray();
        var componentMatches = memberMatches
            .Where(match =>
                IsComponentRole(match.Groups["type"].Value)
                || IsComponentRole(match.Groups["name"].Value))
            .ToArray();
        var componentNames = componentMatches
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var delegationMatches = DelegationRegex.Matches(body)
            .Cast<Match>()
            .Where(match => componentNames.Contains(match.Groups["member"].Value))
            .ToArray();
        var constructorMatches = ConstructorKeywordRegex.Matches(body)
            .Cast<Match>()
            .Concat(CSharpConstructorRegex.Matches(body).Cast<Match>())
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .ToArray();

        var abstractionEvidence =
            $"Class declaration at line {classLine}: {classSnippet}. " +
            $"Abstraction reference detected: {analysis.HasAbstractionReference}.";
        var componentEvidence = BuildEvidence(
            componentMatches,
            source,
            sanitizedSource,
            bodyStart,
            "No component-role member declarations were detected.");
        var constructorEvidence = BuildEvidence(
            constructorMatches,
            source,
            sanitizedSource,
            bodyStart,
            "No constructor injection signature was detected.");
        var delegationEvidence = BuildEvidence(
            delegationMatches,
            source,
            sanitizedSource,
            bodyStart,
            "No calls through a recognized wrapped component member were detected.");

        var scoreEvidence =
            $"Decorator '{className}' has {analysis.StructuralEvidenceCount}/3 structural signals; " +
            $"delegation compliance score {analysis.StructuralComplianceScore:0.##}/100." +
            $"{Environment.NewLine}{abstractionEvidence}" +
            $"{Environment.NewLine}{componentEvidence}" +
            $"{Environment.NewLine}{constructorEvidence}" +
            $"{Environment.NewLine}{delegationEvidence}";

        AddMetricEvidence(
            result,
            "DecoratorDelegationComplianceScore",
            scoreEvidence,
            delegationMatches.Length > 0
                ? GetLineNumber(sanitizedSource, bodyStart + delegationMatches[0].Index)
                : classLine,
            delegationMatches.Length > 0
                ? GetSourceLine(
                    source,
                    GetLineNumber(sanitizedSource, bodyStart + delegationMatches[0].Index))
                : classSnippet);

        AddMetricEvidence(
            result,
            "StructuralComplianceScore",
            scoreEvidence,
            classLine,
            classSnippet);

        if (analysis.HasDecoratorSuffix)
        {
            AddMetricEvidence(
                result,
                "DecoratorNamingCompliance",
                $"Decorator name '{className}' maps to wrapped role '{analysis.WrappedRole}'.",
                classLine,
                classSnippet);
        }

        result.Evidence = scoreEvidence;
        result.LineNumber = classLine;

        return result;
    }

    private static string BuildEvidence(
        IReadOnlyCollection<Match> matches,
        string source,
        string sanitizedSource,
        int bodyStart,
        string emptyEvidence)
    {
        if (matches.Count == 0)
            return emptyEvidence;

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

    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        int discoveredFiles,
        int analyzedFiles,
        int unreadableFiles,
        int candidateCount,
        int structurallySupportedCount,
        int namingApplicableCount,
        int namingViolationCount)
    {
        var summaryResult = new ArchitectureEvaluatorResult(
            Name,
            "DecoratorPatternSummary")
        {
            Target = projectPath,
            Category = ArchitectureRuleCategory.DesignPatterns.ToString()
        };

        summaryResult.Metrics["DiscoveredFiles"] = discoveredFiles;
        summaryResult.Metrics["AnalyzedFiles"] = analyzedFiles;
        summaryResult.Metrics["UnreadableFiles"] = unreadableFiles;
        summaryResult.Metrics["DecoratorCandidateCount"] = candidateCount;

        summaryResult.Metrics["StructurallySupportedCandidateCount"] =
            structurallySupportedCount;

        summaryResult.Metrics["DecoratorNamingApplicableCount"] =
            namingApplicableCount;

        summaryResult.Metrics["DecoratorNamingViolationCount"] =
            namingViolationCount;

        if (namingApplicableCount > 0)
        {
            summaryResult.Metrics["DecoratorNamingCompliance"] =
                (namingApplicableCount - namingViolationCount)
                * 100.0 / namingApplicableCount;
        }

        summaryResult.Metrics["StructuralSupportRate"] = candidateCount == 0
            ? 100
            : structurallySupportedCount * 100.0 / candidateCount;

        summaryResult.Metadata[
            BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
            BaseArchitectureEvaluator.ProjectScope;

        summaryResult.Metadata["SemanticAnalysisPerformed"] = "false";
        summaryResult.Metadata["NamingConventionVerified"] =
            (namingApplicableCount > 0).ToString();
        summaryResult.Metadata["StructuralAnalysisHeuristic"] = "true";

        return summaryResult;
    }

    private string ResolveLayer(string file)
    {
        if (Context?.Layers is null)
        {
            return "Unclassified";
        }

        foreach (var layer in Context.Layers)
        {
            if (layer.Files.Any(layerFile =>
                    string.Equals(
                        layerFile,
                        file,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return layer.Name;
            }
        }

        return "Unclassified";
    }

    private static string ResolveLanguage(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".ts" => "TypeScript",
            _ => "Unknown"
        };
    }

    private static int FindMatchingBrace(
        string source,
        int openingBraceIndex)
    {
        if (openingBraceIndex < 0
            || openingBraceIndex >= source.Length
            || source[openingBraceIndex] != '{')
        {
            return -1;
        }

        var depth = 0;

        for (var index = openingBraceIndex; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
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

    private sealed record ComponentMember(
        string Type,
        string Name);

    private sealed record DecoratorAnalysis(
        bool IsDecoratorCandidate,
        bool HasDecoratorSuffix,
        bool HasValidDecoratorName,
        string WrappedRole,
        bool HasAbstractionReference,
        bool HasWrappedComponent,
        bool HasConstructorInjection,
        bool HasDelegationCalls,
        int ComponentMemberCount,
        int DelegatedMemberCount,
        int StructuralEvidenceCount,
        double StructuralComplianceScore);
}
