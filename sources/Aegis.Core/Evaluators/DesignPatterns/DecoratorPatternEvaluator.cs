
using Aegis.Architecture.Evaluators;
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

    private static readonly Regex StringAndCharacterLiteralRegex = new(
        @"@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LineCommentRegex = new(
        @"//[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BlockCommentRegex = new(
        @"/\*[\s\S]*?\*/",
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
        "ASP.NET",
        "Spring Boot",
        "Angular",
        "React",
        "Vue",
        "Node"
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
            var sanitizedSource = SanitizeSource(source);

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

                if (!analysis.HasValidDecoratorName)
                {
                    namingViolationCount++;
                }

                var result = CreateClassResult(
                    projectPath,
                    file,
                    className,
                    language,
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

        var structuralSignals = 0;

        if (hasAbstractionReference)
        {
            structuralSignals++;
        }

        if (hasWrappedComponent)
        {
            structuralSignals++;
        }

        if (hasConstructorInjection)
        {
            structuralSignals++;
        }

        var isDecoratorCandidate =
            hasDecoratorSuffix
            || (structuralSignals >= 3 && hasWrappedComponent);

        var evidenceCount = structuralSignals;
        var structuralScore = evidenceCount / 3.0 * 100.0;

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
        DecoratorAnalysis analysis)
    {
        var result = new ArchitectureEvaluatorResult(Name, className)
        {
            Target = projectPath,
            
            Category = ArchitectureRuleCategory.DesignPatterns.ToString()
        };

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

        result.Metrics["HasValidDecoratorName"] =
            analysis.HasValidDecoratorName ? 1 : 0;

        result.Metrics["ComponentMemberCount"] =
            analysis.ComponentMemberCount;

        result.Metrics["DelegatedMemberCount"] =
            analysis.DelegatedMemberCount;

        result.Metrics["StructuralEvidenceCount"] =
            analysis.StructuralEvidenceCount;

        result.Metrics["StructuralComplianceScore"] =
            analysis.StructuralComplianceScore;

        result.Metrics["DecoratorNamingCompliance"] =
            analysis.HasValidDecoratorName ? 100 : 0;

        result.Metrics["DecoratorComplianceScore"] =
            analysis.HasValidDecoratorName
                ? analysis.StructuralComplianceScore
                : analysis.StructuralComplianceScore * 0.75;

        result.Metadata[BaseArchitectureEvaluator.FilePathMetadataKey] = file;
        result.Metadata[BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
            BaseArchitectureEvaluator.FileScope;

        result.Metadata["ClassName"] = className;
        result.Metadata["Language"] = language;
        result.Metadata["DecoratorCandidate"] =
            analysis.IsDecoratorCandidate.ToString();

        result.Metadata["NamingConventionCompliant"] =
            analysis.HasValidDecoratorName.ToString();

        result.Metadata["WrappedComponentRole"] = analysis.WrappedRole;
        result.Metadata["SemanticAnalysisPerformed"] = "false";
        result.Metadata["NamingConventionVerified"] = "false";
        result.Metadata["StructuralAnalysisHeuristic"] = "true";
        result.Metadata["Layer"] = ResolveLayer(file);

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        int discoveredFiles,
        int analyzedFiles,
        int unreadableFiles,
        int candidateCount,
        int structurallySupportedCount,
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

        summaryResult.Metrics["DecoratorNamingViolationCount"] =
            namingViolationCount;

        summaryResult.Metrics["DecoratorNamingCompliance"] = candidateCount == 0
            ? 100
            : (candidateCount - namingViolationCount) * 100.0 / candidateCount;

        summaryResult.Metrics["StructuralSupportRate"] = candidateCount == 0
            ? 100
            : structurallySupportedCount * 100.0 / candidateCount;

        summaryResult.Metadata[
            BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
            BaseArchitectureEvaluator.ProjectScope;

        summaryResult.Metadata["SemanticAnalysisPerformed"] = "false";
        summaryResult.Metadata["NamingConventionVerified"] = "false";
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

    private static string SanitizeSource(string source)
    {
        // This is a lexical heuristic, not a full parser. It masks common
        // quoted literals and comments while preserving offsets and newlines.
        var sanitized = StringAndCharacterLiteralRegex.Replace(
            source,
            match => PreserveLineBreaks(match.Value));

        sanitized = BlockCommentRegex.Replace(
            sanitized,
            match => PreserveLineBreaks(match.Value));

        sanitized = LineCommentRegex.Replace(
            sanitized,
            match => PreserveLineBreaks(match.Value));

        return sanitized;
    }

    private static string PreserveLineBreaks(string value)
    {
        return string.Create(
            value.Length,
            value,
            static (buffer, original) =>
            {
                for (var index = 0; index < original.Length; index++)
                {
                    buffer[index] = original[index] is '\r' or '\n'
                        ? original[index]
                        : ' ';
                }
            });
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
