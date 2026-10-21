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

        Assert.DoesNotContain(
            outcome.Results,
            result => result.RuleId == "AEG-FRONT-R11");
        Assert.Contains(
            "dedicated complexity rule",
            outcome.Stats.NotApplicableReasons["AEG-FRONT-R11"],
            StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Razor_view_facts_require_and_use_razor_framework_evidence()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var razorFact = new ArchitectureEvaluatorResult(
            "RazorEvaluator",
            "Views/Orders/Index.cshtml")
        {
            Category = "Frontend",
            Metrics = new Dictionary<string, double>
            {
                ["PresentationSeparationScore"] = 80,
                ["ViewModelScore"] = 90,
                ["DataIsolationScore"] = 100
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "Views/Orders/Index.cshtml",
                ["Framework"] = "Razor",
                ["Language"] = "C#"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext
            {
                Language = "C#",
                Framework = "ASP.NET Core",
                DetectedFrameworks = ["ASP.NET Core"]
            },
            [razorFact]);

        Assert.Contains(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-FRONT-R5");

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-FRONT-R8");

        Assert.Contains(
            outcome.Results,
            result => result.RuleId == "AEG-FRONT-R5"
                && result.Target == "Views/Orders/Index.cshtml");
    }

    [Fact]
    public void Missing_frontend_frameworks_are_not_applicable_in_a_multi_module_repository()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext
            {
                ProjectName = "MixedBackendBlazor",
                Language = "C#",
                Framework = "ASP.NET Core",
                DetectedFrameworks = ["ASP.NET Core", "Blazor", ".NET"],
                Modules =
                [
                    new ProjectModuleContext { Name = "Api", Path = "src/Api" },
                    new ProjectModuleContext { Name = "Admin", Path = "src/Admin" }
                ]
            },
            []);

        Assert.Contains("AEG-FRONT-R1", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-FRONT-R2", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-FRONT-R3", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-FRONT-R4", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-FRONT-R5", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-FRONT-R6", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-FRONT-R7", outcome.Stats.RulesNotApplicable);
    }

    [Fact]
    public void React_complexity_uses_shared_rule_without_dedicated_rule()
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

        Assert.Contains(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-FRONT-R11"
                && evaluation.Target == "src/Orders.tsx"
                && evaluation.Evaluator == "ReactEvaluator");

        Assert.Contains(
            outcome.Results,
            result => result.RuleId == "AEG-FRONT-R11"
                && result.Target == "src/Orders.tsx");

        Assert.DoesNotContain("AEG-FRONT-R11", outcome.Stats.RulesNotApplicable);
    }
    [Fact]
    public void Mixed_frontend_project_does_not_cross_apply_blazor_rules_to_react_facts()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var blazorFact = new ArchitectureEvaluatorResult(
            "BlazorEvaluator",
            "Components/Orders.razor")
        {
            Category = "Frontend",
            Metrics = new Dictionary<string, double> { ["ComplexityScore"] = 50 },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "Components/Orders.razor",
                ["Framework"] = "Blazor",
                ["Language"] = "Razor"
            }
        };

        var reactFact = new ArchitectureEvaluatorResult(
            "ReactEvaluator",
            "src/Orders.tsx")
        {
            Category = "Frontend",
            Metrics = new Dictionary<string, double> { ["ComplexityScore"] = 10 },
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
                Language = "C#",
                Framework = "Blazor",
                DetectedFrameworks = ["Blazor", "React"]
            },
            [blazorFact, reactFact]);

        var blazorEvaluations = outcome.Evaluations
            .Where(evaluation => evaluation.RuleId == "AEG-FRONT-R8")
            .ToArray();

        Assert.Single(blazorEvaluations);
        Assert.Equal("Components/Orders.razor", blazorEvaluations[0].Target);
    }

}
