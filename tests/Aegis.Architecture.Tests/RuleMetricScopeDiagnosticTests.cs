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
            "SampleEvaluator",
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
}
