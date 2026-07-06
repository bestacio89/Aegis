using Aegis.Cli.Commands;
using Aegis.Cli.Services;
using Aegis.Infrastructure.Extensions;
using Franz.Common.Logging.Extensions;
using Franz.Common.Logging.Tracing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.CommandLine;

TraceHelper.LogConsole();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddAegisInfrastructure(context.HostingEnvironment, context.Configuration);
        services.AddScoped<AnalyzeCommandService>();
    })
    .UseLog()
    .Build();

// ---------------- CLI ----------------

var root = new RootCommand("🧠 Aegis CLI — Deterministic Architecture Audit Tool");

var analyzeCmd = new Command("analyze", "Run a full Aegis analysis on a project");

var pathArg = new Argument<string>( 
     Directory.GetCurrentDirectory());

var policyOpt = new Option<string?>("--policy");
var jsonOpt = new Option<bool>("--json");

analyzeCmd.Arguments.Add(pathArg);
analyzeCmd.Options.Add(policyOpt);
analyzeCmd.Options.Add(jsonOpt);

analyzeCmd.SetAction(async (parseResult, cancellationToken) =>
{
    using var scope = host.Services.CreateScope();

    var svc = scope.ServiceProvider
        .GetRequiredService<AnalyzeCommandService>();

    var path = parseResult.GetValue(pathArg);
    var policy = parseResult.GetValue(policyOpt);
    var json = parseResult.GetValue(jsonOpt);

    await svc.RunAsync(path!, policy, json);
});

root.Subcommands.Add(analyzeCmd);
root.Add(analyzeCmd);