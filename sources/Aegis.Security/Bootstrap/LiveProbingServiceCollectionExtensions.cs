using Aegis.Security.Live;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aegis.Security.Bootstrap;

public static class LiveProbingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the live-probing pipeline, its scope gate and the available live probes.
    /// Independent from <c>AddAegisSecurity</c>: the static pipeline never executes anything registered here.
    /// </summary>
    public static IServiceCollection AddAegisLiveProbing(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<ILiveScopeValidator, LiveScopeValidator>();

        // Each live probe is registered here and only here; add new ones alongside this line.
        services.AddScoped<ILiveProbe, TlsLiveProbe>();

        services.AddScoped<ILiveProbePipeline, AegisLiveProbePipeline>();

        return services;
    }
}