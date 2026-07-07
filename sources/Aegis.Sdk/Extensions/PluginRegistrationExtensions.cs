using Aegis.Infrastructure.Exporters;
using Aegis.Sdk.Contracts;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Architecture.Models.Policies.Dependency;
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
/// Provides convenience methods for registering Aegis plugins into DI.
/// </summary>
public static class PluginRegistrationExtensions
{
    public static IServiceCollection AddAegisPlugin<TPlugin>(this IServiceCollection services)
        where TPlugin : class, IPlugin
    {
        services.AddSingleton<IPlugin, TPlugin>();
        return services;
    }

    public static IServiceCollection AddAegisReportExporter<TExporter>(this IServiceCollection services)
        where TExporter : class, IReportExporter
    {
        services.AddSingleton<IReportExporter, TExporter>();
        return services;
    }


    public static IServiceCollection AddAegisReportExporters(
    this IServiceCollection services)
    {
        services
            .AddAegisReportExporter<JsonReportExporter>()
            .AddAegisReportExporter<PdfReportExporter>()
            .AddAegisReportExporter<HtmlReportExporter>()
            .AddAegisReportExporter<MarkdownReportExporter>()
            .AddAegisReportExporter<ForensicPdfExporter>();
     

            

        return services;
    }

    /// <summary>
    /// Registers all Aegis policies using configuration binding.
    /// Explicitly routes paths through structural subfolders cleanly.
    /// </summary>
    public static IServiceCollection AddAegisPolicies(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        IConfiguration config = configuration ?? BuildPolicyConfiguration();

        services.Configure<ArchitecturePolicy>(config.GetSection("Architecture"));
        services.Configure<RepositoryPolicy>(config.GetSection("Repository"));
        services.Configure<SecurityPolicy>(config.GetSection("Security"));
        services.Configure<MaintainabilityPolicy>(config.GetSection("Maintainability"));
        services.Configure<DependencyPolicy>(config.GetSection("Dependency"));
        services.Configure<NamingPolicy>(config.GetSection("Naming"));
        services.Configure<ComplexityPolicy>(config.GetSection("Complexity"));
        services.Configure<CohesionPolicy>(config.GetSection("Cohesion"));
        services.Configure<CouplingPolicy>(config.GetSection("Coupling"));
        services.Configure<ApiConsistencyPolicy>(config.GetSection("ApiConsistency"));
        services.Configure<ErrorHandlingPolicy>(config.GetSection("ErrorHandling"));

        return services;
    }

    private static IConfiguration BuildPolicyConfiguration()
    {
        // Ground the configuration baseline to the running application binaries absolute root directory
        var rootDir = AppContext.BaseDirectory;

        if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
        {
            var location = Assembly.GetExecutingAssembly().Location;
            rootDir = Path.GetDirectoryName(location) ?? Directory.GetCurrentDirectory();
        }

        // Keep the configuration engine base path pinned safely to the execution root,
        // while specifying the precise relative directory inside the string path literal.
        return new ConfigurationBuilder()
            .SetBasePath(rootDir)
            .AddJsonFile(Path.Combine("config", "architecture.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "repository.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "security.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "maintainability.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "dependency.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "naming.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "complexity.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "cohesion.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "coupling.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "apiconsistency.policy.json"), optional: false, reloadOnChange: true)
            .AddJsonFile(Path.Combine("config", "errorhandling.policy.json"), optional: false, reloadOnChange: true)
            .Build();
    }
}