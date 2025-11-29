using Aegis.Security.Evaluators.Base;
using Aegis.Shared.Security.Models.Rules.Sets;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Evaluators.IaC;

public sealed class IaCSecurityEvaluator : BaseSecurityEvaluator
{
    public override string Domain => "IaC";

    public IaCSecurityEvaluator(
        ILogger<IaCSecurityEvaluator> logger,
        IaCRuleSet ruleSet)
        : base(logger, ruleSet.Rules)
    {
    }
}
