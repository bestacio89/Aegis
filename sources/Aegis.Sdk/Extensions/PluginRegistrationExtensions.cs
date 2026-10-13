
using Aegis.Infrastructure.Exporters;
using Aegis.Sdk.Contracts;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Architecture.Models.Policies.Dependency;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;
using Aegis.Shared.Architecture.Models.Policies.Naming;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Reflection;

namespace Aegis.SDK.Extensions;

/// <summary>
/// Provides convenience methods for registering Aegis plugins,
/// report exporters, and architecture policy configuration.
/// </summary>
public static class PluginRegistrationExtensions
{
    private const string ConfigurationDirectory = "config";

    /// <summary>
    /// Registers a plugin as a singleton.
    /// </summary>
    public static IServiceCollection AddAegisPlugin<TPlugin>(
        this IServiceCollection services)
        where TPlugin : class, IPlugin
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPlugin, TPlugin>();

        return services;
    }

    /// <summary>
    /// Registers a report exporter as a singleton.
    /// </summary>
    public static IServiceCollection AddAegisReportExporter<TExporter>(
        this IServiceCollection services)
        where TExporter : class, IReportExporter
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IReportExporter, TExporter>();

        return services;
    }

    /// <summary>
    /// Registers all supported architecture policy options.
    ///
    /// When a configuration instance is supplied, its values are used
    /// directly. Otherwise, the policy files are loaded from the
    /// application's configuration directory.
    /// </summary>
    public static IServiceCollection AddAegisPolicies(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var config = configuration ?? BuildPolicyConfiguration();

        RegisterPolicyOptions(services, config);

        return services;
    }

    /// <summary>
    /// Binds each typed policy to its corresponding configuration section.
    /// </summary>
    private static void RegisterPolicyOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ArchitecturePolicy>(
            configuration.GetSection("Architecture"));

        services.Configure<RepositoryPolicy>(
            configuration.GetSection("Repository"));

        services.Configure<SecurityPolicy>(
            configuration.GetSection("Security"));

        services.Configure<MaintainabilityPolicy>(
            configuration.GetSection("Maintainability"));

        services.Configure<DependencyPolicy>(
            configuration.GetSection("Dependency"));

        services.Configure<NamingPolicy>(
            configuration.GetSection("Naming"));

        services.Configure<ComplexityPolicy>(
            configuration.GetSection("Complexity"));

        services.Configure<CohesionPolicy>(
            configuration.GetSection("Cohesion"));

        services.Configure<CouplingPolicy>(
            configuration.GetSection("Coupling"));

        services.Configure<ApiConsistencyPolicy>(
            configuration.GetSection("ApiConsistency"));

        services.Configure<ErrorHandlingPolicy>(
            configuration.GetSection("ErrorHandling"));

        services.Configure<FrontendPolicy>(
            configuration.GetSection("Frontend"));
    }

    /// <summary>
    /// Builds configuration from the policy files distributed with
    /// the running application.
    /// </summary>
    private static IConfiguration BuildPolicyConfiguration()
    {
        var rootDirectory = ResolveApplicationDirectory();

        var builder = new ConfigurationBuilder()
            .SetBasePath(rootDirectory);

        foreach (var fileName in GetPolicyFileNames())
        {
            var relativePath = Path.Combine(
                ConfigurationDirectory,
                fileName);

            builder.AddJsonFile(
                relativePath,
                optional: false,
                reloadOnChange: true);
        }

        return builder.Build();
    }

    /// <summary>
    /// Resolves the directory used as the base for distributed
    /// configuration files.
    /// </summary>
    private static string ResolveApplicationDirectory()
    {
        var applicationDirectory = AppContext.BaseDirectory;

        if (!string.IsNullOrWhiteSpace(applicationDirectory) &&
            Directory.Exists(applicationDirectory))
        {
            return applicationDirectory;
        }

        var assemblyLocation = Assembly.GetExecutingAssembly().Location;

        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);

        if (!string.IsNullOrWhiteSpace(assemblyDirectory) &&
            Directory.Exists(assemblyDirectory))
        {
            return assemblyDirectory;
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Lists the policy files required by the current registration model.
    /// </summary>
    private static string[] GetPolicyFileNames()
    {
        return
        [
            "architecture.policy.json",
            "repository.policy.json",
            "security.policy.json",
            "maintainability.policy.json",
            "dependency.policy.json",
            "naming.policy.json",
            "complexity.policy.json",
            "cohesion.policy.json",
            "coupling.policy.json",
            "apiconsistency.policy.json",
            "errorhandling.policy.json",
            "frontend.policy.json"
        ];
    }
}
