using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class ArchitectureReportCoverageTests
{
    [Fact]
    public void ComputeCompliance_exposes_incomplete_rule_coverage_without_claiming_complete_health()
    {
        var report = new AegisArchitectureReport
        {
            EvaluationStats = new RuleEvaluationStats
            {
                RulesRegistered = 73,
                RulesApplicable = 73,
                RulesEvaluated = 29,
                EvaluationsErrored = 0
            }
        };

        report.ComputeCompliance(new Dictionary<ArchitectureRuleCategory, double>
        {
            [ArchitectureRuleCategory.Architecture] = 88.04
        });

        Assert.Equal(39.73, report.Metrics.RuleEvaluationCoverage, 2);
        Assert.Equal(
            Math.Round(88.04 * 29.0 / 73.0, 2),
            report.Metrics.CoverageAdjustedHealthIndex);
        Assert.False(report.Metrics.AssessmentComplete);
        Assert.Equal(88.04, report.Metrics.ProjectHealthIndex);
    }

    [Fact]
    public void Zero_applicable_rules_do_not_report_full_evaluation_coverage()
    {
        var stats = new RuleEvaluationStats
        {
            RulesRegistered = 76,
            RulesApplicable = 0,
            RulesEvaluated = 0
        };

        Assert.Equal(0.0, stats.EvaluationCoverage);
    }

    [Fact]
    public void ComputeCompliance_marks_assessment_complete_only_at_sufficient_coverage()
    {
        var report = new AegisArchitectureReport
        {
            EvaluationStats = new RuleEvaluationStats
            {
                RulesRegistered = 100,
                RulesApplicable = 100,
                RulesEvaluated = 100,
                EvaluationsErrored = 0
            }
        };

        report.ComputeCompliance(new Dictionary<ArchitectureRuleCategory, double>
        {
            [ArchitectureRuleCategory.Architecture] = 95
        });

        Assert.Equal(100, report.Metrics.RuleEvaluationCoverage);
        Assert.Equal(95, report.Metrics.CoverageAdjustedHealthIndex);
        Assert.True(report.Metrics.AssessmentComplete);
    }
}
