using Aegis.Architecture.RuleEngines;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Architecture.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class RuleEngineScopeTests
{
    [Fact]
    public void EvaluateDetailed_matches_facts_only_to_rules_with_the_same_scope()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var projectFact = new ArchitectureEvaluatorResult(
            "CircularDependencyEvaluator",
            "SampleProject")
        {
            Category = "Architecture",
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = 1
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };

        var fileFact = new ArchitectureEvaluatorResult(
            "CircularDependencyEvaluator",
            "Widget.cs")
        {
            Category = "ArchitectureSummary",
            Metrics = new Dictionary<string, double>
            {
                // A project metric on a file fact must not trigger a project rule.
                ["CircularDependencyCount"] = 99
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/Widget.cs"
            }
        };

        var namingFact = new ArchitectureEvaluatorResult(
            "NamingEvaluator",
            "Widget.cs")
        {
            Category = "Naming",
            Metrics = new Dictionary<string, double>
            {
                ["TypeNamingCompliance"] = 20
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/Widget.cs"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [projectFact, fileFact, namingFact]);

        Assert.Contains(
            outcome.Evaluations,
            evaluation =>
                evaluation.RuleId == "AEG-ARCH-R1"
                && evaluation.Scope == "Project"
                && evaluation.Target == "SampleProject"
                && evaluation.ObservedValue == 1);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation =>
                evaluation.RuleId == "AEG-ARCH-R1"
                && evaluation.Scope == "File");

        Assert.Contains(
            outcome.Evaluations,
            evaluation =>
                evaluation.RuleId == "AEG-NAM-R1"
                && evaluation.Scope == "File"
                && evaluation.Target == "Widget.cs"
                && evaluation.ObservedValue == 20);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation =>
                evaluation.RuleId == "AEG-NAM-R1"
                && evaluation.Scope == "Project");
    }
    [Fact]
    public void Registry_validation_rejects_an_unspecified_rule_scope()
    {
        var rule = new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R1",
            Name = "Circular Dependencies Detected",
            Category = "Architecture",
            MetricKey = "CircularDependencyCount",
            Threshold = 0,
            Operator = ComparisonOperator.GreaterThan,
            Severity = ArchitectureRuleSeverity.Blocker,
            Recommendation = "Remove circular dependencies."
        };

        var errors = ArchitectureRuleRegistry.Validate([rule]);

        Assert.Contains(
            errors,
            error => error.Contains(
                "no valid, explicitly declared evaluation scope",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Non_finite_project_metric_does_not_complete_a_rule_evaluation()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var fact = new ArchitectureEvaluatorResult(
            "CircularDependencyEvaluator",
            "SampleProject")
        {
            Category = "Architecture",
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = double.NaN
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [fact]);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-ARCH-R1");
    }

    [Fact]
    public void Non_finite_rule_threshold_does_not_complete_a_rule_evaluation()
    {
        var policy = new AegisArchitecturePolicy();
        policy.RuleThresholds["AEG-ARCH-R1"] = double.PositiveInfinity;

        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy);

        var fact = new ArchitectureEvaluatorResult(
            "CircularDependencyEvaluator",
            "SampleProject")
        {
            Category = "Architecture",
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = 1
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [fact]);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-ARCH-R1");
    }

}
