using Aegis.Architecture.RuleEngines;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureMetricContractTests
{
    [Fact]
    public void RuleEngine_withholds_public_surface_ratio_when_threshold_uses_percentage_scale()
    {
        var policy = new AegisArchitecturePolicy();
        policy.RuleThresholds["AEG-DES-FAC002"] = 80;
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy);

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext
            {
                ProjectName = "Sample",
                RootPath = Path.GetTempPath(),
                Language = "C#",
                Framework = "ASP.NET"
            },
            [
                new ArchitectureEvaluatorResult(
                    "FacadePatternEvaluator",
                    "SampleFacade")
                {
                    Target = Path.Combine(Path.GetTempPath(), "SampleFacade.cs"),
                    Category = "DesignPatterns",
                    Metrics = new Dictionary<string, double>
                    {
                        ["PublicSurfaceRatio"] = 0.25
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["EvaluationScope"] = "File",
                        ["FilePath"] = Path.Combine(Path.GetTempPath(), "SampleFacade.cs"),
                        ["MetricUnit.PublicSurfaceRatio"] = "RatioOfConfiguredMaximum"
                    }
                }
            ]);

        Assert.DoesNotContain(outcome.Evaluations, evaluation =>
            evaluation.RuleId == "AEG-DES-FAC002");
        Assert.Equal(1, outcome.Stats.MetricContractFailures);
        Assert.Contains("AEG-DES-FAC002", outcome.Stats.RulesNotEvaluated);
        Assert.Contains(
            "outside the verified scale",
            outcome.Stats.NotEvaluatedReasons["AEG-DES-FAC002"]);
    }

    [Fact]
    public void RuleEngine_withholds_god_class_finding_when_threshold_is_below_the_limit_ratio()
    {
        var policy = new AegisArchitecturePolicy();
        policy.RuleThresholds["AEG-DES-GOD001"] = 0.25;
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            policy);

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext
            {
                ProjectName = "Sample",
                RootPath = Path.GetTempPath(),
                Language = "C#",
                Framework = "ASP.NET"
            },
            [
                new ArchitectureEvaluatorResult(
                    "GodClassEvaluator",
                    "LargeService")
                {
                    Target = Path.Combine(Path.GetTempPath(), "LargeService.cs"),
                    Category = "DesignPatterns",
                    Metrics = new Dictionary<string, double>
                    {
                        ["GodClassSeverity"] = 2.0
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["EvaluationScope"] = "File",
                        ["FilePath"] = Path.Combine(Path.GetTempPath(), "LargeService.cs"),
                        ["MetricUnit.GodClassSeverity"] = "MinimumOfLineAndMethodLimitRatios"
                    }
                }
            ]);

        Assert.DoesNotContain(outcome.Evaluations, evaluation =>
            evaluation.RuleId == "AEG-DES-GOD001");
        Assert.Equal(1, outcome.Stats.MetricContractFailures);
        Assert.Contains(
            "must be at least 1",
            outcome.Stats.NotEvaluatedReasons["AEG-DES-GOD001"]);
    }
}
