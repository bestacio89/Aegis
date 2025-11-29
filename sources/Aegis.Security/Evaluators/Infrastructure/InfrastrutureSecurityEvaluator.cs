using Aegis.Security.Evaluators.Base;
using Aegis.Shared.Security.Models.Rules.Sets;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Evaluators.Infrastructure;

public sealed class InfrastructureSecurityEvaluator : BaseSecurityEvaluator
{
    public override string Domain => "Infrastructure";

    public InfrastructureSecurityEvaluator(
        ILogger<InfrastructureSecurityEvaluator> logger,
        InfrastructureRuleSet ruleSet)
        : base(logger, ruleSet.Rules)
    {
    }
}
