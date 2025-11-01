// Aegis.Shared/Security/Models/Policies/Network/NetworkSecurityPolicy.cs
using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Policies.Network;

public sealed class NetworkSecurityPolicy : AegisSecurityPolicy
{
    public override string Domain => "Network";

    public override IReadOnlyCollection<string> RuleSetKeys => new[]
    {
        "Network", "Infrastructure"
    };

    public override double DefaultRiskTolerance => 0.50;

    public override IReadOnlyDictionary<SecuritySeverity, double> SeverityThresholds =>
        new Dictionary<SecuritySeverity, double>
        {
            [SecuritySeverity.Critical] = 9.0,
            [SecuritySeverity.High] = 7.8,
            [SecuritySeverity.Medium] = 5.5,
            [SecuritySeverity.Low] = 3.5,
            [SecuritySeverity.Info] = 1.0
        };
}
