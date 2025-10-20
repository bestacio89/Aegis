using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using System.Diagnostics;
using System.IO;

namespace Aegis.Infrastructure.Persistence
{
    public static class DatabaseDetector
    {
        public static string DetectOrRepairDatabaseConfig(IConfiguration config, ILogger logger)
        {
            var provider = config["Database:Provider"]?.ToLowerInvariant();
            var connectionOk = TryConnection(config, provider, logger);

            if (connectionOk)
            {
                logger.LogInformation("✅ Database connection succeeded for provider {Provider}.", provider);
                return provider!;
            }

            logger.LogWarning("⚠️ Connection to provider {Provider} failed. Scanning for available engines...", provider);

            // Check for available local engines
            if (IsProcessRunning("postgres"))
                return PatchConfig(config, "postgres", logger);
            if (IsProcessRunning("mysqld") || IsProcessRunning("mariadbd"))
                return PatchConfig(config, "mariadb", logger);
            if (IsProcessRunning("sqlservr"))
                return PatchConfig(config, "sqlserver", logger);

            logger.LogError("❌ No supported database engine detected locally. Falling back to SQLite memory mode.");
            return PatchConfig(config, "sqlite", logger);
        }

        private static bool TryConnection(IConfiguration config, string? provider, ILogger logger)
        {
            try
            {
                return provider switch
                {
                    "postgres" => TryPostgres(config),
                    "mariadb" or "mysql" => TryMaria(config),
                    "sqlserver" => TrySqlServer(config),
                    _ => false
                };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Connection test for provider {Provider} failed.", provider);
                return false;
            }
        }

        private static bool TryPostgres(IConfiguration config)
        {
            using var conn = new NpgsqlConnection(BuildPostgresConnectionString(config));
            conn.Open();
            return conn.State == System.Data.ConnectionState.Open;
        }

        private static bool TryMaria(IConfiguration config)
        {
            using var conn = new MySqlConnection(BuildMariaConnectionString(config));
            conn.Open();
            return conn.State == System.Data.ConnectionState.Open;
        }

        private static bool TrySqlServer(IConfiguration config)
        {
            using var conn = new SqlConnection(BuildSqlServerConnectionString(config));
            conn.Open();
            return conn.State == System.Data.ConnectionState.Open;
        }

        private static bool IsProcessRunning(string processName)
            => Process.GetProcessesByName(processName).Any();

        private static string PatchConfig(IConfiguration config, string newProvider, ILogger logger)
        {
            logger.LogInformation("🔄 Switching database provider to {Provider}", newProvider);

            // NOTE: this modifies in-memory config (you can also persist it to appsettings.json)
            var section = config.GetSection("Database");
            section["Provider"] = newProvider;

            return newProvider;
        }

        private static string BuildPostgresConnectionString(IConfiguration config)
            => $"Host={config["Database:ServerName"]};Port={config["Database:Port"] ?? "5432"};" +
               $"Database={config["Database:DatabaseName"]};Username={config["Database:UserName"]};Password={config["Database:Password"]}";

        private static string BuildMariaConnectionString(IConfiguration config)
            => $"Server={config["Database:ServerName"]};Port={config["Database:Port"] ?? "3306"};" +
               $"Database={config["Database:DatabaseName"]};Uid={config["Database:UserName"]};Pwd={config["Database:Password"]};";

        private static string BuildSqlServerConnectionString(IConfiguration config)
            => $"Server={config["Database:ServerName"]},{config["Database:Port"] ?? "1433"};" +
               $"Database={config["Database:DatabaseName"]};User Id={config["Database:UserName"]};Password={config["Database:Password"]};TrustServerCertificate=True;";
    }
}
