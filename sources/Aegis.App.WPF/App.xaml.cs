using Aegis.App.Wpf.Services.Abstractions;
using Aegis.App.Wpf.Services.NewFolder;
using Aegis.App.Wpf.ViewModels;
using Aegis.App.Wpf.Views;
using Aegis.Architecture.Aggregation;
using Aegis.Architecture.Bootstrap;
using Aegis.Architecture.RuleEngines;
using Aegis.Architecture.Scoring;
using Aegis.Infrastructure.Aggregation;
using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Extensions;
using Aegis.Infrastructure.Persistence;
using Aegis.Sdk;
using Aegis.SDK.Extensions;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Franz.Common.Logging.Extensions;
using Franz.Common.Mediator.Bootstrap;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
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

                // Registers IOptions wrapper hierarchies 
                services.AddAegisPolicies();

                // ========================================================
                // BRIDGE OPTIONS TO DIRECT INJECTION FOR RULE ENGINE
                // ========================================================
                services.AddTransient<AegisArchitecturePolicy>(sp =>
                    sp.GetRequiredService<IOptions<AegisArchitecturePolicy>>().Value);

                services.AddTransient<ArchitecturePolicy>(sp =>
                    sp.GetRequiredService<IOptions<ArchitecturePolicy>>().Value);

                // ========================================================
                // ENGINE ARCHITECTURE REGISTRATIONS
                // ========================================================
                services.AddAegisCore();
                services.AddScoped<AegisArchitectureAnalysisRunner>();
                services.AddSingleton<LayerAggregator>();

               
                services.AddFranzMediatorStandard(new[] { typeof(AegisDbContext).Assembly });

                // =========================
                // VIEW MODELS
                // =========================
                services.AddSingleton<MainViewModel>();

                services.AddTransient<LayerDashboardViewModel>();
                services.AddTransient<SectionDashboardViewModel>();
                services.AddTransient<RuleDashboardViewModel>();
                services.AddTransient<ReportVisualizationViewModel>();
                services.AddTransient<ILayerAnalysisService, LayerAnalysisService>();

                // =========================
                // VIEWS
                // =========================
                services.AddSingleton<MainWindow>();
                services.AddTransient<ReportVisualizationView>();
                services.AddTransient<LayerDashboardView>();
                services.AddTransient<SectionDashboardView>();
                services.AddTransient<RuleDashboardView>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await Host.StartAsync();

        // Run database migrations and execution schemas inside an isolated initialization scope
        using (var scope = Host.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var appLogger = services.GetRequiredService<ILogger<App>>();

            try
            {
                appLogger.LogInformation("[INIT] Resolving database infrastructure targets for schema synchronization...");
                var dbContext = services.GetRequiredService<AegisDbContext>();

                // Ensures physical DB creation and executes all pending EF Core structural migrations
                await dbContext.Database.MigrateAsync();
                appLogger.LogInformation("[INIT] Database schema alignment verified successfully.");
            }
            catch (Exception ex)
            {
                appLogger.LogCritical(ex, "[FATAL] Critical failure occurred during storage infrastructure migration routing.");
                MessageBox.Show(
                    "A critical exception occurred while orchestrating structural backend stores. Application execution aborted.",
                    "Database Migration Failure",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
                return;
            }
        }

        var mainWindow = Host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await Host.StopAsync();
        Host.Dispose();
        base.OnExit(e);
    }
}