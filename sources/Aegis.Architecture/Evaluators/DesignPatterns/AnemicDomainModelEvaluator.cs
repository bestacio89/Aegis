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
/// Evaluates observable state and behavior characteristics of domain-model
/// candidates in C# and Java source files.
///
/// This evaluator produces lexical metrics and heuristic indicators.
/// It does not establish domain-model quality or definitively diagnose
/// anemic domain models.
///
/// Semantic analysis of method bodies, inherited members, and domain behavior
/// belongs to compiler-backed or language-specific analysis services.
/// </summary>
public sealed class AnemicDomainModelEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    private static readonly Regex TypeDeclarationRegex = new(
        @"\b(?:class|record(?:\s+(?:class|struct))?)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PropertyRegex = new(
        @"\b[A-Za-z_][A-Za-z0-9_<>.,?\[\]\s]*\s+[A-Za-z_][A-Za-z0-9_]*\s*\{\s*(?:get|set|init)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FieldRegex = new(
        @"(?m)^\s*(?:(?:public|private|protected|internal|static|final|readonly|volatile|transient|const|constexpr|mutable)\s+)*(?:[A-Za-z_][A-Za-z0-9_$.<>?,\[\]]*\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^;]*)?;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MethodRegex = new(
        @"(?m)^\s*(?:(?:public|private|protected|internal|static|final|abstract|virtual|override|sealed|async|synchronized|native|extern|default|strictfp)\s+)*(?:[A-Za-z_][A-Za-z0-9_$.<>?,\[\]]*\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConstructorRegex = new(
        @"(?m)^\s*(?:(?:public|private|protected|internal)\s+)*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] DomainIndicators =
    [
        "Entity",
        "Aggregate",
        "Domain",
        "Model",
        "Root",
        "ValueObject"
    ];

    private static readonly string[] DomainPathIndicators =
    [
        "/Domain/",
        "/Core/",
        "/Entities/",
        "/Aggregates/",
        "/ValueObjects/"
    ];

    public override string Name => "AnemicDomainModelEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java"
    ];

    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring Boot"
    ];

    public AnemicDomainModelEvaluator(
        ILogger<AnemicDomainModelEvaluator> logger,
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

        if (!_policy.DetectAnemicDomainModels)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Anemic domain model analysis disabled by policy.");

            return results;
        }

        var files = ResolveSourceFiles(
                projectPath,
                ".cs",
                ".java")
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

            var sanitizedContent = SanitizeCode(content);
            var fileIsDomainCandidate = IsDomainCandidateFile(file);

            foreach (Match typeMatch in TypeDeclarationRegex.Matches(sanitizedContent))
            {
                token.ThrowIfCancellationRequested();

                var typeName = typeMatch.Groups[1].Value;

                if (!fileIsDomainCandidate && !IsDomainType(typeName))
                {
                    continue;
                }

                var openingBrace = sanitizedContent.IndexOf(
                    '{',
                    typeMatch.Index + typeMatch.Length);

                if (openingBrace < 0)
                {
                    // Forwards declarations and incomplete source are not
                    // treated as analyzable class bodies.
                    continue;
                }

                var closingBrace = FindMatchingBrace(
                    sanitizedContent,
                    openingBrace);

                if (closingBrace < 0)
                {
                    AegisDiagnostics.Report(
                        Name,
                        DiagnosticLevel.Trace,
                        $"Unable to find the closing brace for type '{typeName}' in '{file}'.");

                    continue;
                }

                var typeBody = sanitizedContent[
                    (openingBrace + 1)..closingBrace];

                var propertyCount = CountMatches(
                    PropertyRegex,
                    typeBody);

                var fieldCount = CountMatches(
                    FieldRegex,
                    typeBody);

                var methodCount = CountMethods(
                    typeBody,
                    typeName);

                var constructorCount = CountConstructors(
                    typeBody,
                    typeName);

                var stateMemberCount = propertyCount + fieldCount;

                // A stateless type is not an anemic domain-model candidate.
                // Without state there is no state-to-behavior imbalance to assess.
                if (stateMemberCount == 0)
                    continue;

                var behaviorRatio = (double)methodCount / stateMemberCount;

                var behaviorScore = CalculateBehaviorScore(
                    stateMemberCount,
                    methodCount);

                var result = CreateTypeResult(
                    file,
                    typeName,
                    propertyCount,
                    fieldCount,
                    methodCount,
                    constructorCount,
                    behaviorRatio,
                    behaviorScore);

                results.Add(result);
                candidateTypes++;
            }
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Domain model analysis completed. " +
            $"Files discovered: {files.Count}; " +
            $"Files analyzed: {analyzedFiles}; " +
            $"Unreadable files: {unreadableFiles}; " +
            $"Candidate types: {candidateTypes}; " +
            $"Results: {results.Count}.");

        return results;
    }

    private ArchitectureEvaluatorResult CreateTypeResult(
        string file,
        string typeName,
        int propertyCount,
        int fieldCount,
        int methodCount,
        int constructorCount,
        double behaviorRatio,
        double behaviorScore)
    {
        var context = Context;

        var result = CreateResult(file, "DesignPattern");

        result.ProjectName = context?.ProjectName;
        result.Language = context?.Language;
        result.Framework = context?.Framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = context?.Confidence ?? 0;
        result.Category = "DesignPattern";

        result.Metrics["Properties"] = propertyCount;
        result.Metrics["Fields"] = fieldCount;
        result.Metrics["StateMemberCount"] = propertyCount + fieldCount;
        result.Metrics["Methods"] = methodCount;
        result.Metrics["Constructors"] = constructorCount;
        result.Metrics["BehaviorRatio"] = behaviorRatio;
        result.Metrics["DomainBehaviorScore"] = behaviorScore;
        result.Metrics["AnemicDomainModelIndex"] =
            1d - Math.Clamp(behaviorScore / 100d, 0d, 1d);

        result.Metadata["FilePath"] = file;
        result.Metadata["ClassName"] = typeName;
        result.Metadata["Language"] = context?.Language ?? "Unknown";
        result.Metadata["Framework"] = context?.Framework ?? "Unknown";
        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["MeasurementMethod"] =
            "Lexical count of property declarations, field declarations, and method-like declarations.";
        result.Metadata["ScoreInterpretation"] =
            "Heuristic state-to-method balance indicator; not a definitive anemic-domain-model diagnosis.";
        result.Metadata["SemanticAnalysisPerformed"] = false.ToString();
        result.Metadata["InheritanceResolved"] = false.ToString();
        result.Metadata["MethodBehaviorClassified"] = false.ToString();

        return result;
    }

    private static int CountMatches(
        Regex regex,
        string content)
    {
        return regex.Matches(content).Count;
    }

    private static int CountMethods(
        string typeBody,
        string typeName)
    {
        var count = 0;

        foreach (Match match in MethodRegex.Matches(typeBody))
        {
            var methodName = match.Groups["name"].Value;

            if (methodName.Equals(
                    typeName,
                    StringComparison.Ordinal))
            {
                // Constructors are reported separately.
                continue;
            }

            count++;
        }

        return count;
    }

    private static int CountConstructors(
        string typeBody,
        string typeName)
    {
        var count = 0;

        foreach (Match match in ConstructorRegex.Matches(typeBody))
        {
            if (match.Groups["name"].Value.Equals(
                    typeName,
                    StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static double CalculateBehaviorScore(
        int stateMemberCount,
        int methodCount)
    {
        if (stateMemberCount <= 0)
        {
            return methodCount > 0 ? 100 : 0;
        }

        var ratio = (double)methodCount / stateMemberCount;

        return Math.Round(
            Math.Min(ratio, 1.0) * 100.0,
            2);
    }

    private static bool IsDomainCandidateFile(string file)
    {
        var normalized = file.Replace('\\', '/');

        return DomainPathIndicators.Any(indicator =>
            normalized.Contains(
                indicator,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDomainType(string typeName)
    {
        return DomainIndicators.Any(indicator =>
            typeName.Contains(
                indicator,
                StringComparison.OrdinalIgnoreCase));
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

    private static int FindMatchingBrace(
        string content,
        int openingBrace)
    {
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

    /// <summary>
    /// Replaces comments and string/character literal contents with spaces,
    /// preserving newlines and source offsets for subsequent regex matching
    /// and brace balancing.
    ///
    /// Handles common C# and Java comments, ordinary strings, character
    /// literals, C# verbatim strings, C# raw strings, and Java text blocks.
    /// It is a lexical approximation, not a complete language lexer.
    /// </summary>
    private static string SanitizeCode(string content)
    {
        var buffer = new StringBuilder(content);
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inCharacter = false;
        var isVerbatimString = false;
        var rawStringQuoteCount = 0;

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
                if (rawStringQuoteCount > 0)
                {
                    if (current == '"')
                    {
                        var quoteCount = CountConsecutiveQuotes(
                            content,
                            index);

                        if (quoteCount >= rawStringQuoteCount)
                        {
                            for (var offset = 0; offset < rawStringQuoteCount; offset++)
                            {
                                buffer[index + offset] = ' ';
                            }

                            index += rawStringQuoteCount - 1;
                            inString = false;
                            rawStringQuoteCount = 0;
                            continue;
                        }
                    }

                    if (current is not '\r' and not '\n')
                    {
                        buffer[index] = ' ';
                    }

                    continue;
                }

                if (isVerbatimString && current == '"' && next == '"')
                {
                    buffer[index] = ' ';
                    buffer[index + 1] = ' ';
                    index++;
                    continue;
                }

                if (!isVerbatimString && current == '\\')
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
                    isVerbatimString = false;
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

            if (current == '/' && next == '/')
            {
                buffer[index] = ' ';
                buffer[index + 1] = ' ';
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                buffer[index] = ' ';
                buffer[index + 1] = ' ';
                index++;
                inBlockComment = true;
                continue;
            }

            if (current == '"')
            {
                var quoteCount = CountConsecutiveQuotes(
                    content,
                    index);

                if (quoteCount >= 3)
                {
                    rawStringQuoteCount = quoteCount;
                    inString = true;

                    for (var offset = 0; offset < quoteCount; offset++)
                    {
                        buffer[index + offset] = ' ';
                    }

                    index += quoteCount - 1;
                    continue;
                }

                isVerbatimString = index > 0 && content[index - 1] == '@';
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