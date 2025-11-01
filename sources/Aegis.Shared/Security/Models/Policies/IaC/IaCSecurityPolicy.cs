// Aegis.Shared/Security/Models/Policies/IaC/IaCSecurityPolicy.cs
using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Policies.IaC;

public sealed class IaCSecurityPolicy : AegisSecurityPolicy
{
    public override string Domain => "IaC";

    public override IReadOnlyCollection<string> RuleSetKeys => new[]
    {
        "IaC", "Infrastructure", "Network"
    };

    public override double DefaultRiskTolerance => 0.55;

    public override IReadOnlyDictionary<SecuritySeverity, double> SeverityThresholds =>
        new Dictionary<SecuritySeverity, double>
        {
            [SecuritySeverity.Critical] = 9.0,
            [SecuritySeverity.High] = 7.5,
            [SecuritySeverity.Medium] = 5.5,
            [SecuritySeverity.Low] = 3.5,
            [SecuritySeverity.Info] = 1.0
        };
}
