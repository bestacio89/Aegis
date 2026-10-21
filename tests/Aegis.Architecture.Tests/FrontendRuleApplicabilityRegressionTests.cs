using Aegis.Architecture.RuleEngines;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class FrontendRuleApplicabilityRegressionTests
{
    [Fact]
    public void Blazor_complexity_is_not_evaluated_against_cross_framework_rule()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var fact = new ArchitectureEvaluatorResult(
            "BlazorEvaluator",
            "Components/Orders.razor")
        {
            Category = "Frontend",
            Metrics = new Dictionary<string, double>
            {
                ["ComplexityScore"] = 50
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "Components/Orders.razor",
                ["Framework"] = "Blazor",
                ["Language"] = "Razor"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext
            {
                Language = "C#",
                Framework = "Blazor",
                DetectedFrameworks = ["Blazor"]
            },
            [fact]);

        Assert.Contains(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-FRONT-R8");

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-FRONT-R11");

        Assert.Contains("AEG-FRONT-R11", outcome.Stats.RulesNotApplicable);
        Assert.Contains(
            "dedicated complexity rule",
            outcome.Stats.NotApplicableReasons["AEG-FRONT-R11"],
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void React_complexity_is_not_evaluated_against_cross_framework_rule()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var fact = new ArchitectureEvaluatorResult(
            "ReactEvaluator",
            "src/Orders.tsx")
        {
            Category = "Frontend",
            Metrics = new Dictionary<string, double>
            {
                ["ComplexityScore"] = 50
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/Orders.tsx",
                ["Framework"] = "React",
                ["Language"] = "TypeScript"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext
            {
                Language = "TypeScript",
                Framework = "React",
                DetectedFrameworks = ["React"]
            },
            [fact]);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-FRONT-R11");

        Assert.Contains("AEG-FRONT-R11", outcome.Stats.RulesNotApplicable);
    }
}
