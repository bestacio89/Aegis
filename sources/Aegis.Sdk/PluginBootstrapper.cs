using Aegis.Sdk.Contracts;
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
        ArgumentNullException.ThrowIfNull(services);

        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic &&
                (assemblyFilter?.Invoke(assembly) ??
                 (assembly.FullName?.StartsWith("Aegis", StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToList();

        logger?.LogInformation(
            "Scanning {AssemblyCount} assemblies for Aegis plugins.",
            assemblies.Count);

        services.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(classes => classes.AssignableTo<IEvaluator>())
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo<IReportExporter>())
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo<IPlugin>())
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        services.AddScopedDependencies(
            assembly => assembly.FullName?.StartsWith("Aegis", StringComparison.OrdinalIgnoreCase) ?? false);

        logger?.LogInformation(
            "Plugin discovery complete. {ServiceCount} service descriptors registered.",
            services.Count);

        return services;
    }

    /// <summary>
    /// Loads external plugin DLLs from the specified path before registration.
    /// </summary>
    public static void LoadExternalPlugins(string pluginPath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);

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
                logger?.LogInformation("Loaded plugin assembly: {Name}", assembly.GetName().Name);
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Failed to load plugin assembly from {Dll}.", dll);
            }
        }
    }
}