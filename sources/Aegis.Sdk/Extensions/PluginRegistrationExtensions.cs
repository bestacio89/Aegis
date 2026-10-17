using System;
using System.IO;
using System.Linq;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Architecture.Models.Policies.Dependency;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;
using Aegis.Shared.Architecture.Models.Policies.Naming;
using Aegis.Shared.Architecture.Models.Policies.Performance;
using Aegis.Shared.Architecture.Models.Policies.Persistence;
using Aegis.Shared.Contracts;
using Aegis.Sdk.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.SDK.Extensions;

/// <summary>
/// Registers Aegis plugins, report exporters, and typed policy configuration.
/// Policy files are loaded as independent documents and combined by their root sections.
/// </summary>
public static class PluginRegistrationExtensions
{
    private static readonly string[] PolicyFileNames =
    [
        "aegis.metadata.policy.json",
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
        "frontend.policy.json",
        "dependencygraph.policy.json",
        "node.policy.json",
        "circulardependency.policy.json",
        "configuration.policy.json",
        "logging.policy.json",
        "transaction.policy.json",
        "performance.policy.json",
        
    ];

    public static IServiceCollection AddAegisPlugin<TPlugin>(this IServiceCollection services)
        where TPlugin : class, IPlugin
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPlugin, TPlugin>();
        return services;
    }

    public static IServiceCollection AddAegisReportExporter<TExporter>(this IServiceCollection services)
        where TExporter : class, IReportExporter
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IReportExporter, TExporter>();
        return services;
    }

    /// <summary>
    /// Loads the split policy documents and optionally applies an additional
    /// configuration provider afterward, allowing environment/application values
    /// to override file-based policy values.
    /// </summary>
    public static IServiceCollection AddAegisPolicies(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var policyConfiguration = BuildPolicyConfiguration(configuration);
        RegisterPolicyOptions(services, policyConfiguration);

        return services;
    }

    private static void RegisterPolicyOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Aggregate policy consumed by the architecture evaluators.
        services.Configure<AegisArchitecturePolicy>(configuration);
        services.Configure<AegisArchitecturePolicy>(
            configuration.GetSection("AegisPolicyMetadata"));

        // Independently injectable policy models.
        services.Configure<ArchitecturePolicy>(configuration.GetSection("Architecture"));
        services.Configure<RepositoryPolicy>(configuration.GetSection("Repository"));
        services.Configure<SecurityPolicy>(configuration.GetSection("Security"));
        services.Configure<MaintainabilityPolicy>(configuration.GetSection("Maintainability"));
        services.Configure<DependencyPolicy>(configuration.GetSection("Dependency"));
        services.Configure<NamingPolicy>(configuration.GetSection("Naming"));
        services.Configure<ComplexityPolicy>(configuration.GetSection("Complexity"));
        services.Configure<CohesionPolicy>(configuration.GetSection("Cohesion"));
        services.Configure<CouplingPolicy>(configuration.GetSection("Coupling"));
        services.Configure<ApiConsistencyPolicy>(configuration.GetSection("ApiConsistency"));
        services.Configure<ErrorHandlingPolicy>(configuration.GetSection("ErrorHandling"));
        services.Configure<FrontendPolicy>(configuration.GetSection("Frontend"));
        services.Configure<DependencyGraphPolicy>(configuration.GetSection("DependencyGraph"));
        services.Configure<NodePolicy>(configuration.GetSection("Node"));
        services.Configure<CircularDependencyPolicy>(configuration.GetSection("CircularDependency"));
        services.Configure<ConfigurationPolicy>(configuration.GetSection("Configuration"));
        services.Configure<LoggingPolicy>(configuration.GetSection("Logging"));
        services.Configure<TransactionPolicy>(configuration.GetSection("Transaction"));
        services.Configure<PerformancePolicy>(configuration.GetSection("Performance"));
        services.Configure<DesignPatternPolicy>(configuration.GetSection("Architecture:DesignPatterns"));
    }

    private static IConfiguration BuildPolicyConfiguration(IConfiguration? overrides)
    {
        var configurationDirectory = ResolveConfigurationDirectory();
        var builder = new ConfigurationBuilder();

        if (configurationDirectory is not null)
        {
            builder.SetBasePath(configurationDirectory);

            foreach (var fileName in PolicyFileNames)
            {
                builder.AddJsonFile(fileName, optional: false, reloadOnChange: true);
            }
        }
        else if (overrides is null)
        {
            throw new DirectoryNotFoundException(
                "Aegis policy files were not found and no external configuration was supplied.");
        }

        if (overrides is not null)
        {
            builder.AddConfiguration(overrides);
        }

        return builder.Build();
    }

    /// <summary>
    /// In a source checkout, prefer the project's config directory so edits made
    /// by the WPF editor are the same files the policy providers monitor. In a
    /// published installation, use the copied config directory beside the app.
    /// </summary>
    private static string? ResolveConfigurationDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var configDirectory = Path.Combine(current.FullName, "config");
            var hasProjectFile = Directory.Exists(configDirectory) &&
                (File.Exists(Path.Combine(current.FullName, "Aegis.Wpf.csproj")) ||
                 File.Exists(Path.Combine(current.FullName, "Aegis.Cli.csproj")));

            if (hasProjectFile && PolicyFileNames.All(
                    fileName => File.Exists(Path.Combine(configDirectory, fileName))))
            {
                return configDirectory;
            }

            current = current.Parent;
        }

        var outputConfigDirectory = Path.Combine(AppContext.BaseDirectory, "config");
        if (PolicyFileNames.All(fileName =>
                File.Exists(Path.Combine(outputConfigDirectory, fileName))))
        {
            return outputConfigDirectory;
        }

        return null;
    }
}
