using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aegis.Architecture.Analysis.Semantics;

public static class JavaSemanticAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddJavaSemanticAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<JavaWorkerOptions>(_ => { });
        services.TryAddSingleton<IJavaClasspathResolver, LocalJavaClasspathResolver>();
        services.TryAddSingleton<IJavaWorkerClient, JavaWorkerClient>();
        services.TryAddSingleton<ISemanticAnalysisAdapter, JavaSemanticAnalysisAdapter>();
        services.TryAddSingleton<JavaSemanticAnalysisOrchestrator>();

        return services;
    }
}
