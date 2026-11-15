using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aegis.Architecture.Analysis.Semantics;

public static class KotlinSemanticAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddKotlinSemanticAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<KotlinWorkerOptions>(_ => { });
        services.TryAddSingleton<IJvmClasspathResolver, LocalJvmClasspathResolver>();
        services.TryAddSingleton<JvmSourceRootResolver>();
        services.TryAddSingleton<IKotlinWorkerClient, KotlinWorkerClient>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISemanticAnalysisAdapter, KotlinSemanticAnalysisAdapter>());
        services.TryAddSingleton<SemanticAnalysisCoordinator>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISemanticAnalysisProjectAnalyzer, KotlinSemanticAnalysisOrchestrator>());
        services.TryAddSingleton<SemanticAnalysisPipeline>();

        return services;
    }
}
