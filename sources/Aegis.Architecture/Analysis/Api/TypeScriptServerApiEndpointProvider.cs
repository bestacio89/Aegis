using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Api;

/// <summary>
/// Discovers Express and NestJS server endpoints using the repository's
/// TypeScript compiler API parser. This provider performs syntax analysis only;
/// it does not execute application code or claim type-checking semantics.
/// </summary>
public sealed class TypeScriptServerApiEndpointProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);

    public IReadOnlyList<ApiEndpointEvidence> Discover(
        string modulePath,
        IEnumerable<string> sourceFiles,
        ICollection<AnalysisDiagnostic>? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentNullException.ThrowIfNull(sourceFiles);

        var fullModulePath = Path.GetFullPath(modulePath);
        var files = sourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .Where(path => File.Exists(path) && IsWithinModule(path, fullModulePath))
            .OrderBy(path => path, PathComparer)
            .ToArray();

        if (files.Length == 0)
            return Array.Empty<ApiEndpointEvidence>();

        var analyzerPath = ResolveAnalyzerPath();
        if (analyzerPath is null)
        {
            AddDiagnostic(
                diagnostics,
                "TypeScriptServerApiAnalyzerNotFound",
                "The TypeScript server API analyzer could not be located.",
                fullModulePath);
            return Array.Empty<ApiEndpointEvidence>();
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("AEGIS_NODE_EXECUTABLE") ?? "node",
            WorkingDirectory = Path.GetDirectoryName(analyzerPath)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(analyzerPath);
        startInfo.ArgumentList.Add(fullModulePath);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                AddDiagnostic(
                    diagnostics,
                    "TypeScriptServerApiAnalyzerStartFailed",
                    "The Node.js TypeScript endpoint analyzer did not start.",
                    fullModulePath);
                return Array.Empty<ApiEndpointEvidence>();
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.StandardInput.Write(JsonSerializer.Serialize(files));
            process.StandardInput.Close();

            if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                AddDiagnostic(
                    diagnostics,
                    "TypeScriptServerApiAnalyzerTimedOut",
                    $"The TypeScript endpoint analyzer exceeded {ProcessTimeout.TotalSeconds} seconds.",
                    fullModulePath);
                return Array.Empty<ApiEndpointEvidence>();
            }

            Task.WaitAll(standardOutput, standardError);
            if (process.ExitCode != 0)
            {
                AddDiagnostic(
                    diagnostics,
                    "TypeScriptServerApiAnalyzerFailed",
                    LimitText(standardError.Result, 4_000),
                    fullModulePath);
                return Array.Empty<ApiEndpointEvidence>();
            }

            using var document = JsonDocument.Parse(standardOutput.Result);
            var root = document.RootElement;
            if (!root.TryGetProperty("status", out var status)
                || !string.Equals(status.GetString(), "completed", StringComparison.Ordinal))
            {
                AddDiagnostic(
                    diagnostics,
                    "TypeScriptServerApiAnalyzerInvalidResponse",
                    "The TypeScript endpoint analyzer returned an unsupported response.",
                    fullModulePath);
                return Array.Empty<ApiEndpointEvidence>();
            }

            var evidence = root.TryGetProperty("endpoints", out var endpoints)
                ? endpoints.Deserialize<ApiEndpointEvidence[]>(JsonOptions) ?? []
                : [];

            if (root.TryGetProperty("diagnostics", out var analyzerDiagnostics)
                && analyzerDiagnostics.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in analyzerDiagnostics.EnumerateArray())
                {
                    var code = GetString(item, "code") ?? "TypeScriptServerApiAnalyzerDiagnostic";
                    var file = GetString(item, "file");
                    var line = item.TryGetProperty("line", out var lineValue)
                        && lineValue.ValueKind == JsonValueKind.Number
                        ? lineValue.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : null;
                    var message = GetString(item, "message") ?? code;
                    var target = file is null ? fullModulePath : Path.Combine(fullModulePath, file);

                    AddDiagnostic(
                        diagnostics,
                        code,
                        line is null ? message : $"{message} (line {line})",
                        target);
                }
            }

            return Array.AsReadOnly(evidence);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or JsonException
                or System.ComponentModel.Win32Exception
                or ArgumentException)
        {
            AddDiagnostic(
                diagnostics,
                "TypeScriptServerApiAnalyzerExecutionFailed",
                exception.Message,
                fullModulePath);
            return Array.Empty<ApiEndpointEvidence>();
        }
    }

    private static string? ResolveAnalyzerPath()
    {
        var configured = Environment.GetEnvironmentVariable("AEGIS_SERVER_API_ANALYZER_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var relativePath = Path.Combine(
            "Evaluators",
            "FrontEnd",
            "server-api-endpoint-analyzer.mjs");

        var outputCandidate = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(outputCandidate))
            return outputCandidate;

        for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var sourceCandidate = Path.Combine(
                directory.FullName,
                "sources",
                "Aegis.Architecture",
                "Evaluators",
                "FrontEnd",
                "server-api-endpoint-analyzer.mjs");
            if (File.Exists(sourceCandidate))
                return sourceCandidate;
        }

        return null;
    }

    private static bool IsWithinModule(string filePath, string modulePath)
    {
        var relative = Path.GetRelativePath(modulePath, filePath);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static void AddDiagnostic(
        ICollection<AnalysisDiagnostic>? diagnostics,
        string code,
        string message,
        string target)
    {
        diagnostics?.Add(new AnalysisDiagnostic
        {
            Code = code,
            Component = nameof(TypeScriptServerApiEndpointProvider),
            Message = message,
            Target = target,
            AffectsCoverage = true
        });
    }

    private static string LimitText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "The analyzer process failed without diagnostic output.";

        return value.Length <= maximumLength
            ? value
            : value[..maximumLength];
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
