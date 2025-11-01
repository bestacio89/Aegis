// Aegis.Shared/Security/Models/Policies/Infrastructure/InfrastructureSecurityPolicy.cs
using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Policies.Infrastructure;

public sealed class InfrastructureSecurityPolicy : AegisSecurityPolicy
{
    public override string Domain => "Infrastructure";

    public override IReadOnlyCollection<string> RuleSetKeys => new[]
    {
        "Infrastructure", "IaC", "Network"
    };

    public override double DefaultRiskTolerance => 0.65;

    public override IReadOnlyDictionary<SecuritySeverity, double> SeverityThresholds =>
        new Dictionary<SecuritySeverity, double>
        {
            [SecuritySeverity.Critical] = 8.8,
            [SecuritySeverity.High] = 7.2,
            [SecuritySeverity.Medium] = 5.5,
            [SecuritySeverity.Low] = 3.5,
            [SecuritySeverity.Info] = 1.0
        };
}
