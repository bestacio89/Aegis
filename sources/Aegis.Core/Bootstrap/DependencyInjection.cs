using Aegis.Architecture.Aggregation;
using Aegis.Architecture.RuleEngines;
using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Franz.Common.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Bootstrap;

public static class DependencyInjection
{
    public static IServiceCollection AddAegisCore(this IServiceCollection services, ILogger? logger = null)
    {
        // Register all internal scoped dependencies following Franz model
        services.AddScopedDependencies(a => a.FullName?.StartsWith("Aegis") ?? false);

        services.AddSingleton<RuleEngine>();

        // Safe to always call: this only fills in whatever Franz's convention
        // scan above missed, it never re-registers something already present.
        services.AddMissingArchitectureEvaluators();
        services.AddSingleton<RuleEngineCore>();
        services.AddSingleton<RuleWeightingEngine>();
        services.AddSingleton<CrossEvaluatorAggregator>();
        services.AddSingleton<RuleEngine>();

        return services;
    }


    /// <summary>
    /// Failsafe for evaluator registration. Diffs the concrete <see cref="IEvaluator"/>
    /// implementations found by reflection against what's already registered
    /// (presumably by Franz's convention scan above) and registers only the
    /// ones that are missing — so this is safe to always call, unlike a blind
    /// "register everything" pass, which would double-register anything Franz
    /// already caught and double every fact/violation count as a result.
    /// </summary>
    public static IServiceCollection AddMissingArchitectureEvaluators(this IServiceCollection services)
    {
        var alreadyRegistered = services
            .Where(d => d.ServiceType == typeof(IEvaluator))
            .Select(d => d.ImplementationType)
            .Where(t => t is not null)
            .ToHashSet();

        var allEvaluatorTypes = typeof(RuleEngine).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IEvaluator).IsAssignableFrom(t));

        var missing = allEvaluatorTypes.Where(t => !alreadyRegistered.Contains(t)).ToList();

        foreach (var type in missing)
        {
            services.AddScoped(typeof(IEvaluator), type);
        }

        return services;
    }


}