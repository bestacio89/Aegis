using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Live;

/// <summary>One observation made against a live target, with its evidence and control mapping.</summary>
public sealed record LiveProbeResult(
    string ProbeId,
    string ProbeName,
    string Target,
    DateTimeOffset ObservedAtUtc,
    SecuritySeverity Severity,
    string Title,
    string? Detail,
    EvidenceConfidence Confidence,
    string? ControlReference,
    string? Remediation)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}