using Aegis.Core.Architecture.RuleEngines;
using Franz.Common.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Core.Bootstrap;

public static class DependencyInjection
{
    public static IServiceCollection AddAegisCore(this IServiceCollection services, ILogger? logger = null)
    {
        // Register all internal scoped dependencies following Franz model
        services.AddScopedDependencies(a => a.FullName?.StartsWith("Aegis") ?? false);

        // Register all discovered plugins (scanners, rules, writers)
        
        services.AddSingleton<RuleEngine>();

        return services;
    }
}
