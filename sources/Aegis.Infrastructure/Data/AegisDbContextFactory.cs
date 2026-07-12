using Franz.Common.Mediator.Dispatchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Infrastructure.Data;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<AegisDbContext>
{
    public AegisDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AegisDbContext>();
        var config = new ConfigurationBuilder()
          .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../Aegis.App.WPF/config"))
          .AddJsonFile("appsettings.json", optional: false)
          .AddJsonFile("appsettings.Development.json", optional: true)
          .Build();

        var db = config.GetSection("Database");

        var connectionString =
            $"Host={db["ServerName"]};" +
            $"Database={db["DatabaseName"]};" +
            $"Username={db["UserName"]};" +
            $"Password={db["Password"]};" +
            $"Port={db["Port"]};" +
            $"SSL Mode={db["Ssl"]};";

        optionsBuilder.UseNpgsql(
                connectionString);

        // Minimal service provider just for design time
        var services = new ServiceCollection();
        services.AddScoped<IDispatcher, FranzDispatcher>();
        var sp = services.BuildServiceProvider();

        return new AegisDbContext(
            optionsBuilder.Options,
            sp.GetRequiredService<IDispatcher>(), null);
    }
}