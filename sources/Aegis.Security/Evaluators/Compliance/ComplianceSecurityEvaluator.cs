using Aegis.Security.Evaluators.Base;
using Aegis.Shared.Security.Models.Rules.Sets;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Evaluators.Compliance;

public sealed class ComplianceSecurityEvaluator : BaseSecurityEvaluator
{
    public override string Domain => "Compliance";

    public ComplianceSecurityEvaluator(
        ILogger<ComplianceSecurityEvaluator> logger,
        ComplianceRuleSet ruleSet)
        : base(logger, ruleSet.Rules)
    {
    }
}
