using Aegis.App.Wpf.ViewModels;
using Aegis.Core.Architecture.RuleEngines;
using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Repositories;
using Aegis.Sdk;
using Franz.Common.EntityFramework.Repositories;
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
        Host = CreateHostBuilder().Build();
    }

    private static IHostBuilder CreateHostBuilder() =>
        Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // ✅ Core Aegis services
                services.AddDbContext<AegisDbContext>();
                services.AddScoped<RuleEngine>();
                services.AddScoped<AegisRunner>();

                // ✅ Repositories
                services.AddScoped(typeof(EntityRepository<AegisDbContext, RuleResultEntity>));
                services.AddScoped(typeof(EntityRepository<AegisDbContext, ReportEntity>));
                services.AddScoped<IRuleResultRepository, RuleResultRepository>();
                services.AddScoped<IReportRepository, ReportRepository>();

                // ✅ ViewModels
                services.AddSingleton<MainViewModel>();

                // ✅ Logging
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.AddDebug();
                });
            });

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
