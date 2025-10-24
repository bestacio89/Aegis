using Aegis.Sdk;
using Franz.Common.Logging.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aegis.Cli.Services;

public static class CliHostBuilder
{
    public static IHost Build() =>
        Host.CreateDefaultBuilder()
            .UseLog() // Franz Logging Integration
            .ConfigureServices(services =>
            {
                services.AddSingleton<AegisArchitectureAnalysisRunner>(); // 👈 This line is key
            })
            .Build();
}
