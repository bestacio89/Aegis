// Aegis.Shared/Security/Models/Policies/Secrets/SecretsSecurityPolicy.cs
using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Policies.Secrets;

public sealed class SecretsSecurityPolicy : AegisSecurityPolicy
{
    public override string Domain => "Secrets";

    public override IReadOnlyCollection<string> RuleSetKeys => new[]
    {
        "Secrets", "Application"
    };

    public override double DefaultRiskTolerance => 0.45;

    public override IReadOnlyDictionary<SecuritySeverity, double> SeverityThresholds =>
        new Dictionary<SecuritySeverity, double>
        {
            [SecuritySeverity.Critical] = 9.0,
            [SecuritySeverity.High] = 7.8,
            [SecuritySeverity.Medium] = 5.0,
            [SecuritySeverity.Low] = 3.0,
            [SecuritySeverity.Info] = 0.5
        };
}
