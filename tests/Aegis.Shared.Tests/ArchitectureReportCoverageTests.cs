using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Rules;
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
                RulesRegistered = 76,
                RulesApplicable = 53,
                RulesUnsupported = 23,
                RulesEvaluated = 29,
                EvaluationsErrored = 0
            }
        };

        report.ComputeCompliance(new Dictionary<ArchitectureRuleCategory, double>
        {
            [ArchitectureRuleCategory.Architecture] = 88.04
        });

        Assert.Equal(Math.Round(29.0 / 53.0 * 100.0, 2), report.Metrics.RuleEvaluationCoverage, 2);
        Assert.Equal(
            Math.Round(88.04 * 29.0 / 53.0, 2),
            report.Metrics.CoverageAdjustedHealthIndex);
        Assert.False(report.Metrics.AssessmentComplete);
        Assert.Equal(88.04, report.Metrics.ProjectHealthIndex);
        Assert.True(report.Metrics.HasAssessmentEvidence);
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
    public void ComputeCompliance_does_not_infer_passes_from_absent_findings()
    {
        var report = new AegisArchitectureReport();

        report.ComputeCompliance();

        Assert.Empty(report.ComplianceScores);
        Assert.False(report.Metrics.HasAssessmentEvidence);
        Assert.False(report.Metrics.AssessmentComplete);
        Assert.Equal(0, report.Metrics.ProjectHealthIndex);
        Assert.Contains("HealthIndex: N/A", report.Summary, StringComparison.Ordinal);
        Assert.Contains("RuleCoverage: N/A", report.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeCompliance_uses_completed_rule_evaluations_when_category_scores_are_not_supplied()
    {
        var report = new AegisArchitectureReport();
        report.Evaluations.Add(new RuleEvaluationResult
        {
            RuleId = "AEG-ARCH-R1",
            Status = RuleEvaluationStatus.Passed
        });
        report.Evaluations.Add(new RuleEvaluationResult
        {
            RuleId = "AEG-ARCH-R1",
            Status = RuleEvaluationStatus.Violated
        });

        report.ComputeCompliance();

        Assert.Equal(50, report.ComplianceScores[ArchitectureRuleCategory.Architecture]);
        Assert.Equal(50, report.Metrics.ProjectHealthIndex);
        Assert.True(report.Metrics.HasAssessmentEvidence);
        Assert.False(report.Metrics.HasCoverageEvidence);
        Assert.Contains("CoverageAdjustedDiagnostic: N/A", report.Summary, StringComparison.Ordinal);
        Assert.False(report.Metrics.AssessmentComplete);
    }

    [Fact]
    public void ComputeCompliance_weights_domain_health_by_completed_checks()
    {
        var report = new AegisArchitectureReport
        {
            EvaluationStats = new RuleEvaluationStats
            {
                RulesRegistered = 2,
                RulesApplicable = 2,
                RulesEvaluated = 2
            }
        };
        report.Domains.Add(new ArchitectureDomainSummary
        {
            Domain = "Small",
            ChecksPerformed = 1,
            HasAssessmentEvidence = true,
            HealthIndex = 0.2
        });
        report.Domains.Add(new ArchitectureDomainSummary
        {
            Domain = "Large",
            ChecksPerformed = 9,
            HasAssessmentEvidence = true,
            HealthIndex = 0.8
        });

        report.ComputeCompliance(new Dictionary<ArchitectureRuleCategory, double>());

        Assert.Equal(74, report.Metrics.ProjectHealthIndex);
        Assert.True(report.Metrics.HasAssessmentEvidence);
        Assert.True(report.Metrics.HasCoverageEvidence);
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
        Assert.True(report.Metrics.HasAssessmentEvidence);
        Assert.True(report.Metrics.HasCoverageEvidence);
    }
}
