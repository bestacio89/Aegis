using Aegis.Security.Bootstrap;
using Aegis.Security.Pipeline;
using Aegis.Wpf.ViewModels.Orchestrators;
using Aegis.Wpf.ViewModels.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Wpf.Services.Security;

public static class SecurityWorkspaceRegistration
{
    /// <summary>
    /// Registers the Security workspace UI. Prerequisite: AddAegisSecurity(configuration)
    /// (Aegis.Security.Bootstrap) must already be called so the pipeline and report builders exist.
    /// </summary>
    public static IServiceCollection AddAegisSecurityWorkspace(this IServiceCollection services)
    {
        services.AddSingleton<ISecurityScanService, SecurityScanService>();

        services.AddSingleton<SecurityOverviewViewModel>();
        services.AddSingleton<SecurityDomainsViewModel>();
        services.AddSingleton<SecurityFindingsViewModel>();
        services.AddSingleton<SecurityWorkspaceViewModel>();
        services.AddSingleton<SecurityWorkspaceOrchestrator>();
        services.AddSingleton<IAegisSecurityPipeline, AegisSecurityPipeline>();
 
        return services;
    }
}