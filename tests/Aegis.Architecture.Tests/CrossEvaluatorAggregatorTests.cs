using Aegis.Architecture.Aggregation;
using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class CrossEvaluatorAggregatorTests
{
    [Fact]
    public void Aggregate_reports_weighted_check_coverage_and_domain_health()
    {
        var aggregator = CreateAggregator();
        var violation = new ArchitectureRuleresult(
            "TEST-ARCH-1",
            "Sample architectural rule",
            ArchitectureRuleCategory.Architecture,
            ArchitectureRuleSeverity.Medium,
            "src/Widget.cs",
            null,
            "A violation",
            DateTimeOffset.UtcNow)
        {
            Domain = "Application",
            Target = "Widget",
            DetectedBy = "FixtureEvaluator"
        };

        var stats = new RuleEvaluationStats
        {
            RulesRegistered = 1,
            RulesApplicable = 1,
            RulesEvaluated = 1,
            RulesViolated = 1,
            ChecksPerformed = 2,
            ChecksViolated = 1,
            ChecksPassed = 1,
            CheckBuckets =
            [
                new RuleCheckBucket(
                    "Application",
                    ArchitectureRuleSeverity.Medium,
                    "Architecture",
                    2)
            ],
            RulesEvaluatedByDomain = new Dictionary<string, int>
            {
                ["Application"] = 1
            }
        };

        var report = aggregator.Aggregate([violation], stats: stats);

        var domain = Assert.Single(report.Domains);
        Assert.Equal("Application", domain.Domain);
        Assert.Equal(2, domain.ChecksPerformed);
        Assert.Equal(1, domain.Violations);
        Assert.Equal(1, domain.RulesEvaluated);
        Assert.Equal(0.5, domain.WeightedScore);
        Assert.Equal(0.5, domain.MaintainabilityIndex);
        Assert.Equal(0.5, domain.HealthIndex);
        Assert.Equal(50, report.Metrics.ProjectHealthIndex);
        Assert.Equal(0.5, report.Metrics.WeightedCompliance);
        Assert.Equal(50, report.ComplianceScores[ArchitectureRuleCategory.Architecture]);
        Assert.Same(stats, report.EvaluationStats);
        Assert.Single(report.Results);
    }

    [Fact]
    public void Aggregate_includes_known_clean_layers_when_checks_were_performed()
    {
        var context = new ProjectArchitectureContext();
        context.Modules.Add(new ProjectModuleContext
        {
            Name = "Application",
            Path = Path.Combine(Path.GetTempPath(), "Sample", "Application"),
            Layers =
            [
                new ProjectLayerContext { Name = "Application" },
                new ProjectLayerContext { Name = "Domain" }
            ]
        });

        var stats = new RuleEvaluationStats
        {
            RulesRegistered = 1,
            RulesApplicable = 1,
            RulesEvaluated = 1,
            RulesPassed = 1,
            ChecksPerformed = 1,
            ChecksPassed = 1,
            CheckBuckets =
            [
                new RuleCheckBucket(
                    "Application",
                    ArchitectureRuleSeverity.Medium,
                    "Architecture",
                    1)
            ],
            RulesEvaluatedByDomain = new Dictionary<string, int>
            {
                ["Application"] = 1
            }
        };

        var report = CreateAggregator().Aggregate(
            [],
            context: context,
            stats: stats);

        Assert.Contains(report.Domains, domain =>
            domain.Domain == "Application" && domain.ChecksPerformed == 1);
        Assert.Contains(report.Domains, domain =>
            domain.Domain == "Domain" && domain.ChecksPerformed == 0);
        Assert.Equal(100, report.Metrics.ProjectHealthIndex);
    }

    [Fact]
    public void Aggregate_empty_input_returns_a_valid_empty_report()
    {
        var report = CreateAggregator().Aggregate([]);

        Assert.Empty(report.Results);
        Assert.Empty(report.Domains);
        Assert.Equal(100, report.Metrics.ProjectHealthIndex);
        Assert.Equal(1, report.Metrics.WeightedCompliance);
    }

    private static CrossEvaluatorAggregator CreateAggregator() =>
        new(
            NullLogger<CrossEvaluatorAggregator>.Instance,
            new RuleWeightingEngine(NullLogger<RuleWeightingEngine>.Instance));
}
