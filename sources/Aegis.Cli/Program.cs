using Aegis.Cli.Commands;
using Aegis.Infrastructure.Extensions;
using Franz.Common.Logging.Tracing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using Aegis.Cli.Services;
using Franz.Common.Logging.Extensions;

TraceHelper.LogConsole();

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        var env = context.HostingEnvironment;
        var cfg = context.Configuration;

        // 👇 Add Aegis Infrastructure & Franz DI bootstrappers
        services.AddAegisInfrastructure(env, cfg);

        // Register your command services
        services.AddScoped<AnalyzeCommandService>();
    })
    .UseLog()
    .Build();

// --- CLI setup ---
var root = new RootCommand("🧠 Aegis CLI — Deterministic Architecture Audit Tool");
var analyzeCmd = new Command("analyze", "Run a full Aegis analysis on a project")
{
    new Argument<string>("path", () => Directory.GetCurrentDirectory(), "Project root path"),
    new Option<string?>("--policy", "Optional policy JSON path"),
    new Option<bool>("--json", "Export results as JSON report")
};

analyzeCmd.SetHandler(async (string path, string? policy, bool json) =>
{
    var scope = builder.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<AnalyzeCommandService>();
    await svc.RunAsync(path, policy, json);
},
    (System.CommandLine.Binding.IValueDescriptor<string>)analyzeCmd.Arguments[0],
    (System.CommandLine.Binding.IValueDescriptor<string>)analyzeCmd.Options[0],
    (System.CommandLine.Binding.IValueDescriptor<bool>)analyzeCmd.Options[1]);

root.AddCommand(analyzeCmd);
return await root.InvokeAsync(args);
