using Aegis.Architecture.RuleEngines;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class RuleEngineCoreBehaviorTests
{
    [Fact]
    public void EvaluateDetailed_records_both_passes_and_violations_but_returns_only_findings()
    {
        var engine = CreateEngine();

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [
                ProjectFact("CleanProject", 0),
                ProjectFact("BrokenProject", 2)
            ]);

        Assert.Contains(outcome.Evaluations, evaluation =>
            evaluation.RuleId == "AEG-ARCH-R1"
            && evaluation.Target == "CleanProject"
            && evaluation.Status == RuleEvaluationStatus.Passed);

        Assert.Contains(outcome.Evaluations, evaluation =>
            evaluation.RuleId == "AEG-ARCH-R1"
            && evaluation.Target == "BrokenProject"
            && evaluation.Status == RuleEvaluationStatus.Violated);

        Assert.Single(outcome.Results, result => result.Target == "BrokenProject");
        Assert.Equal(1, outcome.Stats.ChecksPassed);
        Assert.Equal(1, outcome.Stats.ChecksViolated);
        Assert.Equal(2, outcome.Stats.ChecksPerformed);
        Assert.Equal(1, outcome.Stats.RulesPassed);
        Assert.Equal(1, outcome.Stats.RulesViolated);
    }

    [Fact]
    public void EvaluateDetailed_prefers_rule_id_threshold_override_over_metric_default()
    {
        var policy = new AegisArchitecturePolicy();
        policy.RuleThresholds["AEG-ARCH-R1"] = 3;

        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy);

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [ProjectFact("SampleProject", 2)]);

        var evaluation = Assert.Single(
            outcome.Evaluations,
            item => item.RuleId == "AEG-ARCH-R1");

        Assert.Equal(RuleEvaluationStatus.Passed, evaluation.Status);
        Assert.Equal(3, evaluation.Threshold);
        Assert.Empty(outcome.Results);
    }

    [Fact]
    public void EvaluateDetailed_reports_missing_metrics_as_unevaluated_instead_of_passed()
    {
        var engine = CreateEngine();
        var fact = new ArchitectureEvaluatorResult("ArchitecturalEvaluator", "SampleProject")
        {
            Category = "Architecture",
            Metrics = new Dictionary<string, double>(),
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
        Assert.Contains("AEG-ARCH-R1", outcome.Stats.RulesNotEvaluated);
        Assert.Contains(
            outcome.Stats.NotEvaluatedReasons,
            pair => pair.Key == "AEG-ARCH-R1"
                && pair.Value.Contains("did not emit declared metric", StringComparison.Ordinal));
        Assert.Equal(0, outcome.Stats.ChecksPerformed);
    }

    [Fact]
    public void EvaluateDetailed_keeps_explicit_file_scope_even_when_category_looks_like_project_summary()
    {
        var engine = CreateEngine();
        var fact = new ArchitectureEvaluatorResult("NamingEvaluator", "Widget.cs")
        {
            Category = "ArchitectureSummary",
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = 10,
                ["TypeNamingCompliance"] = 100
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/Widget.cs"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [fact]);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-ARCH-R1");
        Assert.Contains(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-NAM-R1"
                && evaluation.Scope == "File");
    }

    private static RuleEngineCore CreateEngine() =>
        new(NullLogger<RuleEngineCore>.Instance, new AegisArchitecturePolicy());

    private static ArchitectureEvaluatorResult ProjectFact(string target, double circularDependencyCount) =>
        new("ArchitecturalEvaluator", target)
        {
            Category = "Architecture",
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = circularDependencyCount
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };
}
