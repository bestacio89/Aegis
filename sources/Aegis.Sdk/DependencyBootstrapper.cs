using Microsoft.Extensions.DependencyInjection;
using Franz.Common.Logging;
using Aegis.Core.Architecture.Evaluators;
using Aegis.Core.Architecture.RuleEngines;

namespace Aegis.Sdk;

public static class DependencyBootstrapper
{
    public static void Configure(IServiceCollection services)
    {
       
        // Core components
        services.AddScoped<RuleEngine>();
        services.AddScoped<RuleEngineCore>();
        services.AddScoped<AegisArchitectureAnalysisRunner>();
        // Evaluators
        services.Scan(scan => scan
            .FromAssembliesOf(typeof(BaseArchitectureEvaluator))
            .AddClasses(classes => classes.AssignableTo<BaseArchitectureEvaluator>())
            .AsImplementedInterfaces()
            .WithScopedLifetime());
    }
}
