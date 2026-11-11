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
                ["EvaluationScope"] = "Project",
                ["Evidence.CircularDependencyCount"] =
                    "No source-backed circular dependency count was emitted."
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
                ["FilePath"] = "src/Widget.cs",
                ["Evidence.TypeNamingCompliance"] = "Line 1: Type 'Widget' — public class Widget { }",
                ["EvidenceLine.TypeNamingCompliance"] = "1",
                ["CodeSnippet.TypeNamingCompliance"] = "public class Widget { }"
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
    public void EvaluateDetailed_marks_metrics_without_evidence_as_insufficient_evidence()
    {
        var fact = new ArchitectureEvaluatorResult("NamingEvaluator", "Widget.cs")
        {
            Metrics = new Dictionary<string, double>
            {
                ["TypeNamingCompliance"] = 0
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/Widget.cs"
            }
        };

        var outcome = CreateEngine().EvaluateDetailed(
            new ProjectArchitectureContext { Language = "C#" },
            [fact]);

        Assert.Empty(outcome.Results);
        Assert.Contains(outcome.Evaluations, evaluation =>
            evaluation.RuleId == "AEG-NAM-R1"
            && evaluation.Status == RuleEvaluationStatus.InsufficientEvidence);
        Assert.Contains("AEG-NAM-R1", outcome.Stats.RulesNotEvaluated);
        Assert.Equal(1, outcome.Stats.EvaluationsInsufficientEvidence);
        Assert.Contains("traceable source evidence",
            outcome.Stats.NotEvaluatedReasons["AEG-NAM-R1"]);
    }

    [Fact]
    public void EvaluateDetailed_does_not_evaluate_a_metric_from_incomplete_analysis()
    {
        var fact = ProjectFact("PartialProject", 5);
        fact.Metadata["AnalysisComplete"] = "false";
        fact.Metadata["AnalysisStatus"] = "Incomplete";

        var outcome = CreateEngine().EvaluateDetailed(
            new ProjectArchitectureContext(),
            [fact]);

        Assert.Empty(outcome.Results);
        Assert.Contains(outcome.Evaluations, evaluation =>
            evaluation.RuleId == "AEG-ARCH-R1"
            && evaluation.Status == RuleEvaluationStatus.InsufficientEvidence);
        Assert.Equal(1, outcome.Stats.EvaluationsInsufficientEvidence);
        Assert.Contains("incomplete analysis",
            outcome.Stats.NotEvaluatedReasons["AEG-ARCH-R1"]);
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
    public void EvaluateDetailed_excludes_frontend_rules_only_after_complete_discovery_proves_no_frontend()
    {
        var policy = new AegisArchitecturePolicy();
        var completeContext = new ProjectArchitectureContext
        {
            Language = "C#",
            Framework = ".NET 10",
            DetectedFrameworks = [".NET 10"],
            FrontendFrameworkDiscoveryComplete = true
        };

        var completeOutcome = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy).EvaluateDetailed(completeContext, []);

        var frontendRuleIds = ArchitectureRuleRegistry.All
            .Where(rule => rule.Id.StartsWith("AEG-FRONT-", StringComparison.OrdinalIgnoreCase))
            .Select(rule => rule.Id)
            .ToArray();

        Assert.Equal(11, frontendRuleIds.Length);
        Assert.All(
            frontendRuleIds,
            ruleId => Assert.Contains(ruleId, completeOutcome.Stats.RulesNotApplicable));
        Assert.All(
            frontendRuleIds,
            ruleId => Assert.DoesNotContain(ruleId, completeOutcome.Stats.RulesNotEvaluated));

        var incompleteContext = new ProjectArchitectureContext
        {
            Language = "C#",
            Framework = ".NET 10",
            DetectedFrameworks = [".NET 10"],
            FrontendFrameworkDiscoveryComplete = false
        };

        var incompleteOutcome = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy).EvaluateDetailed(incompleteContext, []);

        Assert.All(
            frontendRuleIds,
            ruleId => Assert.DoesNotContain(ruleId, incompleteOutcome.Stats.RulesNotApplicable));
        Assert.All(
            frontendRuleIds,
            ruleId => Assert.Contains(ruleId, incompleteOutcome.Stats.RulesNotEvaluated));
    }

    [Fact]
    public void EvaluateDetailed_classifies_policy_disabled_rules_separately()
    {
        var disabledPolicy = new AegisArchitecturePolicy();
        disabledPolicy.Architecture.DesignPatterns.EnforceStrategyPattern = false;

        var cycleSummary = new ArchitectureEvaluatorResult(
            "CircularDependencyEvaluator",
            "Project")
        {
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = 0
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };

        var disabledOutcome = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            disabledPolicy).EvaluateDetailed(
                new ProjectArchitectureContext { Language = "C#" },
                [cycleSummary]);

        Assert.Contains("AEG-DES-STRAT001", disabledOutcome.Stats.DisabledByPolicyReasons.Keys);
        Assert.Contains("AEG-DES-STRAT002", disabledOutcome.Stats.DisabledByPolicyReasons.Keys);
        Assert.DoesNotContain("AEG-DES-STRAT001", disabledOutcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-DES-STRAT002", disabledOutcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-DES-STRAT001", disabledOutcome.Stats.RulesNotEvaluated);
        Assert.DoesNotContain("AEG-DES-STRAT002", disabledOutcome.Stats.RulesNotEvaluated);
        Assert.Equal(2, disabledOutcome.Stats.RulesDisabledByPolicy);

        var enabledPolicy = new AegisArchitecturePolicy();
        enabledPolicy.Architecture.DesignPatterns.EnforceStrategyPattern = true;

        var enabledOutcome = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            enabledPolicy).EvaluateDetailed(
                new ProjectArchitectureContext { Language = "C#" },
                [cycleSummary]);

        Assert.DoesNotContain("AEG-DES-STRAT001", enabledOutcome.Stats.DisabledByPolicyReasons.Keys);
        Assert.DoesNotContain("AEG-DES-STRAT002", enabledOutcome.Stats.DisabledByPolicyReasons.Keys);
        Assert.DoesNotContain("AEG-DES-STRAT001", enabledOutcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-DES-STRAT002", enabledOutcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-DES-STRAT001", enabledOutcome.Stats.RulesNotEvaluated);
        Assert.Contains("AEG-DES-STRAT002", enabledOutcome.Stats.RulesNotEvaluated);
        Assert.True(
            disabledOutcome.Stats.EvaluationCoverage > enabledOutcome.Stats.EvaluationCoverage,
            "Policy-disabled rules must not reduce evaluation coverage.");
    }

    [Fact]
    public void EvaluateDetailed_marks_strategy_rules_not_applicable_when_complete_analysis_finds_no_candidates()
    {
        var policy = new AegisArchitecturePolicy();
        policy.Architecture.DesignPatterns.EnforceStrategyPattern = true;

        var summary = new ArchitectureEvaluatorResult(
            "StrategyPatternEvaluator",
            "SampleProject")
        {
            Metrics = new Dictionary<string, double>
            {
                ["StrategyCount"] = 0,
                ["UnreadableFiles"] = 0,
                ["AnalyzedFiles"] = 2,
                ["DiscoveredFiles"] = 2
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project",
                ["AnalysisComplete"] = "true",
                ["AnalysisStatus"] = "Completed",
                ["EmptyImplementationSet"] = "True"
            }
        };

        var cycleSummary = new ArchitectureEvaluatorResult(
            "CircularDependencyEvaluator",
            "Project")
        {
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = 0
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };

        var outcome = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy).EvaluateDetailed(
                new ProjectArchitectureContext { Language = "C#" },
                [summary, cycleSummary]);

        Assert.Contains("AEG-DES-STRAT001", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-DES-STRAT002", outcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-DES-STRAT001", outcome.Stats.RulesNotEvaluated);
        Assert.DoesNotContain("AEG-DES-STRAT002", outcome.Stats.RulesNotEvaluated);

        summary.Metrics["StrategyCount"] = 1;
        var candidateOutcome = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy).EvaluateDetailed(
                new ProjectArchitectureContext { Language = "C#" },
                [summary, cycleSummary]);

        Assert.Equal(
            outcome.Stats.RulesApplicable + 2,
            candidateOutcome.Stats.RulesApplicable);
        Assert.True(
            outcome.Stats.EvaluationCoverage > candidateOutcome.Stats.EvaluationCoverage,
            "Rules proven inapplicable must be excluded from the coverage denominator.");
    }

    [Fact]
    public void EvaluateDetailed_marks_layer_boundary_rules_disabled_when_policy_disables_them()
    {
        var policy = new AegisArchitecturePolicy();
        policy.Architecture.EnforceLayerBoundaries = false;

        var context = new ProjectArchitectureContext
        {
            ArchitectureStyle = "Multi-Project / Mixed",
            Modules =
            [
                new() { Name = "Application", Path = "src/Application" },
                new() { Name = "Infrastructure", Path = "src/Infrastructure" }
            ],
            Layers =
            [
                new() { Name = "Application" },
                new() { Name = "Infrastructure" }
            ]
        };

        var outcome = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy).EvaluateDetailed(context, []);

        Assert.Contains("AEG-ARCH-R2", outcome.Stats.DisabledByPolicyReasons.Keys);
        Assert.Contains("AEG-ARCH-R3", outcome.Stats.DisabledByPolicyReasons.Keys);
        Assert.Contains("AEG-ARCH-R5", outcome.Stats.DisabledByPolicyReasons.Keys);
        Assert.DoesNotContain("AEG-ARCH-R2", outcome.Stats.RulesNotApplicable);
        Assert.Contains(
            outcome.Stats.DisabledByPolicyReasons,
            pair => pair.Key == "AEG-ARCH-R2"
                && pair.Value.Contains("disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateDetailed_marks_layer_boundary_rules_not_applicable_without_two_architectural_roles()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureStyle = "Multi-Project / Mixed",
            Modules =
            [
                new() { Name = "LibraryA", Path = "src/LibraryA" },
                new() { Name = "LibraryB", Path = "src/LibraryB" }
            ],
            Layers =
            [
                new() { Name = "Client" },
                new() { Name = "Tests" },
                new() { Name = "Unclassified:LibraryA" },
                new() { Name = "Unclassified:LibraryB" }
            ]
        };

        var outcome = CreateEngine().EvaluateDetailed(context, []);

        Assert.Contains("AEG-ARCH-R2", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-ARCH-R3", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-ARCH-R5", outcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-ARCH-R2", outcome.Stats.RulesNotEvaluated);
        Assert.DoesNotContain("AEG-ARCH-R3", outcome.Stats.RulesNotEvaluated);
        Assert.DoesNotContain("AEG-ARCH-R5", outcome.Stats.RulesNotEvaluated);
    }

    [Fact]
    public void EvaluateDetailed_keeps_layer_boundary_rules_applicable_when_two_architectural_roles_exist()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureStyle = "Multi-Project / Mixed",
            Modules =
            [
                new() { Name = "Application", Path = "src/Application" },
                new() { Name = "Infrastructure", Path = "src/Infrastructure" }
            ],
            Layers =
            [
                new() { Name = "Application" },
                new() { Name = "Infrastructure" }
            ]
        };

        var outcome = CreateEngine().EvaluateDetailed(context, []);

        Assert.DoesNotContain("AEG-ARCH-R2", outcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-ARCH-R3", outcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-ARCH-R5", outcome.Stats.RulesNotApplicable);
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
    public void Every_registered_rule_has_an_explicit_producer_contract_or_unsupported_disposition()
    {
        var outcome = CreateEngine().EvaluateDetailed(
            new ProjectArchitectureContext(),
            []);

        Assert.Equal(76, ArchitectureRuleRegistry.All.Count);
        Assert.Equal(28, outcome.Stats.RulesUnsupported);
        Assert.Equal(42, outcome.Stats.RulesApplicable);
        Assert.Equal(2, outcome.Stats.RulesDisabledByPolicy);
        Assert.Equal(42, outcome.Stats.NotEvaluatedReasons.Count);
        Assert.Equal(42, outcome.Stats.RulesNotEvaluated.Count);
        Assert.Equal(4, outcome.Stats.RulesNotApplicable.Count);
        Assert.Equal(
            outcome.Stats.RulesRegistered,
            outcome.Stats.RulesApplicable
                + outcome.Stats.RulesUnsupported
                + outcome.Stats.RulesNotApplicable.Count
                + outcome.Stats.RulesDisabledByPolicy);

        // Rule-level missing evidence is not a scoped evaluation result.
        Assert.Empty(outcome.Evaluations);
        Assert.Equal(0, outcome.Stats.EvaluationsNotEvaluated);
        Assert.Equal(0, outcome.Stats.EvaluationsInsufficientEvidence);
        Assert.Equal(0, outcome.Stats.EvaluationsErrored);
        Assert.Equal(0, outcome.Stats.RulesWithErrors);
        Assert.Equal(0, outcome.Stats.EvaluationCoverage);

        foreach (var rule in ArchitectureRuleRegistry.All)
        {
            if (outcome.Stats.NotApplicableReasons.ContainsKey(rule.Id)
                || outcome.Stats.DisabledByPolicyReasons.ContainsKey(rule.Id)
                || outcome.Stats.UnsupportedRuleReasons.ContainsKey(rule.Id))
            {
                continue;
            }

            Assert.Contains(rule.Id, outcome.Stats.NotEvaluatedReasons.Keys);
            var reason = outcome.Stats.NotEvaluatedReasons[rule.Id];
            Assert.DoesNotContain(
                "No producer-to-rule contract is registered",
                reason,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EvaluateDetailed_does_not_promote_heuristic_domain_and_mediator_signals_to_violations()
    {
        var facts = new[]
        {
            new ArchitectureEvaluatorResult("AnemicDomainModelEvaluator", "OrderModel.cs")
            {
                Metrics = new Dictionary<string, double>
                {
                    ["AnemicDomainModelIndex"] = 0.95
                },
                Metadata = new Dictionary<string, string>
                {
                    ["EvaluationScope"] = "File",
                    ["SemanticAnalysisPerformed"] = "false"
                }
            },
            new ArchitectureEvaluatorResult("MediatorPatternEvaluator", "OrderHandler.cs")
            {
                Metrics = new Dictionary<string, double>
                {
                    ["MediatorCouplingIndex"] = 0.95
                },
                Metadata = new Dictionary<string, string>
                {
                    ["EvaluationScope"] = "File",
                    ["SemanticAnalysisPerformed"] = "false"
                }
            }
        };

        var outcome = CreateEngine().EvaluateDetailed(
            new ProjectArchitectureContext(),
            facts);

        Assert.DoesNotContain(outcome.Results, result =>
            result.RuleId is "AEG-DES-ADM001" or "AEG-DES-MEDI001");
        Assert.Contains("AEG-DES-ADM001", outcome.Stats.UnsupportedRuleReasons.Keys);
        Assert.Contains("AEG-DES-MEDI001", outcome.Stats.UnsupportedRuleReasons.Keys);
        Assert.Contains("compiler-backed", outcome.Stats.UnsupportedRuleReasons["AEG-DES-ADM001"]);
        Assert.Contains("validated mediator-coupling", outcome.Stats.UnsupportedRuleReasons["AEG-DES-MEDI001"]);
    }

    [Fact]
    public void EvaluateDetailed_reports_pattern_rules_without_producers_as_explicitly_unsupported()
    {
        var outcome = CreateEngine().EvaluateDetailed(
            new ProjectArchitectureContext(),
            []);

        Assert.Contains(
            "no dedicated pattern-circular-reference producer",
            outcome.Stats.NotEvaluatedReasons["AEG-DES-CIRC001"],
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "no dedicated pattern-overuse producer",
            outcome.Stats.NotEvaluatedReasons["AEG-DES-OVER001"],
            StringComparison.OrdinalIgnoreCase);
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

        var fact = new ArchitectureEvaluatorResult("CircularDependencyEvaluator", "SampleProject")
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
            Evidence = circularDependencyCount > 0
                ? $"Dependency cycle: {target} -> DependencyA -> {target}."
                : $"Dependency graph for {target} was analyzed; no cycle was found.",
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = circularDependencyCount
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project",
                ["Evidence.CircularDependencyCount"] = circularDependencyCount > 0
                    ? $"Dependency cycle: {target} -> DependencyA -> {target}."
                    : $"Dependency graph for {target} was analyzed; no cycle was found."
            }
        };
}
