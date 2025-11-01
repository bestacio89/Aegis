// Aegis.Shared/Security/Models/Policies/Application/ApplicationSecurityPolicy.cs
using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Policies.Application;

public sealed class ApplicationSecurityPolicy : AegisSecurityPolicy
{
    public override string Domain => "Application";

    public override IReadOnlyCollection<string> RuleSetKeys => new[]
    {
        "Application", "Compliance", "Secrets"
    };

    public override double DefaultRiskTolerance => 0.70;

    public override IReadOnlyDictionary<SecuritySeverity, double> SeverityThresholds =>
        new Dictionary<SecuritySeverity, double>
        {
            [SecuritySeverity.Critical] = 9.0,
            [SecuritySeverity.High] = 7.5,
            [SecuritySeverity.Medium] = 5.0,
            [SecuritySeverity.Low] = 3.0,
            [SecuritySeverity.Info] = 0.5
        };
}
