
using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Core.Evaluators.FrontEnd.src;

/// <summary>
/// Evaluates Blazor components using Roslyn C# syntax analysis and
/// explicitly qualified Razor source heuristics.
///
/// This evaluator does not claim semantic compilation, complete Razor parsing,
/// resolved dependency graphs, or runtime correctness.
///
/// Rule contracts:
/// AEG-FRONT-R8  -> ComplexityScore
/// AEG-FRONT-R9  -> CodeBehindScore
/// AEG-FRONT-R10 -> DependencyInjectionScore
/// AEG-FRONT-R11 -> ComplexityScore
///
/// Scores are expressed on a 0-100 scale.
/// </summary>
public sealed class BlazorEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;

    public override string Name => nameof(BlazorEvaluator);

    public override string[] SupportedLanguages =>
    [
        "Razor",
        "C#"
    ];

    public override string[] SupportedFrameworks =>
    [
        "Blazor"
    ];

    private static readonly Regex CodeBlockRegex = new(
        @"@\s*(?:code|functions)\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InjectionDirectiveRegex = new(
        @"(?m)^[ \t]*@inject[ \t]+(.+?)[ \t]+\w+[ \t]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ComponentTagRegex = new(
        @"<(?:(?:[A-Z][A-Za-z0-9_]*\.)*[A-Z][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EventBindingRegex = new(
        @"@\s*on[A-Za-z]+\s*(?::\s*[A-Za-z]+\s*)*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RazorCommentRegex = new(
        @"@\*[\s\S]*?\*@|<!--[\s\S]*?-->",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> LifecycleMethodNames =
    [
        "OnInitialized",
        "OnInitializedAsync",
        "OnParametersSet",
        "OnParametersSetAsync",
        "OnAfterRender",
        "OnAfterRenderAsync",
        "ShouldRender",
        "SetParametersAsync",
        "Dispose",
        "DisposeAsync"
    ];

    public BlazorEvaluator(
        ILogger<BlazorEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Frontend ?? new FrontendPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();
        var files = ResolveSourceFiles(projectPath, ".razor");

        if (files.Count == 0)
        {
            return results;
        }

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            if (string.Equals(
                    Path.GetFileName(file),
                    "_Imports.razor",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to read Razor component {FilePath}.",
                    file);

                continue;
            }

            token.ThrowIfCancellationRequested();

            var codeBehindPath = file + ".cs";
            string? codeBehindContent = null;

            if (File.Exists(codeBehindPath))
            {
                try
                {
                    codeBehindContent = await File.ReadAllTextAsync(
                        codeBehindPath,
                        token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Unable to read Blazor code-behind file {FilePath}.",
                        codeBehindPath);
                }
            }

            token.ThrowIfCancellationRequested();

            var metrics = AnalyzeComponent(
                content,
                codeBehindContent,
                token);

            var result = new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Frontend",

                Metrics =
                {
                    // Existing frontend rule contracts.
                    ["ComplexityScore"] = metrics.ComplexityScore,
                    ["CodeBehindScore"] = metrics.CodeBehindScore,
                    ["DependencyInjectionScore"] =
                        metrics.DependencyInjectionScore,

                    // Backwards compatibility with the original evaluator.
                    ["DependencyScore"] = metrics.DependencyScore,

                    // Additional component-level signals.
                    ["CompositionScore"] = metrics.CompositionScore,
                    ["LifecycleScore"] = metrics.LifecycleScore,
                    ["EventHandlingScore"] = metrics.EventScore,
                    ["BlazorComplianceScore"] = metrics.ComplianceScore,

                    ["CyclomaticComplexity"] = metrics.CyclomaticComplexity,
                    ["ExecutableStatementCount"] =
                        metrics.ExecutableStatementCount,
                    ["EventBindingCount"] = metrics.EventBindingCount,
                    ["LifecycleMethodCount"] =
                        metrics.LifecycleMethodCount,
                    ["ManualServiceConstructionCount"] =
                        metrics.ManualServiceConstructionCount,
                    ["CSharpSyntaxErrorCount"] =
                        metrics.CSharpSyntaxErrorCount,
                    ["InlineCodeLineCount"] = metrics.InlineCodeLineCount,
                    ["AnalysisConfidenceScore"] =
                        metrics.AnalysisConfidenceScore
                },

                Metadata =
                {
                    [FilePathMetadataKey] = file,
                    [EvaluationScopeMetadataKey] = FileScope,
                    ["FileName"] = Path.GetFileName(file),
                    ["Framework"] = "Blazor",
                    ["Language"] = "Razor",
                    ["LineCount"] = metrics.LineCount.ToString(),
                    ["InjectionCount"] = metrics.InjectionCount.ToString(),
                    ["ChildComponents"] =
                        metrics.ChildComponentCount.ToString(),
                    ["HasCodeBehind"] = metrics.HasCodeBehind.ToString(),
                    ["HasInlineCode"] = metrics.HasInlineCode.ToString(),
                    ["HasLifecycleMethod"] =
                        metrics.HasLifecycleMethod.ToString(),
                    ["HasAsyncVoidMethod"] =
                        metrics.HasAsyncVoidMethod.ToString(),
                    ["AnalysisMethod"] =
                        "RoslynSyntaxAndRazorHeuristics",
                    ["SemanticAnalysis"] = "NotPerformed",
                    ["RazorCompilerAnalysis"] = "NotPerformed",
                    ["AnalysisLimitations"] =
                        "Razor constructs are heuristically analyzed; C# uses Roslyn syntax trees without semantic compilation."
                }
            };

            results.Add(result);
        }

        if (results.Count > 0)
        {
            AddSummaryResult(results, projectPath);
        }

        return results;
    }

    private ComponentMetrics AnalyzeComponent(
        string content,
        string? codeBehindContent,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var visibleContent = RemoveRazorComments(content);
        var inlineBlocks = ExtractCSharpBlocks(visibleContent, token);

        var hasInlineCode = inlineBlocks.Count > 0;
        var hasCodeBehind = codeBehindContent is not null;

        var roots = new List<SyntaxNode>();
        var syntaxErrorCount = 0;
        var inlineCodeLineCount = 0;

        if (codeBehindContent is not null)
        {
            var tree = CSharpSyntaxTree.ParseText(
                codeBehindContent,
                path: "Component.razor.cs",
                cancellationToken: token);

            roots.Add(tree.GetRoot(token));

            syntaxErrorCount += tree.GetDiagnostics(token)
                .Count(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error);
        }

        for (var index = 0; index < inlineBlocks.Count; index++)
        {
            token.ThrowIfCancellationRequested();

            var block = inlineBlocks[index];

            inlineCodeLineCount += CountNonBlankLines(block);

            var wrappedCode =
                "internal sealed class __AegisRazorAnalysis\n{\n" +
                block +
                "\n}";

            var tree = CSharpSyntaxTree.ParseText(
                wrappedCode,
                path: $"Component.InlineCode{index}.cs",
                cancellationToken: token);

            roots.Add(tree.GetRoot(token));

            syntaxErrorCount += tree.GetDiagnostics(token)
                .Count(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error);
        }

        var nodes = roots
            .SelectMany(root => root.DescendantNodesAndSelf())
            .ToList();

        var methods = nodes
            .OfType<MethodDeclarationSyntax>()
            .ToList();

        var cyclomaticComplexity = CalculateCyclomaticComplexity(nodes);

        var executableStatementCount = nodes
            .OfType<StatementSyntax>()
            .Count(statement =>
                statement is not BlockSyntax &&
                statement is not EmptyStatementSyntax);

        var directiveInjectionCount =
            InjectionDirectiveRegex.Matches(visibleContent).Count;

        var attributeInjectionCount = nodes
            .OfType<PropertyDeclarationSyntax>()
            .Count(HasInjectAttribute);

        var injectionCount =
            directiveInjectionCount + attributeInjectionCount;

        var childComponentCount =
            ComponentTagRegex.Matches(visibleContent).Count;

        var eventBindingCount =
            EventBindingRegex.Matches(visibleContent).Count;

        var lifecycleMethodCount = methods.Count(method =>
            LifecycleMethodNames.Contains(method.Identifier.ValueText));

        var hasLifecycleMethod = lifecycleMethodCount > 0;

        var hasAsyncVoidMethod = methods.Any(IsAsyncVoidMethod);

        var manualConstructionCount = nodes
            .OfType<ObjectCreationExpressionSyntax>()
            .Count(IsExplicitServiceConstruction);

        var lineCount = CountLines(content);

        var complexityScore = CalculateComplexityScore(
            lineCount,
            cyclomaticComplexity);

        var codeBehindScore = CalculateCodeBehindScore(
            hasInlineCode,
            inlineCodeLineCount,
            executableStatementCount,
            hasCodeBehind);

        // R10 is specifically about manually constructing services.
        // Injection count is recorded separately and does not itself
        // constitute a violation.
        var dependencyInjectionScore =
            manualConstructionCount > 0 ? 0.0 : 100.0;

        var dependencyScore = CalculateDependencyScore(injectionCount);

        var compositionScore =
            CalculateCompositionScore(childComponentCount);

        // Missing lifecycle overrides are not inherently a defect.
        var lifecycleScore = 100.0;

        // async void is a strong static warning. This does not claim that
        // every event binding or handler has been semantically resolved.
        var eventScore = hasAsyncVoidMethod ? 0.0 : 100.0;

        var complianceScore = ComputeCompliance(
            complexityScore,
            codeBehindScore,
            dependencyInjectionScore,
            dependencyScore,
            compositionScore,
            lifecycleScore,
            eventScore);

        // A syntax error reduces confidence in the derived C# metrics.
        // The confidence score is separate from compliance and must not be
        // interpreted as a compliance rule result.
        var confidenceScore = syntaxErrorCount == 0 ? 100.0 : 60.0;

        return new ComponentMetrics
        {
            LineCount = lineCount,
            HasInlineCode = hasInlineCode,
            HasCodeBehind = hasCodeBehind,
            InjectionCount = injectionCount,
            ChildComponentCount = childComponentCount,
            EventBindingCount = eventBindingCount,
            CyclomaticComplexity = cyclomaticComplexity,
            ExecutableStatementCount = executableStatementCount,
            LifecycleMethodCount = lifecycleMethodCount,
            HasLifecycleMethod = hasLifecycleMethod,
            HasAsyncVoidMethod = hasAsyncVoidMethod,
            ManualServiceConstructionCount = manualConstructionCount,
            CSharpSyntaxErrorCount = syntaxErrorCount,
            InlineCodeLineCount = inlineCodeLineCount,
            ComplexityScore = complexityScore,
            CodeBehindScore = codeBehindScore,
            DependencyInjectionScore = dependencyInjectionScore,
            DependencyScore = dependencyScore,
            CompositionScore = compositionScore,
            LifecycleScore = lifecycleScore,
            EventScore = eventScore,
            ComplianceScore = complianceScore,
            AnalysisConfidenceScore = confidenceScore
        };
    }

    private static string RemoveRazorComments(string content) =>
        RazorCommentRegex.Replace(content, string.Empty);

    /// <summary>
    /// Extracts @code and @functions blocks using brace balancing that
    /// skips ordinary comments and quoted C# literals.
    /// This remains a bounded heuristic, not a full Razor parser.
    /// </summary>
    private static List<string> ExtractCSharpBlocks(
        string content,
        CancellationToken token)
    {
        var blocks = new List<string>();

        foreach (Match match in CodeBlockRegex.Matches(content))
        {
            token.ThrowIfCancellationRequested();

            var openingBrace = content.IndexOf(
                '{',
                match.Index,
                match.Length);

            if (openingBrace < 0)
            {
                continue;
            }

            var closingBrace = FindMatchingBrace(
                content,
                openingBrace,
                token);

            if (closingBrace < 0)
            {
                continue;
            }

            blocks.Add(content.Substring(
                openingBrace + 1,
                closingBrace - openingBrace - 1));
        }

        return blocks;
    }

    private static int FindMatchingBrace(
        string content,
        int openingBrace,
        CancellationToken token)
    {
        var depth = 1;
        var index = openingBrace + 1;

        while (index < content.Length)
        {
            if ((index & 0x3FF) == 0)
            {
                token.ThrowIfCancellationRequested();
            }

            var current = content[index];

            if (current == '/' && index + 1 < content.Length)
            {
                if (content[index + 1] == '/')
                {
                    index += 2;

                    while (index < content.Length &&
                           content[index] is not '\r' and not '\n')
                    {
                        index++;
                    }

                    continue;
                }

                if (content[index + 1] == '*')
                {
                    index += 2;

                    while (index + 1 < content.Length &&
                           !(content[index] == '*' &&
                             content[index + 1] == '/'))
                    {
                        index++;
                    }

                    index = Math.Min(index + 2, content.Length);
                    continue;
                }
            }

            if (current is '"' or '\'')
            {
                index = SkipQuotedLiteral(content, index);
                continue;
            }

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return index;
                }
            }

            index++;
        }

        return -1;
    }

    private static int SkipQuotedLiteral(string content, int quoteIndex)
    {
        var quote = content[quoteIndex];

        if (quote == '\'')
        {
            var index = quoteIndex + 1;

            while (index < content.Length)
            {
                if (content[index] == '\\')
                {
                    index = Math.Min(index + 2, content.Length);
                    continue;
                }

                if (content[index] == '\'')
                {
                    return index + 1;
                }

                index++;
            }

            return content.Length;
        }

        var isVerbatim =
            quoteIndex > 0 &&
            content[quoteIndex - 1] == '@';

        var quoteCount = 1;

        while (quoteIndex + quoteCount < content.Length &&
               content[quoteIndex + quoteCount] == '"')
        {
            quoteCount++;
        }

        // Raw C# string literals use three or more quote characters.
        if (quoteCount >= 3)
        {
            var index = quoteIndex + quoteCount;

            while (index < content.Length)
            {
                if (content[index] != '"')
                {
                    index++;
                    continue;
                }

                var runLength = 1;

                while (index + runLength < content.Length &&
                       content[index + runLength] == '"')
                {
                    runLength++;
                }

                if (runLength >= quoteCount)
                {
                    return index + quoteCount;
                }

                index += runLength;
            }

            return content.Length;
        }

        var cursor = quoteIndex + 1;

        while (cursor < content.Length)
        {
            if (isVerbatim &&
                content[cursor] == '"' &&
                cursor + 1 < content.Length &&
                content[cursor + 1] == '"')
            {
                cursor += 2;
                continue;
            }

            if (!isVerbatim && content[cursor] == '\\')
            {
                cursor = Math.Min(cursor + 2, content.Length);
                continue;
            }

            if (content[cursor] == '"')
            {
                return cursor + 1;
            }

            cursor++;
        }

        return content.Length;
    }

    private static int CalculateCyclomaticComplexity(
        IEnumerable<SyntaxNode> nodes)
    {
        var complexity = 1;

        foreach (var node in nodes)
        {
            switch (node)
            {
                case IfStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case CatchClauseSyntax:
                case ConditionalExpressionSyntax:
                case SwitchStatementSyntax:
                    complexity++;
                    break;

                case CaseSwitchLabelSyntax:
                    complexity++;
                    break;

                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                         binary.IsKind(SyntaxKind.LogicalOrExpression):
                    complexity++;
                    break;

                case SwitchExpressionSyntax switchExpression:
                    complexity += Math.Max(
                        0,
                        switchExpression.Arms.Count - 1);
                    break;
            }
        }

        return complexity;
    }

    private double CalculateComplexityScore(
        int lineCount,
        int cyclomaticComplexity)
    {
        // Preserve the existing policy's line-count limit, while adding
        // a separate syntax-based complexity signal.
        var lineLimit = _policy.MaxComponentComplexity;

        var sizeScore = 100.0;

        if (lineLimit > 0)
        {
            sizeScore = lineCount switch
            {
                var lines when lines <= lineLimit => 100.0,
                var lines when lines <= lineLimit * 1.5 => 80.0,
                var lines when lines <= lineLimit * 2.0 => 60.0,
                _ => 30.0
            };
        }

        var syntaxScore = cyclomaticComplexity switch
        {
            <= 10 => 100.0,
            <= 15 => 80.0,
            <= 20 => 60.0,
            _ => 30.0
        };

        return Math.Min(sizeScore, syntaxScore);
    }

    private static double CalculateCodeBehindScore(
        bool hasInlineCode,
        int inlineCodeLines,
        int executableStatementCount,
        bool hasCodeBehind)
    {
        if (!hasInlineCode || inlineCodeLines == 0)
        {
            return 100.0;
        }

        // The existence of code-behind is not enough to excuse an oversized
        // inline block. Conversely, a short @code block is not a defect.
        var score = inlineCodeLines switch
        {
            <= 20 => 100.0,
            <= 30 => 80.0,
            <= 50 => 60.0,
            _ => 40.0
        };

        if (executableStatementCount > 25)
        {
            score = Math.Min(score, 60.0);
        }

        if (hasCodeBehind && inlineCodeLines <= 30)
        {
            score = Math.Max(score, 80.0);
        }

        return score;
    }

    private static bool HasInjectAttribute(
        PropertyDeclarationSyntax property)
    {
        return property.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute =>
            {
                var lastSegment = attribute.Name.ToString()
                    .Split('.')
                    .Last();

                return string.Equals(
                           lastSegment,
                           "Inject",
                           StringComparison.Ordinal) ||
                       string.Equals(
                           lastSegment,
                           "InjectAttribute",
                           StringComparison.Ordinal);
            });
    }

    private static bool IsAsyncVoidMethod(
        MethodDeclarationSyntax method)
    {
        var isAsync = method.Modifiers.Any(
            modifier => modifier.IsKind(SyntaxKind.AsyncKeyword));

        var returnsVoid = method.ReturnType is PredefinedTypeSyntax type &&
                          type.Keyword.IsKind(SyntaxKind.VoidKeyword);

        return isAsync && returnsVoid;
    }

    private static bool IsExplicitServiceConstruction(
        ObjectCreationExpressionSyntax creation)
    {
        var typeName = creation.Type
            .ToString()
            .Split('.')
            .Last()
            .Split('<')
            .First();

        return typeName.EndsWith(
                   "Service",
                   StringComparison.Ordinal) ||
               typeName.EndsWith(
                   "Repository",
                   StringComparison.Ordinal) ||
               typeName.EndsWith(
                   "Manager",
                   StringComparison.Ordinal);
    }

    private static double CalculateDependencyScore(int injectionCount)
    {
        return injectionCount switch
        {
            <= 3 => 100.0,
            <= 5 => 80.0,
            <= 8 => 60.0,
            _ => 40.0
        };
    }

    private static double CalculateCompositionScore(int childCount)
    {
        return childCount switch
        {
            <= 10 => 100.0,
            <= 20 => 80.0,
            <= 30 => 60.0,
            _ => 40.0
        };
    }

    private static double ComputeCompliance(
        double complexityScore,
        double codeBehindScore,
        double dependencyInjectionScore,
        double dependencyScore,
        double compositionScore,
        double lifecycleScore,
        double eventScore)
    {
        var score =
            complexityScore * 0.35 +
            codeBehindScore * 0.15 +
            dependencyInjectionScore * 0.20 +
            dependencyScore * 0.05 +
            compositionScore * 0.10 +
            lifecycleScore * 0.05 +
            eventScore * 0.10;

        return Math.Round(score, 2);
    }

    private static int CountLines(string content)
    {
        if (content.Length == 0)
        {
            return 0;
        }

        return content.Count(character => character == '\n') +
               (content.EndsWith('\n') ? 0 : 1);
    }

    private static int CountNonBlankLines(string content)
    {
        return content.Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line));
    }

    private static void AddSummaryResult(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var componentCount = results.Count;

        var average = results.Average(result =>
            result.Metrics.GetValueOrDefault(
                "BlazorComplianceScore",
                0.0));

        var summary = new ArchitectureEvaluatorResult(
            nameof(BlazorEvaluator),
            projectPath)
        {
            Category = "FrontendSummary",

            Metrics =
            {
                ["BlazorComponentCount"] = componentCount,
                ["AverageComplianceScore"] = average,
                ["BlazorHealthIndex"] = average
            },

            Metadata =
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["Framework"] = "Blazor",
                ["Language"] = "Razor",
                ["ComponentCount"] = componentCount.ToString()
            }
        };

        results.Add(summary);
    }

    private sealed class ComponentMetrics
    {
        public int LineCount { get; init; }
        public bool HasInlineCode { get; init; }
        public bool HasCodeBehind { get; init; }
        public int InjectionCount { get; init; }
        public int ChildComponentCount { get; init; }
        public int EventBindingCount { get; init; }
        public int CyclomaticComplexity { get; init; }
        public int ExecutableStatementCount { get; init; }
        public int LifecycleMethodCount { get; init; }
        public bool HasLifecycleMethod { get; init; }
        public bool HasAsyncVoidMethod { get; init; }
        public int ManualServiceConstructionCount { get; init; }
        public int CSharpSyntaxErrorCount { get; init; }
        public int InlineCodeLineCount { get; init; }

        public double ComplexityScore { get; init; }
        public double CodeBehindScore { get; init; }
        public double DependencyInjectionScore { get; init; }
        public double DependencyScore { get; init; }
        public double CompositionScore { get; init; }
        public double LifecycleScore { get; init; }
        public double EventScore { get; init; }
        public double ComplianceScore { get; init; }
        public double AnalysisConfidenceScore { get; init; }
    }
}
