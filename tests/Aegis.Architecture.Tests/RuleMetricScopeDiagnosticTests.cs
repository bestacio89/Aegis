using Aegis.Architecture.RuleEngines;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class RuleMetricScopeDiagnosticTests
{
    [Fact]
    public void Known_metric_emitted_at_wrong_scope_is_not_counted_as_unknown()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var fileFact = new ArchitectureEvaluatorResult(
            "CircularDependencyEvaluator",
            "Widget.cs")
        {
            Category = "ArchitectureSummary",
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = 2
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/Widget.cs"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [fileFact]);

        Assert.Equal(1, outcome.Stats.FactsWithMetricsAtWrongScope);
        Assert.Equal(0, outcome.Stats.FactsWithoutMatchingScopedMetrics);
        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-ARCH-R1");

        Assert.Contains(
            "wrong scope",
            outcome.Stats.NotEvaluatedReasons["AEG-ARCH-R1"],
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "required: project",
            outcome.Stats.NotEvaluatedReasons["AEG-ARCH-R1"],
            StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Incomplete_project_analysis_explains_withheld_metric()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var summary = new ArchitectureEvaluatorResult(
            "ArchitecturalEvaluator",
            "ArchitectureSummary")
        {
            Category = "ArchitectureSummary",
            Metrics = new Dictionary<string, double>
            {
                ["ObservedDependencyCount"] = 4,
                ["ClassifiedDependencyCount"] = 2,
                ["IncompleteDependencyCount"] = 2
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project",
                ["AnalysisComplete"] = "false",
                ["AnalysisStatus"] = "Incomplete"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [summary]);

        Assert.Contains("AEG-ARCH-R2", outcome.Stats.RulesNotEvaluated);
        Assert.Contains(
            "incomplete analysis",
            outcome.Stats.NotEvaluatedReasons["AEG-ARCH-R2"],
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-ARCH-R2");
    }


    [Fact]
    public void Registered_but_unsupported_metric_is_not_reported_as_unknown()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var fact = new ArchitectureEvaluatorResult(
            "SecurityEvaluator",
            "SecuritySummary")
        {
            Category = "ArchitectureSummary",
            Metrics = new Dictionary<string, double>
            {
                ["SecurityComplianceIndex"] = 72
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };

        var unknownFact = new ArchitectureEvaluatorResult(
            "FixtureEvaluator",
            "UnknownMetricFixture")
        {
            Metrics = new Dictionary<string, double>
            {
                ["UnregisteredFixtureMetric"] = 1
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/UnknownMetricFixture.cs"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [fact, unknownFact]);

        Assert.Equal(1, outcome.Stats.FactsWithoutMatchingScopedMetrics);
        Assert.Equal(1, outcome.Stats.FactsWithKnownMetricsWithoutEligibleRule);
        Assert.Equal(0, outcome.Stats.FactsWithMetricsAtWrongScope);
        Assert.Contains(
            "AEG-ARCH-R10",
            outcome.Stats.RulesNotEvaluated);
    }



    [Fact]
    public void File_candidate_evidence_overrides_a_contradictory_zero_candidate_summary()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var summary = new ArchitectureEvaluatorResult(
            "BuilderPatternEvaluator",
            "BuilderPatternSummary")
        {
            Category = "DesignPatternSummary",
            Metrics = new Dictionary<string, double>
            {
                ["BuilderCandidateCount"] = 0,
                ["DiscoveredFileCount"] = 12,
                ["AnalyzedFileCount"] = 12,
                ["UnreadableFileCount"] = 0
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };

        var candidate = new ArchitectureEvaluatorResult(
            "BuilderPatternEvaluator",
            "BuilderFixture.cs")
        {
            Metrics = new Dictionary<string, double>
            {
                ["BuilderPatternComplianceIndex"] = 100,
                ["BuilderMutabilityViolationCount"] = 0
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/BuilderFixture.cs",
                ["BuilderClassName"] = "BuilderFixture"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [summary, candidate]);

        Assert.DoesNotContain("AEG-DES-BLD001", outcome.Stats.RulesNotApplicable);
        Assert.Contains(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-DES-BLD001");
    }

    [Fact]
    public void Disabled_rule_producer_is_reported_as_disabled_not_missing_metric()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var status = new ArchitectureEvaluatorResult(
            "StrategyPatternEvaluator",
            "StrategyProject")
        {
            Category = "EvaluatorStatus",
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project",
                ["EvaluatorExecutionStatus"] = "DisabledByPolicy",
                ["AnalysisStatus"] = "DisabledByPolicy",
                ["StatusReason"] = "Strategy analysis is disabled by policy."
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [status]);

        Assert.Contains("AEG-DES-STRAT001", outcome.Stats.DisabledByPolicyReasons.Keys);
        Assert.DoesNotContain("AEG-DES-STRAT001", outcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-DES-STRAT001", outcome.Stats.RulesNotEvaluated);
        Assert.Contains(
            "disabled by the active policy",
            outcome.Stats.DisabledByPolicyReasons["AEG-DES-STRAT001"],
            StringComparison.OrdinalIgnoreCase);
    }



    [Fact]
    public void Repository_leak_risk_cannot_satisfy_observer_unsubscribe_rule()
    {
        var policy = new AegisArchitecturePolicy();
        policy.Architecture.DesignPatterns.EnforceObserverPattern = true;
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy);

        var repositoryFact = new ArchitectureEvaluatorResult(
            "RepositoryPatternEvaluator",
            "OrderRepository.cs")
        {
            Metrics = new Dictionary<string, double>
            {
                ["LeakRisk"] = 1
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/OrderRepository.cs",
                ["Language"] = "C#"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [repositoryFact]);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation => evaluation.RuleId == "AEG-DES-OBS001");
        Assert.Contains("AEG-DES-OBS001", outcome.Stats.RulesNotEvaluated);
        Assert.Contains(
            "ObserverUnsubscribeMissingCount",
            outcome.Stats.NotEvaluatedReasons["AEG-DES-OBS001"],
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pattern_rules_are_not_applicable_when_complete_summaries_find_no_candidates()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var summary = new ArchitectureEvaluatorResult(
            "BuilderPatternEvaluator",
            "BuilderPatternSummary")
        {
            Category = "DesignPatternSummary",
            Metrics = new Dictionary<string, double>
            {
                ["BuilderCandidateCount"] = 0,
                ["DiscoveredFileCount"] = 12,
                ["AnalyzedFileCount"] = 12,
                ["UnreadableFileCount"] = 0
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [summary]);

        Assert.Contains("AEG-DES-BLD001", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-DES-BLD002", outcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-DES-BLD001", outcome.Stats.RulesNotEvaluated);
        Assert.Contains(
            "no applicable candidates",
            outcome.Stats.NotApplicableReasons["AEG-DES-BLD001"],
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CommandPatternEvaluator", "CommandCount", "UnreadableFileCount", "AnalyzedFileCount", "DiscoveredFileCount", "AEG-DES-CMD001")]
    [InlineData("ObserverPatternEvaluator", "ObserverCandidateCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-OBS001")]
    [InlineData("SingletonPatternEvaluator", "SingletonCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-SING001")]
    [InlineData("StrategyPatternEvaluator", "StrategyCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-STRAT001")]
    [InlineData("StrategyPatternEvaluator", "StrategyCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-STRAT002")]
    [InlineData("FacadePatternEvaluator", "FacadeCandidateCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-FAC001")]
    [InlineData("FacadePatternEvaluator", "FacadeCandidateCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-FAC002")]
    [InlineData("FactoryPatternEvaluator", "FactoryCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-FACR001")]
    [InlineData("FactoryPatternEvaluator", "FactoryCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-FACR002")]
    [InlineData("MediatorPatternEvaluator", "MediatorImplementationCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-MEDI001")]
    [InlineData("RepositoryPatternEvaluator", "RepositoryCount", "UnreadableFiles", "AnalyzedFiles", "DiscoveredFiles", "AEG-DES-REPO001")]
    public void Candidate_rules_are_not_applicable_only_after_every_source_file_was_analyzed(
        string producer,
        string candidateMetric,
        string unreadableMetric,
        string analyzedMetric,
        string discoveredMetric,
        string ruleId)
    {
        var policy = new AegisArchitecturePolicy();
        policy.Architecture.DesignPatterns.EnforceCommandPattern = true;
        policy.Architecture.DesignPatterns.EnforceObserverPattern = true;
        policy.Architecture.DesignPatterns.EnforceStrategyPattern = true;

        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy);

        var completeSummary = new ArchitectureEvaluatorResult(
            producer,
            "PatternSummary")
        {
            Category = "DesignPatternSummary",
            Metrics = new Dictionary<string, double>
            {
                [candidateMetric] = 0,
                [unreadableMetric] = 0,
                [analyzedMetric] = 12,
                [discoveredMetric] = 12
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project",
                ["AnalysisComplete"] = "true",
                ["AnalysisStatus"] = "Completed"
            }
        };

        var completeOutcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [completeSummary]);

        Assert.Contains(ruleId, completeOutcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain(ruleId, completeOutcome.Stats.RulesNotEvaluated);

        var incompleteSummary = new ArchitectureEvaluatorResult(
            producer,
            "PatternSummary")
        {
            Category = "DesignPatternSummary",
            Metrics = new Dictionary<string, double>
            {
                [candidateMetric] = 0,
                [unreadableMetric] = 1,
                [analyzedMetric] = 11,
                [discoveredMetric] = 12
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project",
                ["AnalysisComplete"] = "false",
                ["AnalysisStatus"] = "Incomplete"
            }
        };

        var incompleteOutcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [incompleteSummary]);

        Assert.DoesNotContain(ruleId, incompleteOutcome.Stats.RulesNotApplicable);
        Assert.Contains(ruleId, incompleteOutcome.Stats.RulesNotEvaluated);
    }

    [Fact]
    public void Layer_boundary_rules_are_not_applicable_to_mixed_repositories_without_layer_roles()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var context = new ProjectArchitectureContext
        {
            ProjectName = "Franz.Common",
            Nature = "MultiProject",
            DomainType = "Mixed",
            ArchitectureStyle = "Multi-Project / Mixed",
            Modules =
            [
                new ProjectModuleContext
                {
                    Name = "Franz.Common.Business",
                    Path = "sources/Franz.Common.Business",
                    ProjectKind = "ClassLibrary"
                },
                new ProjectModuleContext
                {
                    Name = "Franz.Common.AotSmokeTest",
                    Path = "tests/Franz.Common.AotSmokeTest",
                    ProjectKind = "Executable"
                }
            ],
            Layers =
            [
                new ProjectLayerContext { Name = "Unclassified:Franz.Common.Business" },
                new ProjectLayerContext { Name = "Unclassified:Franz.Common.AotSmokeTest" },
                new ProjectLayerContext { Name = "Tests" }
            ]
        };

        var outcome = engine.EvaluateDetailed(context, []);

        Assert.Contains("AEG-ARCH-R2", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-ARCH-R3", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-ARCH-R5", outcome.Stats.RulesNotApplicable);
        Assert.DoesNotContain("AEG-ARCH-R2", outcome.Stats.RulesNotEvaluated);
    }

    [Fact]
    public void Layer_boundary_rules_remain_applicable_when_a_layer_role_is_recognized()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var context = new ProjectArchitectureContext
        {
            ArchitectureStyle = "Multi-Project / Mixed",
            Modules =
            [
                new ProjectModuleContext { Name = "Domain", Path = "src/Domain", ProjectKind = "ClassLibrary" },
                new ProjectModuleContext { Name = "Infrastructure", Path = "src/Infrastructure", ProjectKind = "ClassLibrary" }
            ],
            Layers =
            [
                new ProjectLayerContext { Name = "Domain" },
                new ProjectLayerContext { Name = "Infrastructure" }
            ]
        };

        var outcome = engine.EvaluateDetailed(context, []);

        Assert.DoesNotContain("AEG-ARCH-R2", outcome.Stats.RulesNotApplicable);
        Assert.Contains("AEG-ARCH-R2", outcome.Stats.RulesNotEvaluated);
    }

}
