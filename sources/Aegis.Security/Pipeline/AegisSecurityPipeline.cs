using Aegis.Security.Governance;
using Aegis.Security.RuleEngine;
using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Pipeline;

/// <summary>
/// Static security pipeline: runs only Static probes/evaluators (see <see cref="SecurityModeCatalog"/>).
/// Live probing (handshakes, ports, host state, cloud APIs) is a separate concern with its own
/// authorization gate and is never executed from here.
/// </summary>
public sealed class AegisSecurityPipeline : IAegisSecurityPipeline
{
    private const string EngineVersion = "1.0.0-alpha";
    private const string PolicyVersion = "v1.0";

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
        _logger.LogInformation("Aegis static security pipeline starting for {Project}", context.ProjectPath);

        var allProbes = _probes.ToList();
        var staticProbes = allProbes
            .Where(p => SecurityModeCatalog.ModeOfProbe(p.Id) == ProbeMode.Static)
            .ToList();

        var skipped = allProbes.Count - staticProbes.Count;
        if (skipped > 0)
        {
            _logger.LogInformation(
                "Skipped {Count} live probe(s); live probing runs only through the live pipeline with an authorized scope.",
                skipped);
        }

        var allSignals = new List<SecuritySignal>();

        foreach (var probe in staticProbes)
        {
            try
            {
                _logger.LogInformation("Running probe {ProbeId}", probe.Id);

                var signals = (await probe.ExecuteAsync(context, ct)).ToList();
                allSignals.AddRange(signals);

                _logger.LogInformation("Probe {ProbeId} produced {Count} signals", probe.Id, signals.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Probe {ProbeId} failed", probe.Id);
            }
        }

        if (allSignals.Count == 0)
        {
            // An empty scan is indistinguishable from a clean one in the compliance numbers,
            // so say it loudly instead of letting it pass as 100%.
            _logger.LogWarning(
                "No static evidence was collected for {Project} ({Probes} static probe(s) ran). " +
                "Compliance figures for this run reflect missing evidence, not a clean result.",
                context.ProjectPath, staticProbes.Count);
        }

        var evaluations = (await _evaluatorEngine
            .EvaluateAllAsync(context, allSignals, ProbeMode.Static, ct))
            .ToArray();

        var summary = SecurityScanSummary.FromEvaluations(
            context.ProjectPath,
            evaluations,
            policyVersion: PolicyVersion,
            executedBy: context.Environment);

        var report = new AegisSecurityReport
        {
            ProjectName = context.ProjectPath,
            EngineVersion = EngineVersion,
            PolicyVersion = PolicyVersion,
            StartedAtUtc = context.ExecutedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Summary = summary,
            Metadata = new ProjectSecurityMetadata
            {
                Environment = context.Environment,
                Tags = BuildTags(context)
            }
        };

        // Named arguments: the signature is (report, evaluations, policyVersion, evaluatedBy).
        var compliance = _complianceEngine.Evaluate(
            report,
            evaluations,
            policyVersion: PolicyVersion,
            evaluatedBy: context.Environment);

        _logger.LogInformation(
            "Aegis static security pipeline completed. Risk={Risk}, Compliance={Compliance:F2}%",
            compliance.GlobalRisk,
            compliance.ComplianceRate);

        return (report, compliance);
    }

    // The metadata keeps tags as a flat string collection; keep the keys, not only the values.
    private static IReadOnlyCollection<string> BuildTags(ProjectSecurityContext context)
    {
        var tags = context.Tags.Select(t => $"{t.Key}={t.Value}").ToList();

        if (!string.IsNullOrWhiteSpace(context.PipelineRunId))
            tags.Add($"PipelineRunId={context.PipelineRunId}");

        return tags;
    }
}