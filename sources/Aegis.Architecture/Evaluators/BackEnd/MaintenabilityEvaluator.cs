
using System.Text;
using System.Text.RegularExpressions;
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.BackEnd;

public sealed class MaintainabilityEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly MaintainabilityPolicy _policy;

    private static readonly Regex BraceLanguageDecisionRegex = new(
        @"\b(?:if|for|while|case|catch)\b" +
        @"|\b(?:else\s+if|elseif)\b" +
        @"|\b(?:switch)\b" +
        @"|&&|\|\||\?(?!\?)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex PythonDecisionRegex = new(
        @"\b(?:if|elif|for|while|except|and|or)\b" +
        @"|\b(?:match|case)\b" +
        @"|(?<!:)(?<!\?)\?(?!\?)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    public override string Name => "MaintainabilityEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python"
    ];

    public override string[] SupportedFrameworks =>
    [
        "LanguageAgnostic"
    ];

    public MaintainabilityEvaluator(
        ILogger<MaintainabilityEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Maintainability
            ?? new MaintainabilityPolicy();
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

        // Maintainability is language-specific, not framework-specific.
        // Resolve every supported source file independently in mixed repositories.
        var files = ResolveSourceFiles(
            projectPath,
            ".cs",
            ".java",
            ".py");

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing maintainability for {files.Count} files " +
            $"({context.Language}/{context.Framework}).");

        var analyzedFiles = 0;
        var unreadableFiles = 0;
        var unreadableFilePaths = new List<string>();

        double totalDecisionPoints = 0;
        double totalCommentDensity = 0;
        double totalNonEmptyLines = 0;
        double totalCommentLines = 0;
        double totalMaintainabilityScore = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException exception)
            {
                unreadableFiles++;
                unreadableFilePaths.Add(file);

                _logger.LogWarning(
                    exception,
                    "{Evaluator}: Unable to read source file {FilePath}.",
                    Name,
                    file);

                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFiles++;
                unreadableFilePaths.Add(file);

                _logger.LogWarning(
                    exception,
                    "{Evaluator}: Access denied reading source file {FilePath}.",
                    Name,
                    file);

                continue;
            }

            token.ThrowIfCancellationRequested();

            var language = DetectLanguage(file);
            if (!SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
                continue;

            var framework = ResolveFrameworkForFile(file, projectPath, context);
            var metrics = AnalyzeMaintainability(content, language);

            analyzedFiles++;

            totalDecisionPoints += metrics.DecisionPointCount;
            totalCommentDensity += metrics.CommentDensity;
            totalNonEmptyLines += metrics.NonEmptyLineCount;
            totalCommentLines += metrics.CommentLineCount;
            totalMaintainabilityScore += metrics.MaintainabilityScore;

            results.Add(CreateFileResult(file, metrics, language, framework));
        }

        token.ThrowIfCancellationRequested();

        results.Add(
            CreateSummary(
                files.Count,
                analyzedFiles,
                unreadableFiles,
                unreadableFilePaths,
                totalDecisionPoints,
                totalCommentDensity,
                totalNonEmptyLines,
                totalCommentLines,
                totalMaintainabilityScore));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Maintainability evaluation completed: {analyzedFiles} file(s) analyzed, " +
            $"{unreadableFiles} unreadable, {results.Count - 1} file result(s).");

        return results;
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string file,
        MaintainabilityMetrics metrics,
        string language,
        string? framework)
    {
        var result = CreateResult(file, "Maintainability");

        result.Language = language;
        result.Framework = framework;
        result.Layer = ResolveLayer(file);
        result.DetectionConfidence = Context?.Confidence ?? 0;

        result.Metrics["Complexity"] = metrics.DecisionPointCount;
        result.Metrics["DecisionPointCount"] = metrics.DecisionPointCount;
        result.Metrics["LineCount"] = metrics.NonEmptyLineCount;
        result.Metrics["CommentCount"] = metrics.CommentLineCount;
        result.Metrics["CommentDensity"] = metrics.CommentDensity;
        result.Metrics["MaintainabilityScore"] = metrics.MaintainabilityScore;

        result.Metadata["FilePath"] = file;
        result.Metadata["Language"] = language;
        result.Metadata["Framework"] = framework ?? "Unknown";
        result.Metadata["Layer"] = result.Layer ?? "Unknown";
        result.Metadata["EvaluationScope"] = FileScope;

        result.Metadata["ComplexityMeasurement"] = metrics.DecisionPointMeasurement;

        result.Metadata["CommentDensityMeasurement"] =
            "Percentage of non-empty physical lines containing comment text.";
        result.Metadata["MaintainabilityMeasurement"] =
            "Aegis heuristic (0-100): 100 minus a weighted maximum executable-member decision-point count for C# (Roslyn AST), or file-level lexical decision points for Java/Python, and non-comment code lines beyond the policy limit. Comment density is informational only. This is not an industry-standard maintainability index.";
        result.Metadata["CommentDensityInterpretation"] =
            "Comment density is informational only; low comment density is not treated as a maintainability defect.";
        result.Metadata["AnalysisComplete"] = "true";
        result.Metadata["AnalysisStatus"] = "Completed";
        result.Evidence = string.Join(Environment.NewLine, metrics.Evidence);
        result.Metadata["Evidence.MaintainabilityScore"] = result.Evidence;
        result.Metadata["EvidenceSummary.MaintainabilityScore"] = metrics.EvidenceSummary;
        result.Metadata["EvidenceScope.MaintainabilityScore"] = "FileMetrics";
        result.Metadata["EvidenceTruncated.MaintainabilityScore"] =
            metrics.EvidenceTruncated.ToString();

        if (metrics.FirstEvidenceLine is > 0)
        {
            result.LineNumber = metrics.FirstEvidenceLine;
            result.Metadata["EvidenceLine.MaintainabilityScore"] =
                metrics.FirstEvidenceLine.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            result.Metadata["CodeSnippet.MaintainabilityScore"] =
                GetSourceLine(metrics.Source, metrics.FirstEvidenceLine.Value);
        }

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        int filesDiscovered,
        int analyzedFiles,
        int unreadableFiles,
        IReadOnlyCollection<string> unreadableFilePaths,
        double totalDecisionPoints,
        double totalCommentDensity,
        double totalNonEmptyLines,
        double totalCommentLines,
        double totalMaintainabilityScore)
    {
        var context = Context;

        var summary = new ArchitectureEvaluatorResult(
            Name,
            "MaintainabilitySummary")
        {
            Category = "MaintainabilitySummary",
            ProjectName = context?.ProjectName,
            Language = context?.Language,
            Framework = context?.Framework,
            DetectionConfidence = context?.Confidence ?? 0
        };

        summary.Metrics["FilesDiscovered"] = filesDiscovered;
        summary.Metrics["AnalyzedFiles"] = analyzedFiles;
        summary.Metrics["UnreadableFiles"] = unreadableFiles;

        summary.Metrics["TotalDecisionPoints"] = totalDecisionPoints;
        summary.Metrics["AverageComplexity"] =
            analyzedFiles == 0
                ? 0
                : Math.Round(totalDecisionPoints / analyzedFiles, 2);

        summary.Metrics["TotalNonEmptyLines"] = totalNonEmptyLines;
        summary.Metrics["TotalCommentLines"] = totalCommentLines;
        summary.Metrics["AverageMaintainabilityScore"] =
            analyzedFiles > 0 && unreadableFiles == 0
                ? Math.Round(totalMaintainabilityScore / analyzedFiles, 2)
                : -1;

        // This is a weighted project-wide ratio, unlike the average of
        // per-file percentages, which gives tiny files equal influence.
        summary.Metrics["AverageCommentDensity"] =
            totalNonEmptyLines == 0
                ? 0
                : Math.Round(
                    totalCommentLines / totalNonEmptyLines * 100.0,
                    2);

        summary.Metadata["EvaluationScope"] = ProjectScope;
        summary.Metadata["Language"] = context?.Language ?? "Unknown";
        summary.Metadata["Framework"] = context?.Framework ?? "Unknown";

        summary.Metadata["ComplexityMeasurement"] =
            "Sum of per-file maximum executable-member decision-point counts for C# and file-level lexical decision-point counts for Java/Python.";

        summary.Metadata["CommentDensityMeasurement"] =
            "Project-wide comment-line count divided by non-empty physical source lines.";

        var discoveryDiagnosticCount = context?.DiscoveryDiagnostics.Count(
            diagnostic => diagnostic.AffectsCoverage) ?? 0;
        var analysisComplete = analyzedFiles == filesDiscovered
            && unreadableFiles == 0
            && discoveryDiagnosticCount == 0;
        summary.Metadata["AnalysisComplete"] =
            analysisComplete.ToString().ToLowerInvariant();
        summary.Metadata["AnalysisStatus"] = analysisComplete
            ? filesDiscovered == 0 ? "NoSourceFiles" : "Completed"
            : "Incomplete";
        summary.Metadata["DiscoveryDiagnosticCount"] =
            discoveryDiagnosticCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture);

        if (!analysisComplete)
        {
            var reasons = new List<string>();
            if (unreadableFiles > 0)
            {
                reasons.Add(
                    $"{unreadableFiles} source file(s) could not be read.");
            }

            if (analyzedFiles != filesDiscovered)
            {
                reasons.Add(
                    $"{analyzedFiles} of {filesDiscovered} discovered source file(s) were analyzed.");
            }

            if (discoveryDiagnosticCount > 0)
            {
                reasons.Add(
                    $"{discoveryDiagnosticCount} coverage-affecting project discovery diagnostic(s) were recorded.");
            }

            summary.Metadata["IncompleteReason"] = string.Join(" ", reasons);
            summary.Metadata["UnreadableFilePaths"] = string.Join(
                Environment.NewLine,
                unreadableFilePaths.Take(20));
        }

        summary.Metadata["MaintainabilityMeasurement"] =
            "Aegis heuristic; average is unavailable (-1) when any discovered file was unreadable. A scope with no governed source files is reported as NoSourceFiles, not as an incomplete analysis.";

        return summary;
    }

    private MaintainabilityMetrics AnalyzeMaintainability(
        string content,
        string? language)
    {
        var sanitized = SanitizeSource(
            content,
            language,
            out var commentLines);
        var commentLineCount = commentLines.Count;

        var physicalNonEmptyLineCount = content
            .Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line));

        var nonEmptyLineCount = sanitized
            .Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line));

        var commentDensity = physicalNonEmptyLineCount == 0
            ? 0
            : (double)commentLineCount / physicalNonEmptyLineCount * 100.0;

        var decisionRegex = GetDecisionRegex(language);
        var decisionMatches = decisionRegex?.Matches(sanitized)
            .Cast<Match>()
            .ToArray() ?? [];
        var decisionPointCount = decisionMatches.Length;
        var decisionPointMeasurement =
            "File-level lexical decision-point count; method boundaries are not parsed for this language.";
        var decisionEvidence = new List<string>();
        int? firstEvidenceLine = null;
        var evidenceTruncated = decisionMatches.Length > 50;
        string? maximumMemberName = null;
        int? maximumMemberLine = null;

        if (string.Equals(language, "C#", StringComparison.OrdinalIgnoreCase))
        {
            var csharpAnalysis = AnalyzeCSharpDecisionPoints(content);

            if (csharpAnalysis is not null)
            {
                decisionPointCount = csharpAnalysis.DecisionPoints.Count;
                maximumMemberName = csharpAnalysis.Member?.Name;
                maximumMemberLine = csharpAnalysis.Member?.Line;
                decisionPointMeasurement =
                    "Roslyn AST: maximum decision-point count of any executable member; nested local functions and lambdas are measured independently.";

                foreach (var decisionPoint in csharpAnalysis.DecisionPoints.Take(50))
                {
                    decisionEvidence.Add(
                        $"Line {decisionPoint.Line}: member '{maximumMemberName}' contains " +
                        $"{decisionPoint.Kind} — {GetSourceLine(content, decisionPoint.Line)}");
                }

                firstEvidenceLine = csharpAnalysis.DecisionPoints.FirstOrDefault()?.Line
                    ?? maximumMemberLine;
                evidenceTruncated = csharpAnalysis.DecisionPoints.Count > 50;
            }
            else
            {
                decisionPointMeasurement =
                    "File-level lexical fallback: Roslyn reported syntax errors, so method-level decision points could not be measured reliably.";
            }
        }

        if (decisionEvidence.Count == 0 && decisionMatches.Length > 0)
        {
            foreach (var match in decisionMatches.Take(50))
            {
                var lineNumber = GetLineNumber(sanitized, match.Index);
                decisionEvidence.Add(
                    $"Line {lineNumber}: file-level lexical decision point '{match.Value}' — " +
                    GetSourceLine(content, lineNumber));
            }

            firstEvidenceLine ??= GetLineNumber(sanitized, decisionMatches[0].Index);
        }

        var roundedCommentDensity = Math.Round(commentDensity, 2);
        var complexityPenalty = decisionPointCount * Math.Max(0, _policy.ComplexityWeight);
        var excessLines = Math.Max(0, nonEmptyLineCount - _policy.MaxLinesPerFile);
        var linePenalty = excessLines * Math.Max(0, _policy.LineWeight);
        // Comment volume is deliberately excluded from maintainability scoring.
        // Low comment density is not a defect when code is self-descriptive.
        var score = Math.Clamp(
            100.0 - complexityPenalty - linePenalty,
            0.0,
            100.0);

        var roundedScore = Math.Round(score, 2);
        var decisionPointDescription = maximumMemberName is not null
            ? $"maximum executable-member decision points {decisionPointCount} " +
              $"in '{maximumMemberName}' at line {maximumMemberLine}"
            : decisionPointMeasurement.StartsWith("Roslyn AST", StringComparison.Ordinal)
                ? "no executable members were found; decision points 0"
                : $"file-level lexical decision points {decisionPointCount}";

        var evidenceSummary =
            $"Maintainability score {roundedScore:0.##}/100; " +
            $"{decisionPointDescription} (penalty {complexityPenalty:0.##}); " +
            $"non-comment code lines {nonEmptyLineCount}, maximum {_policy.MaxLinesPerFile} " +
            $"(penalty {linePenalty:0.##}); comment density {roundedCommentDensity:0.##}% " +
            "(informational only; not included in the score).";

        var evidence = new List<string> { evidenceSummary };
        evidence.AddRange(decisionEvidence);

        return new MaintainabilityMetrics(
            decisionPointCount,
            nonEmptyLineCount,
            commentLineCount,
            roundedCommentDensity,
            roundedScore,
            evidence,
            evidenceSummary,
            firstEvidenceLine,
            content,
            evidenceTruncated,
            decisionPointMeasurement);
    }

    private static CSharpDecisionAnalysis? AnalyzeCSharpDecisionPoints(string content)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            content,
            new CSharpParseOptions(LanguageVersion.Latest));

        if (syntaxTree.GetDiagnostics().Any(
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        var root = syntaxTree.GetRoot();
        var executableMembers = root.DescendantNodesAndSelf()
            .Select(TryGetCSharpExecutableMember)
            .Where(member => member is not null)
            .Cast<CSharpExecutableMember>()
            .ToArray();

        if (executableMembers.Length == 0)
            return new CSharpDecisionAnalysis(null, []);

        var mostComplexMember = executableMembers
            .Select(member => new
            {
                Member = member,
                DecisionPoints = GetCSharpDecisionPoints(member.Body)
            })
            .OrderByDescending(item => item.DecisionPoints.Count)
            .ThenBy(item => item.Member.Line)
            .First();

        return new CSharpDecisionAnalysis(
            mostComplexMember.Member,
            mostComplexMember.DecisionPoints);
    }

    private static CSharpExecutableMember? TryGetCSharpExecutableMember(
        SyntaxNode node)
    {
        var body = node switch
        {
            BaseMethodDeclarationSyntax method =>
                (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor =>
                (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax localFunction =>
                (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression,
            LambdaExpressionSyntax lambda => lambda.Body,
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.Block,
            PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
            _ => null
        };

        if (body is null)
            return null;

        return new CSharpExecutableMember(
            body,
            GetCSharpExecutableMemberName(node),
            node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
    }

    private static string GetCSharpExecutableMemberName(SyntaxNode node) =>
        node switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => $"~{destructor.Identifier.ValueText}",
            OperatorDeclarationSyntax operation => $"operator {operation.OperatorToken.ValueText}",
            ConversionOperatorDeclarationSyntax conversion =>
                $"{conversion.ImplicitOrExplicitKeyword.ValueText} operator {conversion.Type}",
            AccessorDeclarationSyntax accessor => accessor.Keyword.ValueText,
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText,
            LambdaExpressionSyntax => "lambda",
            AnonymousMethodExpressionSyntax => "anonymous method",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            IndexerDeclarationSyntax => "this[]",
            _ => node.Kind().ToString()
        };

    private static IReadOnlyList<CSharpDecisionPoint> GetCSharpDecisionPoints(
        SyntaxNode executableBody)
    {
        return executableBody
            .DescendantNodesAndSelf(descendIntoChildren: node =>
                !IsNestedExecutableMember(node))
            .Where(IsCSharpDecisionPoint)
            .Select(node => new CSharpDecisionPoint(
                node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                node.Kind().ToString()))
            .ToArray();
    }

    private static bool IsNestedExecutableMember(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax
            or AccessorDeclarationSyntax
            or LocalFunctionStatementSyntax
            or LambdaExpressionSyntax
            or AnonymousMethodExpressionSyntax
            or PropertyDeclarationSyntax
            or IndexerDeclarationSyntax;

    private static bool IsCSharpDecisionPoint(SyntaxNode node) =>
        node is IfStatementSyntax
            or ForStatementSyntax
            or ForEachStatementSyntax
            or ForEachVariableStatementSyntax
            or WhileStatementSyntax
            or DoStatementSyntax
            or CatchClauseSyntax
            or ConditionalExpressionSyntax
            or CaseSwitchLabelSyntax
            or CasePatternSwitchLabelSyntax
            or SwitchExpressionArmSyntax
            or WhenClauseSyntax
        || node is BinaryExpressionSyntax binaryExpression
            && (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression)
                || binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
        || node is BinaryPatternSyntax binaryPattern
            && (binaryPattern.IsKind(SyntaxKind.AndPattern)
                || binaryPattern.IsKind(SyntaxKind.OrPattern));

    private static Regex? GetDecisionRegex(string? language) =>
        language?.Trim() switch
        {
            "Python" => PythonDecisionRegex,
            "C#" or "Java" => BraceLanguageDecisionRegex,
            _ => null
        };

    private static string DetectLanguage(string file) =>
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            _ => "Unknown"
        };

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
        return Context?.Layers
            .FirstOrDefault(layer =>
                layer.Files.Contains(
                    file,
                    StringComparer.OrdinalIgnoreCase))
            ?.Name;
    }

    private static string SanitizeSource(
        string content,
        string? language,
        out IReadOnlyCollection<int> commentLines)
    {
        var characters = content.ToCharArray();
        var discoveredCommentLines = new HashSet<int>();

        if (language?.Trim() == "Python")
        {
            SanitizePython(content, characters, discoveredCommentLines);
        }
        else
        {
            SanitizeBraceLanguage(content, characters, discoveredCommentLines);
        }

        commentLines = discoveredCommentLines;

        return new string(characters);
    }

    private static void SanitizeBraceLanguage(
        string content,
        char[] characters,
        HashSet<int> commentLines)
    {
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inCharacter = false;
        var inTextBlock = false;
        var verbatimString = false;
        var line = 0;

        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];
            var next = index + 1 < content.Length
                ? content[index + 1]
                : '\0';

            if (current == '\n')
            {
                line++;
            }

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                }
                else
                {
                    Blank(characters, index);
                    commentLines.Add(line + 1);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    Blank(characters, index);
                    Blank(characters, index + 1);
                    commentLines.Add(line);
                    index++;
                    inBlockComment = false;
                }
                else
                {
                    if (current is not '\r' and not '\n')
                    {
                        commentLines.Add(line);
                    }

                    Blank(characters, index);
                }

                continue;
            }

            if (inTextBlock)
            {
                if (current == '"'
                    && index + 2 < content.Length
                    && content[index + 1] == '"'
                    && content[index + 2] == '"')
                {
                    Blank(characters, index);
                    Blank(characters, index + 1);
                    Blank(characters, index + 2);
                    index += 2;
                    inTextBlock = false;
                }
                else
                {
                    Blank(characters, index);
                }

                continue;
            }

            if (inString || inCharacter)
            {
                Blank(characters, index);

                if (!verbatimString && current == '\\')
                {
                    if (index + 1 < content.Length)
                    {
                        Blank(characters, index + 1);

                        if (content[index + 1] == '\n')
                        {
                            line++;
                        }

                        index++;
                    }

                    continue;
                }

                if (inString && verbatimString
                    && current == '"' && next == '"')
                {
                    Blank(characters, index + 1);
                    index++;
                    continue;
                }

                if (inString && current == '"')
                {
                    inString = false;
                    verbatimString = false;
                }
                else if (inCharacter && current == '\'')
                {
                    inCharacter = false;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                Blank(characters, index);
                Blank(characters, index + 1);
                commentLines.Add(line);
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                Blank(characters, index);
                Blank(characters, index + 1);
                commentLines.Add(line);
                index++;
                inBlockComment = true;
                continue;
            }

            if (current == '"'
                && index + 2 < content.Length
                && content[index + 1] == '"'
                && content[index + 2] == '"')
            {
                Blank(characters, index);
                Blank(characters, index + 1);
                Blank(characters, index + 2);
                index += 2;
                inTextBlock = true;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                verbatimString =
                    index > 0 && content[index - 1] == '@';

                Blank(characters, index);
                continue;
            }

            if (current == '\'')
            {
                inCharacter = true;
                Blank(characters, index);
            }
        }
    }

    private static void SanitizePython(
        string content,
        char[] characters,
        HashSet<int> commentLines)
    {
        var inComment = false;
        var quoteCharacter = '\0';
        var tripleQuoted = false;
        var line = 0;

        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];

            if (current == '\n')
            {
                line++;
            }

            if (inComment)
            {
                if (current == '\n')
                {
                    inComment = false;
                }
                else
                {
                    Blank(characters, index);
                    commentLines.Add(line);
                }

                continue;
            }

            if (quoteCharacter != '\0')
            {
                if (current == '\\')
                {
                    Blank(characters, index);

                    if (index + 1 < content.Length)
                    {
                        Blank(characters, index + 1);

                        if (content[index + 1] == '\n')
                        {
                            line++;
                        }

                        index++;
                    }

                    continue;
                }

                if (tripleQuoted
                    && current == quoteCharacter
                    && index + 2 < content.Length
                    && content[index + 1] == quoteCharacter
                    && content[index + 2] == quoteCharacter)
                {
                    Blank(characters, index);
                    Blank(characters, index + 1);
                    Blank(characters, index + 2);
                    index += 2;
                    quoteCharacter = '\0';
                    tripleQuoted = false;
                    continue;
                }

                if (!tripleQuoted && current == quoteCharacter)
                {
                    Blank(characters, index);
                    quoteCharacter = '\0';
                    continue;
                }

                Blank(characters, index);
                continue;
            }

            if (current == '#')
            {
                Blank(characters, index);
                commentLines.Add(line);
                inComment = true;
                continue;
            }

            if (current is '\'' or '"')
            {
                quoteCharacter = current;

                tripleQuoted =
                    index + 2 < content.Length
                    && content[index + 1] == current
                    && content[index + 2] == current;

                Blank(characters, index);

                if (tripleQuoted)
                {
                    Blank(characters, index + 1);
                    Blank(characters, index + 2);
                    index += 2;
                }
            }
        }
    }

    private static void Blank(char[] characters, int index)
    {
        if (index >= 0
            && index < characters.Length
            && characters[index] is not '\r' and not '\n')
        {
            characters[index] = ' ';
        }
    }

    private sealed record MaintainabilityMetrics(
        int DecisionPointCount,
        int NonEmptyLineCount,
        int CommentLineCount,
        double CommentDensity,
        double MaintainabilityScore,
        IReadOnlyCollection<string> Evidence,
        string EvidenceSummary,
        int? FirstEvidenceLine,
        string Source,
        bool EvidenceTruncated,
        string DecisionPointMeasurement);

    private sealed record CSharpExecutableMember(
        SyntaxNode Body,
        string Name,
        int Line);

    private sealed record CSharpDecisionPoint(
        int Line,
        string Kind);

    private sealed record CSharpDecisionAnalysis(
        CSharpExecutableMember? Member,
        IReadOnlyList<CSharpDecisionPoint> DecisionPoints);
}
