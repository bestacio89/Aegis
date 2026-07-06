using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Repositories;
using Franz.Common.DependencyInjection.Extensions;
using Franz.Common.EntityFramework.Extensions;

// Franz persistence entrypoint
using Franz.Common.Http.EntityFramework.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aegis.Infrastructure.Extensions
{
    /// <summary>
    /// Registers all persistence and repository services for Aegis.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the Aegis infrastructure layer:
        /// - Automatically configures the database (based on provider)
        /// - Registers generic + entity repositories
        /// - Enables per-request transaction filters
        /// </summary>
        public static IServiceCollection AddAegisInfrastructure(
            this IServiceCollection services,
            IHostEnvironment env,
            IConfiguration config)
        {
            // 🧱 Core persistence (auto-detects provider)
            services.AddRelationalDatabase<AegisDbContext>(env, config)
                    .AddEntityRepositories<AegisDbContext>();

            // 🧩 Explicit repositories (if any custom ones exist)
            services.AddScoped<IReportRepository, ReportRepository>();

            // 🔍 Automatically register all dependencies decorated with [Injectable]
            services.AddDependencies();

            return services;
        }
    }
}
