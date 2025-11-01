// Aegis.Shared/Security/Models/Policies/Governance/GovernanceSecurityPolicy.cs
using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Policies.Governance;

public sealed class GovernanceSecurityPolicy : AegisSecurityPolicy
{
    public override string Domain => "Governance";

    public override IReadOnlyCollection<string> RuleSetKeys => new[]
    {
        "Governance", "Compliance"
    };

    public override double DefaultRiskTolerance => 0.60;

    public override IReadOnlyDictionary<SecuritySeverity, double> SeverityThresholds =>
        new Dictionary<SecuritySeverity, double>
        {
            [SecuritySeverity.Critical] = 9.5,
            [SecuritySeverity.High] = 8.0,
            [SecuritySeverity.Medium] = 6.0,
            [SecuritySeverity.Low] = 3.5,
            [SecuritySeverity.Info] = 1.0
        };
}
