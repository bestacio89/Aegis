using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO;
using Aegis.SDK.Models;

namespace Aegis.SDK.Installers
{
    public static class DatabaseInstaller
    {
        public static DatabaseChoice RunSetup(ILogger logger)
        {
            Console.WriteLine("🧩 Welcome to Aegis setup!");
            Console.WriteLine("Let's configure your database engine.\n");

            var options = new[] { "PostgreSQL", "SQL Server", "MariaDB", "Oracle", "CosmosDB", "MongoDB" };
            for (int i = 0; i < options.Length; i++)
                Console.WriteLine($"{i + 1}. {options[i]}");

            Console.Write("\nSelect your preferred provider [1-6]: ");
            if (!int.TryParse(Console.ReadLine(), out var choice) || choice < 1 || choice > options.Length)
                choice = 1; // default: PostgreSQL

            var provider = options[choice - 1].ToLowerInvariant().Replace(" ", "");

            Console.WriteLine($"\nSelected provider: {provider}");
            Console.Write("Server name [localhost]: ");
            var server = Console.ReadLine() ?? "localhost";

            Console.Write("Database name [aegis]: ");
            var dbName = Console.ReadLine() ?? "aegis";

            Console.Write("User name [postgres]: ");
            var user = Console.ReadLine() ?? "postgres";

            Console.Write("Password: ");
            var pass = ReadPassword();

            var port = provider switch
            {
                "postgresql" or "postgres" => "5432",
                "sqlserver" => "1433",
                "mariadb" => "3306",
                "oracle" => "1521",
                "mongo" => "27017",
                "cosmosdb" => "443",
                _ => "5432"
            };

            var config = new DatabaseChoice(provider, server, dbName, user, pass, port);
            SaveChoice(config, logger);

            return config;
        }

        private static void SaveChoice(DatabaseChoice choice, ILogger logger)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "aegis.settings.json");
            var json = JsonSerializer.Serialize(choice, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            logger.LogInformation("💾 Database configuration saved at {Path}", path);
        }

        private static string ReadPassword()
        {
            var password = string.Empty;
            ConsoleKey key;
            do
            {
                var keyInfo = Console.ReadKey(intercept: true);
                key = keyInfo.Key;

                if (key == ConsoleKey.Backspace && password.Length > 0)
                {
                    Console.Write("\b \b");
                    password = password[..^1];
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    Console.Write("*");
                    password += keyInfo.KeyChar;
                }
            } while (key != ConsoleKey.Enter);
            Console.WriteLine();
            return password;
        }
    }
}
