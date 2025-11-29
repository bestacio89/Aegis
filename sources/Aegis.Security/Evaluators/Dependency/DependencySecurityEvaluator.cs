using Aegis.Security.Evaluators.Base;
using Aegis.Shared.Security.Models.Rules.Sets;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Evaluators.Dependency;

public sealed class DependencySecurityEvaluator : BaseSecurityEvaluator
{
    public override string Domain => "Dependency";

    public DependencySecurityEvaluator(
        ILogger<DependencySecurityEvaluator> logger,
        DependencyRuleSet ruleSet)
        : base(logger, ruleSet.Rules)
    {
    }
}
