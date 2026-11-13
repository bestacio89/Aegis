using Microsoft.Extensions.DependencyInjection;
using Franz.Common.Logging;
using Aegis.Architecture.Evaluators;
using Aegis.Architecture.Analysis.Semantics;
using Aegis.Architecture.RuleEngines;

namespace Aegis.Sdk;

public static class DependencyBootstrapper
{
    public static void Configure(IServiceCollection services)
    {
       
        // Core components
        services.AddJavaSemanticAnalysis();
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
