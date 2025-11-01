// Aegis.Shared/Security/Models/Policies/Dependency/DependencySecurityPolicy.cs
using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Policies.Dependency;

public sealed class DependencySecurityPolicy : AegisSecurityPolicy
{
    public override string Domain => "Dependency";

    public override IReadOnlyCollection<string> RuleSetKeys => new[]
    {
        "Dependency"
    };

    public override double DefaultRiskTolerance => 0.65;

    public override IReadOnlyDictionary<SecuritySeverity, double> SeverityThresholds =>
        new Dictionary<SecuritySeverity, double>
        {
            [SecuritySeverity.Critical] = 8.8,
            [SecuritySeverity.High] = 7.2,
            [SecuritySeverity.Medium] = 5.0,
            [SecuritySeverity.Low] = 3.0,
            [SecuritySeverity.Info] = 0.5
        };
}
