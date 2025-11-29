using Aegis.Security.Aggregation;
using Aegis.Security.Analysis;
using Aegis.Security.Evaluators.Application;
using Aegis.Security.Evaluators.Compliance;
using Aegis.Security.Evaluators.Dependency;
using Aegis.Security.Evaluators.Governance;
using Aegis.Security.Evaluators.IaC;
using Aegis.Security.Evaluators.Infrastructure;
using Aegis.Security.Evaluators.Network;
using Aegis.Security.Evaluators.Secrets;
using Aegis.Security.RuleEngine;
using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Models.Rules.Sets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Aegis.Security.Bootstrap;

public static class SecurityBootstrapper
{
    public static IServiceCollection AddAegisSecurity(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(SecurityProbeRunner).Assembly;
        // --------------------------------------------------------------------
        // PROBES
        // --------------------------------------------------------------------
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo<ISecurityProbe>())
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        // --------------------------------------------------------------------
        // CORE PIPELINE SERVICES
        // --------------------------------------------------------------------
        services.AddScoped<ISecurityProbeRunner, SecurityProbeRunner>();
        services.AddScoped<ISecurityProbeScheduler, SecurityRealtimeProbeRunner>();
        services.AddScoped<ISecurityAggregator, SecurityAggregator>();
        services.AddScoped<ISecurityEvaluatorEngine, SecurityEvaluatorEngine>();

        // --------------------------------------------------------------------
        // RULE SETS (1 per domain)
        // --------------------------------------------------------------------
        services.AddSingleton<ApplicationRuleSet>();
        services.AddSingleton<ComplianceRuleSet>();
        services.AddSingleton<DependencyRuleSet>();
        services.AddSingleton<GovernanceRuleSet>();
        services.AddSingleton<IaCRuleSet>();
        services.AddSingleton<InfrastructureRuleSet>();
        services.AddSingleton<NetworkRuleSet>();
        services.AddSingleton<SecretsRuleSet>();

        // --------------------------------------------------------------------
        // EVALUATORS (explicit > scanning)
        // --------------------------------------------------------------------
        services.AddScoped<ISecurityEvaluator, ApplicationSecurityEvaluator>();
        services.AddScoped<ISecurityEvaluator, ComplianceSecurityEvaluator>();
        services.AddScoped<ISecurityEvaluator, DependencySecurityEvaluator>();
        services.AddScoped<ISecurityEvaluator, GovernanceSecurityEvaluator>();
        services.AddScoped<ISecurityEvaluator, IaCSecurityEvaluator>();
        services.AddScoped<ISecurityEvaluator, InfrastructureSecurityEvaluator>();
        services.AddScoped<ISecurityEvaluator, NetworkSecurityEvaluator>();
        services.AddScoped<ISecurityEvaluator, SecretsSecurityEvaluator>();

        // --------------------------------------------------------------------
        // LOGGING
        // --------------------------------------------------------------------
        services.AddLogging();

        return services;
    }
}
