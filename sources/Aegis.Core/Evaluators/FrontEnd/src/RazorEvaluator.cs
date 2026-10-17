using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Aegis.Core.Evaluators.FrontEnd.src;

public sealed class RazorEvaluator : BaseArchitectureEvaluator
{
    private const string ComplexityScoreMetric = "ComplexityScore";
    private const string PresentationSeparationScoreMetric =
        "PresentationSeparationScore";
    private const string ViewModelScoreMetric = "ViewModelScore";
    private const string DataIsolationScoreMetric = "DataIsolationScore";

    private static readonly Regex InlineCodeBlockRegex = new(
        @"@\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MarkupElementRegex = new(
        @"<\s*[A-Za-z][A-Za-z0-9:_-]*(?=\s|/?>)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ModelDirectiveRegex = new(
        @"(?m)^\s*@model\s+(?<model>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InjectDirectiveRegex = new(
        @"(?m)^\s*@inject\s+(?<type>[^\s]+)\s+(?<name>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DatabaseAccessRegex = new(
        @"\b(?:DbContext|DbSet\s*<|SaveChanges(?:Async)?|ExecuteSql(?:Raw)?(?:Async)?|FromSql(?:Raw|Interpolated)?(?:Async)?)\b",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    private static readonly Regex DataAccessNamespaceRegex = new(
        @"\b(?:Microsoft\.EntityFrameworkCore|System\.Data(?:\.|;)|Dapper)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ViewModelNameRegex = new(
        @"\b(?:[A-Za-z_]\w*ViewModel|[A-Za-z_]\w*PageModel)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ModelBindingRegex = new(
        @"\bModel\s*\.",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConditionalMarkupRegex = new(
        @"@\s*(?:if|switch|for|foreach|while)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HtmlHelperRegex = new(
        @"\bHtml\.(?:Raw|Partial|RenderPartial|Action|RenderAction)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TagHelperDirectiveRegex = new(
        @"(?m)^\s*@addTagHelper\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ServiceRegistrationOrInjectionRegex = new(
        @"\b(?:IServiceProvider|IServiceCollection|AddScoped|AddTransient|AddSingleton)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public RazorEvaluator(ILogger<RazorEvaluator> logger)
        : base(logger)
    {
    }

    public override string Name => nameof(RazorEvaluator);

    public override string[] SupportedLanguages => ["C#", "Razor"];

    public override string[] SupportedFrameworks =>
        ["ASP.NET Core MVC", "Razor Pages", "ASP.NET Core"];

    protected override Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        token.ThrowIfCancellationRequested();

        if (!Directory.Exists(projectPath))
        {
            _logger.LogWarning(
                "Razor evaluation skipped because the project directory does not exist: {ProjectPath}",
                projectPath);

            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
                Array.Empty<ArchitectureEvaluatorResult>());
        }

        var sourceFiles = ResolveSourceFiles(projectPath, ".cshtml");

        if (sourceFiles.Count == 0)
        {
            _logger.LogDebug(
                "No application Razor views found in {ProjectPath}.",
                projectPath);

            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
                Array.Empty<ArchitectureEvaluatorResult>());
        }

        var projectRoot = Path.GetFullPath(projectPath);
        var razorEngine = CreateRazorEngine(projectRoot);

        var results = new List<ArchitectureEvaluatorResult>(
            sourceFiles.Count + 1);

        var analyses = new List<ViewAnalysis>(sourceFiles.Count);

        foreach (var filePath in sourceFiles)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var analysis = AnalyzeView(
                    filePath,
                    projectRoot,
                    razorEngine,
                    token);

                analyses.Add(analysis);
                results.Add(CreateFileResult(filePath, analysis));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to analyze Razor view {FilePath}.",
                    filePath);

                results.Add(CreateFailedFileResult(filePath, exception));
            }
        }

        token.ThrowIfCancellationRequested();

        results.Add(CreateProjectSummary(projectPath, analyses));

        return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            results);
    }

    private static RazorProjectEngine CreateRazorEngine(string projectRoot)
    {
        var fileSystem = RazorProjectFileSystem.Create(projectRoot);

        return RazorProjectEngine.Create(
            RazorConfiguration.Default,
            fileSystem,
            builder => builder.SetNamespace("Aegis.Generated.Razor"));
    }

    private ViewAnalysis AnalyzeView(
        string filePath,
        string projectRoot,
        RazorProjectEngine razorEngine,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var content = File.ReadAllText(filePath);

        var relativePath = Path.GetRelativePath(projectRoot, filePath)
            .Replace('\\', '/');

        var razorItemPath = "/" + relativePath.TrimStart('/');
        var razorItem = razorEngine.FileSystem.GetItem(razorItemPath);

        if (!razorItem.Exists)
        {
            throw new FileNotFoundException(
                "The Razor project file system could not resolve the view.",
                filePath);
        }

        var codeDocument = razorEngine.Process(razorItem);
        var csharpDocument = codeDocument.GetCSharpDocument();
        var generatedCSharp = csharpDocument.GeneratedCode ?? string.Empty;

        token.ThrowIfCancellationRequested();

        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedCSharp,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: filePath + ".generated.cs",
            cancellationToken: token);

        var root = syntaxTree.GetRoot(token);
        var syntaxDiagnostics = syntaxTree.GetDiagnostics(token).ToArray();

        var razorErrorCount = csharpDocument.Diagnostics.Count(
            diagnostic => string.Equals(
                diagnostic.Severity.ToString(),
                "Error",
                StringComparison.OrdinalIgnoreCase));

        var csharpErrorCount = syntaxDiagnostics.Count(
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var csharpWarningCount = syntaxDiagnostics.Count(
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);

        var generatedMethodCount = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Count();

        var generatedConditionalCount = root.DescendantNodes()
            .Count(node =>
                node is IfStatementSyntax
                    or SwitchStatementSyntax
                    or ConditionalExpressionSyntax
                    or ForStatementSyntax
                    or ForEachStatementSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax);

        var generatedLogicalOperatorCount = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Count(node =>
                node.IsKind(SyntaxKind.LogicalAndExpression) ||
                node.IsKind(SyntaxKind.LogicalOrExpression));

        var inlineCodeBlockCount = InlineCodeBlockRegex.Matches(content).Count;
        var markupElementCount = MarkupElementRegex.Matches(content).Count;
        var conditionalMarkupCount =
            ConditionalMarkupRegex.Matches(content).Count;

        var modelMatch = ModelDirectiveRegex.Match(content);
        var modelType = modelMatch.Success
            ? modelMatch.Groups["model"].Value.Trim()
            : null;

        var injectDirectiveCount = InjectDirectiveRegex.Matches(content).Count;

        var hasViewModelReference =
            ViewModelNameRegex.IsMatch(content) ||
            (!string.IsNullOrWhiteSpace(modelType) &&
             ViewModelNameRegex.IsMatch(modelType));

        var hasModelBinding = ModelBindingRegex.IsMatch(content);

        var hasDatabaseAccess =
            DatabaseAccessRegex.IsMatch(content) ||
            DataAccessNamespaceRegex.IsMatch(content);

        var hasHtmlHelperUsage = HtmlHelperRegex.IsMatch(content);
        var hasTagHelperDirective = TagHelperDirectiveRegex.IsMatch(content);

        var hasServiceRegistrationOrInjection =
            ServiceRegistrationOrInjectionRegex.IsMatch(content);

        var hasGeneratedCodeErrors =
            razorErrorCount > 0 || csharpErrorCount > 0;

        return new ViewAnalysis(
            FilePath: filePath,
            LineCount: CountLines(content),
            MarkupElementCount: markupElementCount,
            InlineCodeBlockCount: inlineCodeBlockCount,
            ConditionalMarkupCount: conditionalMarkupCount,
            GeneratedMethodCount: generatedMethodCount,
            GeneratedConditionalCount: generatedConditionalCount,
            GeneratedLogicalOperatorCount: generatedLogicalOperatorCount,
            RazorErrorCount: razorErrorCount,
            CSharpErrorCount: csharpErrorCount,
            CSharpWarningCount: csharpWarningCount,
            ModelType: modelType,
            InjectDirectiveCount: injectDirectiveCount,
            HasViewModelReference: hasViewModelReference,
            HasModelBinding: hasModelBinding,
            HasDatabaseAccess: hasDatabaseAccess,
            HasHtmlHelperUsage: hasHtmlHelperUsage,
            HasTagHelperDirective: hasTagHelperDirective,
            HasServiceRegistrationOrInjection: hasServiceRegistrationOrInjection,
            HasGeneratedCodeErrors: hasGeneratedCodeErrors,
            ComplexityScore: CalculateComplexityScore(
                generatedConditionalCount,
                generatedLogicalOperatorCount,
                inlineCodeBlockCount,
                conditionalMarkupCount,
                generatedMethodCount),
            PresentationSeparationScore: CalculatePresentationSeparationScore(
                inlineCodeBlockCount,
                conditionalMarkupCount,
                hasHtmlHelperUsage,
                hasDatabaseAccess,
                markupElementCount),
            ViewModelScore: CalculateViewModelScore(
                modelType,
                hasViewModelReference,
                hasModelBinding,
                injectDirectiveCount,
                inlineCodeBlockCount),
            DataIsolationScore: CalculateDataIsolationScore(
                hasDatabaseAccess,
                inlineCodeBlockCount));
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string filePath,
        ViewAnalysis analysis)
    {
        var result = CreateResult(filePath);

        result.Metrics[ComplexityScoreMetric] = analysis.ComplexityScore;
        result.Metrics[PresentationSeparationScoreMetric] =
            analysis.PresentationSeparationScore;
        result.Metrics[ViewModelScoreMetric] = analysis.ViewModelScore;
        result.Metrics[DataIsolationScoreMetric] = analysis.DataIsolationScore;

        result.Metrics["RazorErrorCount"] = analysis.RazorErrorCount;
        result.Metrics["GeneratedCSharpErrorCount"] = analysis.CSharpErrorCount;
        result.Metrics["GeneratedCSharpWarningCount"] =
            analysis.CSharpWarningCount;
        result.Metrics["InlineCodeBlockCount"] = analysis.InlineCodeBlockCount;
        result.Metrics["ConditionalMarkupCount"] =
            analysis.ConditionalMarkupCount;
        result.Metrics["MarkupElementCount"] = analysis.MarkupElementCount;
        result.Metrics["GeneratedMethodCount"] = analysis.GeneratedMethodCount;
        result.Metrics["GeneratedConditionalCount"] =
            analysis.GeneratedConditionalCount;
        result.Metrics["GeneratedLogicalOperatorCount"] =
            analysis.GeneratedLogicalOperatorCount;
        result.Metrics["InjectDirectiveCount"] = analysis.InjectDirectiveCount;
        result.Metrics["LineCount"] = analysis.LineCount;

        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["FilePath"] = filePath;
        result.Metadata["AnalysisMethod"] =
            "RazorProjectEngine + Roslyn generated-C# syntax";
        result.Metadata["SemanticCompilationPerformed"] = bool.FalseString;
        result.Metadata["AnalysisStatus"] = "Completed";
        result.Metadata["HasGeneratedCodeErrors"] =
            analysis.HasGeneratedCodeErrors.ToString();

        result.Metadata["HasViewModelReference"] =
            analysis.HasViewModelReference.ToString();
        result.Metadata["HasModelBinding"] =
            analysis.HasModelBinding.ToString();
        result.Metadata["HasDatabaseAccessSignal"] =
            analysis.HasDatabaseAccess.ToString();
        result.Metadata["HasHtmlHelperUsage"] =
            analysis.HasHtmlHelperUsage.ToString();
        result.Metadata["HasTagHelperDirective"] =
            analysis.HasTagHelperDirective.ToString();
        result.Metadata["HasServiceRegistrationOrInjection"] =
            analysis.HasServiceRegistrationOrInjection.ToString();

        if (!string.IsNullOrWhiteSpace(analysis.ModelType))
        {
            result.Metadata["ModelType"] = analysis.ModelType;
        }

        return result;
    }

    private ArchitectureEvaluatorResult CreateFailedFileResult(
        string filePath,
        Exception exception)
    {
        var result = CreateResult(filePath);

        result.Metrics[ComplexityScoreMetric] = 0;
        result.Metrics[PresentationSeparationScoreMetric] = 0;
        result.Metrics[ViewModelScoreMetric] = 0;
        result.Metrics[DataIsolationScoreMetric] = 0;

        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["FilePath"] = filePath;
        result.Metadata["AnalysisStatus"] = "Failed";
        result.Metadata["AnalysisErrorType"] = exception.GetType().Name;
        result.Metadata["AnalysisError"] = exception.Message;
        result.Metadata["SemanticCompilationPerformed"] = bool.FalseString;

        return result;
    }

    private ArchitectureEvaluatorResult CreateProjectSummary(
        string projectPath,
        IReadOnlyCollection<ViewAnalysis> analyses)
    {
        var result = new ArchitectureEvaluatorResult(Name, projectPath);

        result.Metadata["EvaluationScope"] = ProjectScope;
        result.Metadata["AnalysisMethod"] =
            "RazorProjectEngine + Roslyn generated-C# syntax";
        result.Metadata["SemanticCompilationPerformed"] = bool.FalseString;
        result.Metadata["FilesAnalyzed"] =
            analyses.Count.ToString(CultureInfo.InvariantCulture);

        if (analyses.Count == 0)
        {
            result.Metrics[ComplexityScoreMetric] = 0;
            result.Metrics[PresentationSeparationScoreMetric] = 0;
            result.Metrics[ViewModelScoreMetric] = 0;
            result.Metrics[DataIsolationScoreMetric] = 0;

            return result;
        }

        result.Metrics[ComplexityScoreMetric] =
            analyses.Average(analysis => analysis.ComplexityScore);

        result.Metrics[PresentationSeparationScoreMetric] =
            analyses.Average(analysis => analysis.PresentationSeparationScore);

        result.Metrics[ViewModelScoreMetric] =
            analyses.Average(analysis => analysis.ViewModelScore);

        result.Metrics[DataIsolationScoreMetric] =
            analyses.Average(analysis => analysis.DataIsolationScore);

        result.Metrics["FilesAnalyzed"] = analyses.Count;
        result.Metrics["FilesWithGeneratedCodeErrors"] =
            analyses.Count(analysis => analysis.HasGeneratedCodeErrors);
        result.Metrics["RazorErrorCount"] =
            analyses.Sum(analysis => analysis.RazorErrorCount);
        result.Metrics["GeneratedCSharpErrorCount"] =
            analyses.Sum(analysis => analysis.CSharpErrorCount);
        result.Metrics["GeneratedCSharpWarningCount"] =
            analyses.Sum(analysis => analysis.CSharpWarningCount);
        result.Metrics["InlineCodeBlockCount"] =
            analyses.Sum(analysis => analysis.InlineCodeBlockCount);
        result.Metrics["MarkupElementCount"] =
            analyses.Sum(analysis => analysis.MarkupElementCount);
        result.Metrics["ConditionalMarkupCount"] =
            analyses.Sum(analysis => analysis.ConditionalMarkupCount);
        result.Metrics["DatabaseAccessSignalCount"] =
            analyses.Count(analysis => analysis.HasDatabaseAccess);
        result.Metrics["ViewModelReferenceCount"] =
            analyses.Count(analysis => analysis.HasViewModelReference);

        result.Metadata["FilesWithGeneratedCodeErrors"] =
            analyses.Count(analysis => analysis.HasGeneratedCodeErrors)
                .ToString(CultureInfo.InvariantCulture);

        return result;
    }

    private static double CalculateComplexityScore(
        int conditionalCount,
        int logicalOperatorCount,
        int inlineCodeBlockCount,
        int conditionalMarkupCount,
        int generatedMethodCount)
    {
        var complexitySignals =
            conditionalCount +
            logicalOperatorCount +
            (inlineCodeBlockCount * 2) +
            conditionalMarkupCount +
            Math.Max(0, generatedMethodCount - 1);

        return ClampScore(100.0 - complexitySignals * 3.0);
    }

    private static double CalculatePresentationSeparationScore(
        int inlineCodeBlockCount,
        int conditionalMarkupCount,
        bool hasHtmlHelperUsage,
        bool hasDatabaseAccess,
        int markupElementCount)
    {
        var penalty =
            inlineCodeBlockCount * 8.0 +
            conditionalMarkupCount * 2.0 +
            (hasHtmlHelperUsage ? 8.0 : 0.0) +
            (hasDatabaseAccess ? 45.0 : 0.0);

        if (markupElementCount == 0)
        {
            penalty += 5.0;
        }

        return ClampScore(100.0 - penalty);
    }

    private static double CalculateViewModelScore(
        string? modelType,
        bool hasViewModelReference,
        bool hasModelBinding,
        int injectDirectiveCount,
        int inlineCodeBlockCount)
    {
        var score = 100.0;

        if (string.IsNullOrWhiteSpace(modelType))
        {
            score -= 20.0;
        }

        if (!hasViewModelReference)
        {
            score -= 15.0;
        }

        if (!hasModelBinding && injectDirectiveCount == 0)
        {
            score -= 5.0;
        }

        score -= Math.Min(30.0, inlineCodeBlockCount * 5.0);

        return ClampScore(score);
    }

    private static double CalculateDataIsolationScore(
        bool hasDatabaseAccess,
        int inlineCodeBlockCount)
    {
        var score = 100.0;

        if (hasDatabaseAccess)
        {
            score -= 70.0;
        }

        // Inline Razor code is only a weak signal, not proof of a violation.
        score -= Math.Min(20.0, inlineCodeBlockCount * 4.0);

        return ClampScore(score);
    }

    private static double ClampScore(double value) =>
        Math.Clamp(value, 0.0, 100.0);

    private static int CountLines(string content)
    {
        if (content.Length == 0)
        {
            return 0;
        }

        var lineCount = 1;

        foreach (var character in content)
        {
            if (character == '\n')
            {
                lineCount++;
            }
        }

        return lineCount;
    }

    private sealed record ViewAnalysis(
        string FilePath,
        int LineCount,
        int MarkupElementCount,
        int InlineCodeBlockCount,
        int ConditionalMarkupCount,
        int GeneratedMethodCount,
        int GeneratedConditionalCount,
        int GeneratedLogicalOperatorCount,
        int RazorErrorCount,
        int CSharpErrorCount,
        int CSharpWarningCount,
        string? ModelType,
        int InjectDirectiveCount,
        bool HasViewModelReference,
        bool HasModelBinding,
        bool HasDatabaseAccess,
        bool HasHtmlHelperUsage,
        bool HasTagHelperDirective,
        bool HasServiceRegistrationOrInjection,
        bool HasGeneratedCodeErrors,
        double ComplexityScore,
        double PresentationSeparationScore,
        double ViewModelScore,
        double DataIsolationScore);
}