using Aegis.Architecture.Aggregation;
using Aegis.Architecture.RuleEngines;
using Aegis.Architecture.Scoring;
using Aegis.Cli.Services;
using Aegis.Infrastructure.Extensions;
using Aegis.Sdk;
using Aegis.SDK.Extensions;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Franz.Common.Logging.Extensions;
using Franz.Common.Logging.Tracing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.CommandLine;
using System.IO;

TraceHelper.LogConsole();


// --------------------------------------------------------
// BUILD APPLICATION HOST & DI CONTAINER
// --------------------------------------------------------

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(AppContext.BaseDirectory)
              .AddJsonFile(
                  "config/appsettings.json",
                  optional: false,
                  reloadOnChange: true)
              .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddAegisInfrastructure(
            context.HostingEnvironment,
            context.Configuration);

        services.AddAegisPolicies(context.Configuration);


        // Policy bridges

        services.AddTransient<AegisArchitecturePolicy>(sp =>
            sp.GetRequiredService<IOptions<AegisArchitecturePolicy>>().Value);

        services.AddTransient<ArchitecturePolicy>(sp =>
            sp.GetRequiredService<IOptions<ArchitecturePolicy>>().Value);


        // Core engine

        services.AddScoped<CrossEvaluatorAggregator>();
        services.AddScoped<RuleWeightingEngine>();
        services.AddScoped<RuleEngine>();
        services.AddScoped<RuleEngineCore>();

        services.AddScoped<AegisArchitectureAnalysisRunner>();


        // CLI services

        services.AddScoped<AnalyzeCommandService>();
    })
    .UseLog()
    .Build();


// --------------------------------------------------------
// COMMAND LINE INTERFACE
// --------------------------------------------------------

var root = new RootCommand(
    "🧠 Aegis CLI — Deterministic Architecture Audit Tool");


var analyzeCmd = new Command(
    "analyze",
    "Run a full Aegis analysis on a project");


var pathArg = new Argument<string>("path")
{
    Description = "Project path to analyze"
};

pathArg.DefaultValueFactory = _ =>
    Directory.GetCurrentDirectory();


var policyOpt = new Option<string?>(
    "--policy",
    "Optional Aegis policy file path");


analyzeCmd.Arguments.Add(pathArg);
analyzeCmd.Options.Add(policyOpt);


analyzeCmd.SetAction(async (parseResult, cancellationToken) =>
{
    await using var scope = host.Services.CreateAsyncScope();

    var service = scope.ServiceProvider
        .GetRequiredService<AnalyzeCommandService>();


    var path = parseResult.GetValue(pathArg);

    if (string.IsNullOrWhiteSpace(path))
    {
        return 1;
    }


    var policy = parseResult.GetValue(policyOpt);


    var result = await service.RunAsync(
        projectPath: path,
        policyPath: policy
        );


    if (result is null)
    {
        return 1;
    }


    return result.Success ? 0 : 1;
});


root.Subcommands.Add(analyzeCmd);


return await root.Parse(args).InvokeAsync();