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
                && pair.Value.Contains("emitted declared metric", StringComparison.Ordinal));
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

    [Fact]
    public void EvaluateDetailed_marks_framework_specific_rules_not_applicable_for_a_different_detected_family()
    {
        var rule = ArchitectureRuleRegistry.GetById("AEG-FRONT-R1");
        Assert.NotNull(rule);

        var fact = FrontendFact(rule!, "Widget.ts", 1);
        var context = new ProjectArchitectureContext
        {
            Framework = "React"
        };

        var outcome = CreateEngine().EvaluateDetailed(context, [fact]);

        Assert.Contains(rule!.Id, outcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == rule.Id);
        Assert.Contains(
            outcome.Stats.NotApplicableReasons,
            pair => pair.Key == rule.Id
                && pair.Value.Contains("React", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateDetailed_does_not_guess_frontend_applicability_without_fact_framework_evidence()
    {
        var rule = ArchitectureRuleRegistry.GetById("AEG-FRONT-R1");
        Assert.NotNull(rule);

        var fact = FrontendFact(rule!, "Widget.ts", 1);
        var context = new ProjectArchitectureContext
        {
            Framework = "UnknownFramework"
        };

        var outcome = CreateEngine().EvaluateDetailed(context, [fact]);

        // Unknown technology is not proof that the rule is inapplicable, but
        // a metric without file-level framework provenance is not enough to
        // claim a pass or violation either.
        Assert.DoesNotContain(rule!.Id, outcome.Stats.RulesNotApplicable);
        Assert.Contains(rule.Id, outcome.Stats.RulesNotEvaluated);
        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == rule.Id);
        Assert.Contains(
            outcome.Stats.NotEvaluatedReasons,
            pair => pair.Key == rule.Id
                && pair.Value.Contains("framework applicability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateDetailed_rejects_ambiguous_case_insensitive_metric_matches()
    {
        var rule = ArchitectureRuleRegistry.GetById("AEG-ARCH-R1");
        Assert.NotNull(rule);

        var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [rule!.MetricKey.ToLowerInvariant()] = 0,
            [rule.MetricKey.ToUpperInvariant()] = 1
        };

        var fact = new ArchitectureEvaluatorResult("ArchitecturalEvaluator", "SampleProject")
        {
            Category = "Architecture",
            Metrics = metrics,
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = rule.Scope.ToString()
            }
        };

        var outcome = CreateEngine().EvaluateDetailed(
            new ProjectArchitectureContext(),
            [fact]);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == rule.Id);
        Assert.Contains(rule.Id, outcome.Stats.RulesNotEvaluated);
    }


    [Fact]
    public void EvaluateDetailed_does_not_allow_an_unrelated_producer_to_satisfy_a_rule()
    {
        var rule = ArchitectureRuleRegistry.GetById("AEG-BACK-R2");
        Assert.NotNull(rule);

        var unrelatedFact = new ArchitectureEvaluatorResult("PerformanceEvaluator", "Widget.cs")
        {
            Metrics = new Dictionary<string, double>
            {
                [rule!.MetricKey] = 99
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/Widget.cs"
            }
        };

        var outcome = CreateEngine().EvaluateDetailed(
            new ProjectArchitectureContext(),
            [unrelatedFact]);

        Assert.DoesNotContain(outcome.Evaluations, evaluation => evaluation.RuleId == rule.Id);
        Assert.DoesNotContain(outcome.Results, result => result.RuleId == rule.Id);
        Assert.Contains(rule.Id, outcome.Stats.RulesNotEvaluated);
        Assert.Contains(outcome.Stats.NotEvaluatedReasons, pair =>
            pair.Key == rule.Id
            && pair.Value.Contains("producer", StringComparison.OrdinalIgnoreCase));
    }

    private static ArchitectureEvaluatorResult FrontendFact(
        ArchitectureRuleDefinition rule,
        string target,
        double value) =>
        new("AngularEvaluator", target)
        {
            Category = "Frontend",
            Metrics = new Dictionary<string, double>
            {
                [rule.MetricKey] = value
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = rule.Scope.ToString(),
                ["FilePath"] = $"src/{target}"
            }
        };

    private static RuleEngineCore CreateEngine() =>
        new(NullLogger<RuleEngineCore>.Instance, new AegisArchitecturePolicy());

    private static ArchitectureEvaluatorResult ProjectFact(string target, double circularDependencyCount) =>
        new("CircularDependencyEvaluator", target)
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
