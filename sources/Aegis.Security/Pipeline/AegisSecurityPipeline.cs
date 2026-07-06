using Aegis.Security.Governance;
using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Pipeline;

public sealed class AegisSecurityPipeline : IAegisSecurityPipeline
{
    private readonly IEnumerable<ISecurityProbe> _probes;
    private readonly ISecurityEvaluatorEngine _evaluatorEngine;
    private readonly SecurityComplianceEngine _complianceEngine;
    private readonly ILogger<AegisSecurityPipeline> _logger;

    public AegisSecurityPipeline(
        IEnumerable<ISecurityProbe> probes,
        ISecurityEvaluatorEngine evaluatorEngine,
        SecurityComplianceEngine complianceEngine,
        ILogger<AegisSecurityPipeline> logger)
    {
        _probes = probes;
        _evaluatorEngine = evaluatorEngine;
        _complianceEngine = complianceEngine;
        _logger = logger;
    }

    public async Task<(AegisSecurityReport Report, SecurityComplianceResult Compliance)> RunAsync(
        ProjectSecurityContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Aegis security pipeline starting for {Project}", context.ProjectPath);

        var allSignals = new List<SecuritySignal>();

        foreach (var probe in _probes)
        {
            try
            {
                _logger.LogInformation("Running probe {ProbeId}", probe.Id);

                var signals = await probe.ExecuteAsync(context, ct);
                allSignals.AddRange(signals);

                _logger.LogInformation("Probe {ProbeId} produced {Count} signals",
                    probe.Id, signals.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Probe {ProbeId} failed", probe.Id);
            }
        }

        var evaluations = (await _evaluatorEngine
            .EvaluateAllAsync(context, allSignals, ct))
            .ToArray();

        // FIX 1: correct factory usage
        var summary = SecurityScanSummary.FromEvaluations(
            context.ProjectPath,
            evaluations,
            policyVersion: "v1.0",
            executedBy: context.Environment
        );

        var report = new AegisSecurityReport
        {
            ProjectName = context.ProjectPath,
            EngineVersion = "1.0.0-alpha",
            PolicyVersion = "v1.0",
            StartedAtUtc = context.ExecutedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Summary = summary,
            Metadata = new ProjectSecurityMetadata
            {
                Environment = context.Environment,
                Tags = context.Tags.Values.ToList()
            }
        };

        // FIX 2: correct compliance engine signature
        var compliance = _complianceEngine.Evaluate(
            report,
            evaluations,
            context.Environment,
            "v1.0"
        );

        _logger.LogInformation(
            "Aegis pipeline completed. Risk={Risk}, Compliance={Compliance:F2}%",
            compliance.GlobalRisk,
            compliance.ComplianceRate
        );

        return (report, compliance);
    }
}