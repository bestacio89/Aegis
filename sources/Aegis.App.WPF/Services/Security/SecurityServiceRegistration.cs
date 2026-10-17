using Aegis.Security.Bootstrap;
using Aegis.Wpf.ViewModels.Orchestrators;
using Aegis.Wpf.ViewModels.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Wpf.Services.Security;

public static class SecurityWorkspaceRegistration
{
    /// <summary>
    /// Registers the Security workspace UI (static assessment and live probing).
    /// Prerequisite: AddAegisSecurity(configuration) (Aegis.Security.Bootstrap) must already be called:
    /// it registers the static pipeline, the compliance engine and the report builders.
    /// </summary>
    public static IServiceCollection AddAegisSecurityWorkspace(this IServiceCollection services)
    {
        // Static assessment
        services.AddSingleton<ISecurityScanService, SecurityScanService>();

        // Live probing: the gate, the pipeline and the probes live in Aegis.Security.
        services.AddAegisLiveProbing();
        services.AddSingleton<ILiveScanService, LiveScanService>();

        services.AddSingleton<SecurityOverviewViewModel>();
        services.AddSingleton<SecurityDomainsViewModel>();
        services.AddSingleton<SecurityFindingsViewModel>();
        services.AddSingleton<SecurityLiveViewModel>();
        services.AddSingleton<SecurityWorkspaceViewModel>();
        services.AddSingleton<SecurityWorkspaceOrchestrator>();

        return services;
    }
}