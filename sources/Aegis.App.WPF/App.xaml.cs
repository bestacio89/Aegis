using Aegis.App.Wpf.ViewModels;
using Aegis.Architecture.RuleEngines;
using Aegis.Infrastructure.Extensions;
using Aegis.Infrastructure.Persistence;
using Aegis.Sdk;
using Franz.Common.Logging.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace Aegis.App.Wpf;

public partial class App : Application
{
    public static IHost Host { get; private set; } = default!;

    public App()
    {
        Host = CreateHost();
    }

    private static IHost CreateHost()
    {
        // -------------------------------
        // STEP 1: build configuration manually (NO HOST YET)
        // -------------------------------
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("config/appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();

        var configuration = configBuilder.Build();

        // -------------------------------
        // STEP 2: temporary logger factory
        // -------------------------------
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddConsole();
            logging.AddDebug();
        });

        var logger = loggerFactory.CreateLogger("DatabaseBootstrap");

        // -------------------------------
        // STEP 3: DB detection
        // -------------------------------
        DatabaseDetector.DetectOrRepairDatabaseConfig(configuration, logger);

        // -------------------------------
        // STEP 4: build real host
        // -------------------------------
        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseLog()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddConfiguration(configuration);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddAegisInfrastructure(
                    context.HostingEnvironment,
                    context.Configuration);

                services.AddScoped<RuleEngine>();
                services.AddScoped<AegisArchitectureAnalysisRunner>();

                services.AddSingleton<MainViewModel>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await Host.StartAsync();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await Host.StopAsync();
        Host.Dispose();
        base.OnExit(e);
    }
}