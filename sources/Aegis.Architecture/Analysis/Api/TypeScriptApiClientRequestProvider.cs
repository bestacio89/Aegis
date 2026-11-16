using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Api;

/// <summary>
/// Discovers outbound Fetch, Axios, Angular HttpClient and NestJS HttpService
/// declarations from TypeScript and JavaScript using the TypeScript compiler API.
/// </summary>
public sealed class TypeScriptApiClientRequestProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);

    public IReadOnlyList<ApiClientRequestEvidence> Discover(
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
            return Array.Empty<ApiClientRequestEvidence>();

        var analyzerPath = ResolveAnalyzerPath();
        if (analyzerPath is null)
        {
            AddDiagnostic(diagnostics, "TypeScriptApiClientAnalyzerNotFound",
                "The TypeScript API client analyzer could not be located.", fullModulePath);
            return Array.Empty<ApiClientRequestEvidence>();
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
                AddDiagnostic(diagnostics, "TypeScriptApiClientAnalyzerStartFailed",
                    "The Node.js API client analyzer did not start.", fullModulePath);
                return Array.Empty<ApiClientRequestEvidence>();
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.StandardInput.Write(JsonSerializer.Serialize(files));
            process.StandardInput.Close();

            if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                AddDiagnostic(diagnostics, "TypeScriptApiClientAnalyzerTimedOut",
                    $"The TypeScript API client analyzer exceeded {ProcessTimeout.TotalSeconds} seconds.",
                    fullModulePath);
                return Array.Empty<ApiClientRequestEvidence>();
            }

            Task.WaitAll(standardOutput, standardError);
            if (process.ExitCode != 0)
            {
                AddDiagnostic(diagnostics, "TypeScriptApiClientAnalyzerFailed",
                    LimitText(standardError.Result, 4_000), fullModulePath);
                return Array.Empty<ApiClientRequestEvidence>();
            }

            using var document = JsonDocument.Parse(standardOutput.Result);
            var root = document.RootElement;
            if (!root.TryGetProperty("status", out var status)
                || !string.Equals(status.GetString(), "completed", StringComparison.Ordinal))
            {
                AddDiagnostic(diagnostics, "TypeScriptApiClientAnalyzerInvalidResponse",
                    "The TypeScript API client analyzer returned an unsupported response.", fullModulePath);
                return Array.Empty<ApiClientRequestEvidence>();
            }

            var evidence = root.TryGetProperty("clientRequests", out var requests)
                ? requests.Deserialize<ApiClientRequestEvidence[]>(JsonOptions) ?? []
                : [];

            if (root.TryGetProperty("diagnostics", out var analyzerDiagnostics)
                && analyzerDiagnostics.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in analyzerDiagnostics.EnumerateArray())
                {
                    var code = GetString(item, "code") ?? "TypeScriptApiClientAnalyzerDiagnostic";
                    var file = GetString(item, "file");
                    var line = item.TryGetProperty("line", out var lineValue)
                        && lineValue.ValueKind == JsonValueKind.Number
                        ? lineValue.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : null;
                    var message = GetString(item, "message") ?? code;
                    var target = file is null ? fullModulePath : Path.Combine(fullModulePath, file);
                    AddDiagnostic(diagnostics, code,
                        line is null ? message : $"{message} (line {line})", target);
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
            AddDiagnostic(diagnostics, "TypeScriptApiClientAnalyzerExecutionFailed",
                exception.Message, fullModulePath);
            return Array.Empty<ApiClientRequestEvidence>();
        }
    }

    private static string? ResolveAnalyzerPath()
    {
        var configured = Environment.GetEnvironmentVariable("AEGIS_API_CLIENT_ANALYZER_PATH")
            ?? Environment.GetEnvironmentVariable("AEGIS_SERVER_API_ANALYZER_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var relativePath = Path.Combine("Evaluators", "FrontEnd", "server-api-endpoint-analyzer.mjs");
        var searchRoots = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory };
        foreach (var searchRoot in searchRoots)
        {
            for (var directory = new DirectoryInfo(searchRoot);
                 directory is not null;
                 directory = directory.Parent)
            {
                var sourceCandidate = Path.Combine(directory.FullName, "sources", "Aegis.Architecture",
                    "Evaluators", "FrontEnd", "server-api-endpoint-analyzer.mjs");
                if (HasTypeScriptRuntime(sourceCandidate))
                    return sourceCandidate;
            }
        }

        var outputCandidate = Path.Combine(AppContext.BaseDirectory, relativePath);
        return HasTypeScriptRuntime(outputCandidate) ? outputCandidate : null;
    }

    private static bool HasTypeScriptRuntime(string analyzerPath)
    {
        if (!File.Exists(analyzerPath))
            return false;

        var runtimeManifest = Path.Combine(Path.GetDirectoryName(analyzerPath)!,
            "node_modules", "typescript", "package.json");
        return File.Exists(runtimeManifest);
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
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
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
            Component = nameof(TypeScriptApiClientRequestProvider),
            Message = message,
            Target = target,
            AffectsCoverage = true
        });
    }

    private static string LimitText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "The analyzer process failed without diagnostic output.";
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
