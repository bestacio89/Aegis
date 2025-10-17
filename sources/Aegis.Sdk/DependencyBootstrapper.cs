using Aegis.Core.RuleEngines;
using Aegis.Core.Evaluators;
using Microsoft.Extensions.DependencyInjection;
using Franz.Common.Logging;

namespace Aegis.Sdk;

public static class DependencyBootstrapper
{
    public static void Configure(IServiceCollection services)
    {
       
        // Core components
        services.AddScoped<RuleEngine>();
        services.AddScoped<RuleEngineCore>();
        services.AddScoped<AegisRunner>();
        // Evaluators
        services.Scan(scan => scan
            .FromAssembliesOf(typeof(BaseEvaluator))
            .AddClasses(classes => classes.AssignableTo<BaseEvaluator>())
            .AsImplementedInterfaces()
            .WithScopedLifetime());
    }
}
