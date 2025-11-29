using Aegis.Security.Evaluators.Base;
using Aegis.Shared.Security.Models.Rules.Sets;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Evaluators.Secrets;

public sealed class SecretsSecurityEvaluator : BaseSecurityEvaluator
{
    public override string Domain => "Secrets";

    public SecretsSecurityEvaluator(
        ILogger<SecretsSecurityEvaluator> logger,
        SecretsRuleSet ruleSet)
        : base(logger, ruleSet.Rules)
    {
    }
}
