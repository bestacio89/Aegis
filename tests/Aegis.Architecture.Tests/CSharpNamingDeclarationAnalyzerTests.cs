using Aegis.Architecture.Evaluators.Naming.Analysis;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class CSharpNamingDeclarationAnalyzerTests
{
    private readonly CSharpNamingDeclarationAnalyzer _analyzer = new();

    [Fact]
    public async Task AnalyzeAsync_discovers_declaration_kinds_and_source_positions()
    {
        const string source = """
            namespace Sample;

            public interface IOrderService { }

            public sealed class OrderService : IOrderService
            {
                private int _count;
                public const int MAX_RETRIES = 3;
                public int Count { get; set; }

                public OrderService(int initialCount)
                {
                    var localCount = initialCount;
                }

                public void ExecuteOrder(string orderId) { }
            }
            """;

        var result = await _analyzer.AnalyzeAsync(
            "OrderService.cs",
            source,
            framework: null);

        Assert.Equal(NamingAnalysisStatus.Evaluated, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("C#", result.Language);
        Assert.Equal(CSharpNamingDeclarationAnalyzer.Id, result.AnalyzerId);
        Assert.True(result.IsComplete);

        Assert.Contains(result.Declarations, d =>
            d.Name == "IOrderService" && d.Kind == NamingDeclarationKind.Interface && d.IsPublic);
        Assert.Contains(result.Declarations, d =>
            d.Name == "OrderService" && d.Kind == NamingDeclarationKind.Type && d.IsPublic);
        Assert.Contains(result.Declarations, d =>
            d.Name == "_count" && d.Kind == NamingDeclarationKind.Field && !d.IsPublic);
        Assert.Contains(result.Declarations, d =>
            d.Name == "MAX_RETRIES" && d.Kind == NamingDeclarationKind.Constant && d.IsConstant);
        Assert.Contains(result.Declarations, d =>
            d.Name == "Count" && d.Kind == NamingDeclarationKind.Property && d.IsPublic);
        Assert.Contains(result.Declarations, d =>
            d.Name == "ExecuteOrder" && d.Kind == NamingDeclarationKind.Method && d.IsPublic);
        Assert.Contains(result.Declarations, d =>
            d.Name == "orderId" && d.Kind == NamingDeclarationKind.Parameter);
        Assert.Contains(result.Declarations, d =>
            d.Name == "localCount" && d.Kind == NamingDeclarationKind.LocalVariable);

        Assert.All(result.Declarations, declaration =>
        {
            Assert.Equal("OrderService.cs", declaration.FilePath);
            Assert.True(declaration.StartLine > 0);
            Assert.True(declaration.StartColumn > 0);
            Assert.Equal(CSharpNamingDeclarationAnalyzer.Id, declaration.AnalyzerId);
        });
    }

    [Fact]
    public async Task AnalyzeAsync_returns_NoDeclarations_for_valid_source_without_declarations()
    {
        var result = await _analyzer.AnalyzeAsync(
            "Empty.cs",
            "// intentionally contains no declarations",
            framework: null);

        Assert.Equal(NamingAnalysisStatus.NoDeclarations, result.Status);
        Assert.Empty(result.Declarations);
        Assert.Empty(result.Diagnostics);
        Assert.True(result.IsComplete);
        Assert.False(result.HasDeclarationEvidence);
    }

    [Fact]
    public async Task AnalyzeAsync_preserves_partial_declaration_evidence_when_source_has_errors()
    {
        const string source = """
            public class BrokenService
            {
                public void Execute()
                {
                    int count = ;
                }
            }
            """;

        var result = await _analyzer.AnalyzeAsync(
            "BrokenService.cs",
            source,
            framework: null);

        Assert.Equal(NamingAnalysisStatus.PartiallyEvaluated, result.Status);
        Assert.NotEmpty(result.Declarations);
        Assert.NotEmpty(result.Diagnostics);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Declarations, declaration => declaration.Name == "BrokenService");
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.Equal("BrokenService.cs", diagnostic.FilePath);
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Code));
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        });
    }

    [Fact]
    public async Task AnalyzeAsync_reports_AnalysisFailed_when_syntax_errors_prevent_declaration_discovery()
    {
        var result = await _analyzer.AnalyzeAsync(
            "Broken.cs",
            "public class",
            framework: null);

        Assert.Equal(NamingAnalysisStatus.AnalysisFailed, result.Status);
        Assert.Empty(result.Declarations);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task AnalyzeAsync_honors_pre_cancelled_tokens()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _analyzer.AnalyzeAsync(
                "OrderService.cs",
                "public class OrderService { }",
                framework: null,
                cancellation.Token));
    }

    [Theory]
    [InlineData("OrderService.cs", "C#", true)]
    [InlineData("orderservice.CS", "c#", true)]
    [InlineData("OrderService.cs", "Java", false)]
    [InlineData("OrderService.java", "C#", false)]
    public void CanAnalyze_requires_csharp_language_and_source_extension(
        string filePath,
        string language,
        bool expected)
    {
        Assert.Equal(expected, _analyzer.CanAnalyze(filePath, language));
    }

    [Fact]
    public async Task AnalyzeAsync_rejects_invalid_arguments()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _analyzer.AnalyzeAsync(" ", "class Sample { }", null));

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _analyzer.AnalyzeAsync("Sample.cs", null!, null));
    }
}
