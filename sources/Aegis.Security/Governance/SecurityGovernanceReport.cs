using Aegis.Shared.Security.Enums;

namespace Aegis.Security.Governance;

public sealed class GovernanceReport
{
    public double FinalScore { get; init; }
    public RiskLevel OverallRisk { get; init; }

    public int Critical { get; init; }
    public int High { get; init; }
    public int Medium { get; init; }

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public override string ToString()
    {
        return $"Aegis Governance Report\n" +
               $"------------------------\n" +
               $"Final Score: {FinalScore:F2}/10\n" +
               $"Overall Risk: {OverallRisk}\n" +
               $"Critical: {Critical}, High: {High}, Medium: {Medium}\n" +
               $"Generated: {GeneratedAt:u}";
    }
}
