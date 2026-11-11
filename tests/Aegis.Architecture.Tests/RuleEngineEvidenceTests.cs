using Aegis.Architecture.RuleEngines;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class RuleEngineEvidenceTests
{
    [Fact]
    public void EvaluateDetailed_preserves_metric_specific_source_evidence()
    {
        var fact = new ArchitectureEvaluatorResult("NamingEvaluator", "OrderService.cs")
        {
            File = "src/OrderService.cs",
            LineNumber = 18,
            Evidence = "public void createOrder() { }",
            Metrics = new Dictionary<string, double>
            {
                ["MethodNamingCompliance"] = 80
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/OrderService.cs",
                ["Evidence.MethodNamingCompliance"] =
                    "Line 18: Method 'createOrder' — public void createOrder() { }",
                ["EvidenceLine.MethodNamingCompliance"] = "18",
                ["CodeSnippet.MethodNamingCompliance"] =
                    "public void createOrder() { }"
            }
        };

        var outcome = CreateEngine().EvaluateDetailed(
            new ProjectArchitectureContext { Language = "C#" },
            [fact]);

        var finding = Assert.Single(
            outcome.Results,
            result => result.RuleId == "AEG-NAM-R3");

        Assert.Equal("src/OrderService.cs", finding.FilePath);
        Assert.Equal(18, finding.LineNumber);
        Assert.Equal("public void createOrder() { }", finding.CodeSnippet);
        Assert.Contains(
            "Line 18: Method 'createOrder'",
            Assert.Single(finding.Evidence));
        Assert.False(string.IsNullOrWhiteSpace(finding.Recommendation));
        Assert.Equal("NamingEvaluator", finding.DetectedBy);
    }

    [Fact]
    public void EvaluateDetailed_does_not_treat_a_symbol_target_as_a_source_file_path()
    {
        var fact = new ArchitectureEvaluatorResult("NamingEvaluator", "OrderService.CreateOrder")
        {
            Metrics = new Dictionary<string, double>
            {
                ["MethodNamingCompliance"] = 80
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File"
            }
        };

        var outcome = CreateEngine().EvaluateDetailed(
            new ProjectArchitectureContext { Language = "C#" },
            [fact]);

        Assert.DoesNotContain(
            outcome.Results,
            result => result.RuleId == "AEG-NAM-R3");
        Assert.Contains(outcome.Evaluations, evaluation =>
            evaluation.RuleId == "AEG-NAM-R3"
            && evaluation.Status == RuleEvaluationStatus.InsufficientEvidence);
        Assert.Contains("AEG-NAM-R3", outcome.Stats.RulesNotEvaluated);
        Assert.Equal(1, outcome.Stats.EvaluationsInsufficientEvidence);
    }

    private static RuleEngineCore CreateEngine() =>
        new(NullLogger<RuleEngineCore>.Instance, new AegisArchitecturePolicy());
}
