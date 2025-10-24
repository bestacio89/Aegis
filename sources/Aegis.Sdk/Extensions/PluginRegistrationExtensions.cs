using Aegis.Sdk.Contracts;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

 

    /// <summary>
    /// Registers all Aegis policies using configuration binding.
    /// Attempts to auto-load aegis.policy.json if present.
    /// </summary>
    public static IServiceCollection AddAegisPolicies(this IServiceCollection services, IConfiguration? config = null)
    {
        IConfiguration configuration = config ?? BuildLocalConfiguration();

        var aegisSection = configuration.GetSection("AegisPolicy");
        if (!aegisSection.Exists())
            throw new FileNotFoundException("Could not find the 'AegisPolicy' section in configuration or aegis.policy.json.");

        services.Configure<AegisArchitecturePolicy>(aegisSection);
        return services;
    }

    private static IConfiguration BuildLocalConfiguration()
    {
        var builder = new ConfigurationBuilder();

        // Search for aegis.policy.json in likely locations
        var basePath = Directory.GetCurrentDirectory();
        var filePath = Path.Combine(basePath, "aegis.policy.json");

        if (!File.Exists(filePath))
        {
            // Look one level up (e.g., when running from bin/Debug)
            var parentPath = Directory.GetParent(basePath)?.FullName;
            var parentFile = Path.Combine(parentPath ?? basePath, "aegis.policy.json");

            if (File.Exists(parentFile))
                filePath = parentFile;
        }

        if (File.Exists(filePath))
        {
            builder.SetBasePath(Path.GetDirectoryName(filePath)!)
                   .AddJsonFile(Path.GetFileName(filePath), optional: false, reloadOnChange: true);
        }
        else
        {
            throw new FileNotFoundException($"Aegis policy file not found at: {filePath}");
        }

        return builder.Build();
    }
}
