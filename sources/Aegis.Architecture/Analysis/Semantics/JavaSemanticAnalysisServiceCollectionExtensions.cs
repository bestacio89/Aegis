using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aegis.Architecture.Analysis.Semantics;

public static class JavaSemanticAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddJavaSemanticAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<JavaWorkerOptions>(_ => { });
        services.TryAddSingleton<IJvmClasspathResolver, LocalJvmClasspathResolver>();
        services.TryAddSingleton<JvmSourceRootResolver>();
        services.TryAddSingleton<IJavaWorkerClient, JavaWorkerClient>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISemanticAnalysisAdapter, JavaSemanticAnalysisAdapter>());
        services.TryAddSingleton<SemanticAnalysisCoordinator>();
        services.TryAddSingleton<JavaSemanticAnalysisOrchestrator>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISemanticAnalysisProjectAnalyzer, JavaSemanticAnalysisOrchestrator>());
        services.TryAddSingleton<SemanticAnalysisPipeline>();

        return services;
    }
}
