using Aegis.Sdk.Contracts;
using Aegis.SDK.RuleContracts;
using Aegis.Shared.Contracts;
using Franz.Common.DependencyInjection;
using Franz.Common.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Aegis.Sdk;

/// <summary>
/// Discovers and registers Aegis plugin components (scanners, rules, writers, etc.)
/// from both loaded and external assemblies.
/// </summary>
public static class PluginRegistrar
{
    public static IServiceCollection AddAegisPlugins(
        this IServiceCollection services,
        ILogger? logger = null,
        Func<Assembly, bool>? assemblyFilter = null)
    {
        // 1️⃣ Load all Aegis assemblies or apply filter if provided.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic &&
                        (assemblyFilter?.Invoke(a) ??
                         a.FullName!.StartsWith("Aegis", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        logger?.LogInformation("🔍 Scanning {Count} assemblies for Aegis plugins...", assemblies.Count);

        // 2️⃣ Register using Scrutor and Franz conventions.
        services.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(c => c.AssignableTo<IEvaluator>())
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(c => c.AssignableTo<IRule>())
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(c => c.AssignableTo<IReportWriter>())
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(c => c.AssignableTo<IPlugin>())
                .AsImplementedInterfaces()
                .WithSingletonLifetime()
        );

        // 3️⃣ Optionally register any other IScopedDependency automatically (Franz model).
        services.AddScopedDependencies(a => a.FullName?.StartsWith("Aegis") ?? false);

        logger?.LogInformation("✅ Plugin discovery complete — {ServiceCount} services registered.",
            services.Count);

        return services;
    }

    /// <summary>
    /// Loads external plugin DLLs from the specified path before registration.
    /// </summary>
    public static void LoadExternalPlugins(string pluginPath, ILogger? logger = null)
    {
        if (!Directory.Exists(pluginPath))
        {
            logger?.LogWarning("Plugin directory '{Path}' not found.", pluginPath);
            return;
        }

        var dlls = Directory.GetFiles(pluginPath, "*.dll", SearchOption.AllDirectories);
        foreach (var dll in dlls)
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                logger?.LogInformation("📦 Loaded plugin assembly: {Name}", assembly.GetName().Name);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "❌ Failed to load plugin {Dll}", dll);
            }
        }
    }
}
