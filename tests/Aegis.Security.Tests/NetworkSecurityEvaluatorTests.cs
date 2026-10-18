using Aegis.Security.Evaluators.Network;
using Aegis.Security.Kernel.Network;
using Aegis.Security.Kernel.Network.Signals;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Xunit;

namespace Aegis.Security.Tests;

public sealed class NetworkSecurityEvaluatorTests
{
    [Fact]
    public async Task Open_critical_port_produces_critical_finding_with_remediation()
    {
        var evaluator = new NetworkSecurityEvaluator();
        var signal = new PortSecuritySignal(
            new PortScanResult("db.internal", 5432, true, 25, null));

        var result = await evaluator.EvaluateAsync(
            new ProjectSecurityContext(),
            [signal]);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(SecurityCategory.Network, result.Category);
        Assert.Equal(SecuritySeverity.Critical, finding.SeverityLevel);
        Assert.Contains("5432", finding.Message);
        Assert.Contains("db.internal", finding.Message);
        Assert.False(string.IsNullOrWhiteSpace(finding.Remediation));
    }

    [Fact]
    public async Task Closed_critical_port_does_not_produce_exposure_finding()
    {
        var evaluator = new NetworkSecurityEvaluator();
        var signal = new PortSecuritySignal(
            new PortScanResult("db.internal", 5432, false, 25, null));

        var result = await evaluator.EvaluateAsync(
            new ProjectSecurityContext(),
            [signal]);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Open_non_critical_port_with_high_latency_produces_medium_finding()
    {
        var evaluator = new NetworkSecurityEvaluator();
        var signal = new PortSecuritySignal(
            new PortScanResult("service.internal", 8080, true, 501, null));

        var result = await evaluator.EvaluateAsync(
            new ProjectSecurityContext(),
            [signal]);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(SecuritySeverity.Medium, finding.SeverityLevel);
        Assert.Contains("501ms", finding.Message);
    }

    [Fact]
    public async Task Port_error_produces_low_severity_diagnostic_finding()
    {
        var evaluator = new NetworkSecurityEvaluator();
        var signal = new PortSecuritySignal(
            new PortScanResult("service.internal", 8080, false, 0, "connection refused"));

        var result = await evaluator.EvaluateAsync(
            new ProjectSecurityContext(),
            [signal]);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(SecuritySeverity.Low, finding.SeverityLevel);
        Assert.Contains("connection refused", finding.Message);
    }

    [Fact]
    public async Task Open_critical_port_can_produce_both_exposure_and_latency_findings()
    {
        var evaluator = new NetworkSecurityEvaluator();
        var signal = new PortSecuritySignal(
            new PortScanResult("db.internal", 5432, true, 800, null));

        var result = await evaluator.EvaluateAsync(
            new ProjectSecurityContext(),
            [signal]);

        Assert.Equal(2, result.Findings.Count);
        Assert.Contains(result.Findings, finding => finding.SeverityLevel == SecuritySeverity.Critical);
        Assert.Contains(result.Findings, finding => finding.SeverityLevel == SecuritySeverity.Medium);
    }

    [Fact]
    public async Task Unrelated_signal_types_are_ignored()
    {
        var evaluator = new NetworkSecurityEvaluator();
        var signal = new SecuritySignal
        {
            Category = "Network",
            Evidence = "untyped signal without port metadata"
        };

        var result = await evaluator.EvaluateAsync(
            new ProjectSecurityContext(),
            [signal]);

        Assert.Empty(result.Findings);
    }
}
