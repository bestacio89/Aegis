using Aegis.Security.Evaluators.Base;
using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Models;
using Aegis.Shared.Security.Models.Rules.Sets;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Evaluators.Application;

public sealed class ApplicationSecurityEvaluator : BaseSecurityEvaluator
{
    public override string Domain => "Application";

    public ApplicationSecurityEvaluator(
        ILogger<ApplicationSecurityEvaluator> logger,
        ApplicationRuleSet ruleSet)
        : base(logger, ruleSet.Rules)
    {
    }
}
