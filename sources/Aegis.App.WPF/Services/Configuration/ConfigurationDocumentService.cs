using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Architecture.Models.Policies.Dependency;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;
using Aegis.Shared.Architecture.Models.Policies.Naming;
using Aegis.Shared.Architecture.Models.Policies.Performance;
using Aegis.Shared.Architecture.Models.Policies.Persistence;
using Aegis.Security.Bootstrap;

namespace Aegis.Wpf.Services.Configuration;

public sealed class ConfigurationDocumentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly PolicyDescriptor[] PolicyDescriptors =
    [
        new("aegis.metadata.policy.json", "Policy metadata", "Policy name, version, report detail, and global weighting metadata.", "AegisPolicyMetadata", () => new AegisArchitecturePolicy(), ["Name", "Version", "ReportDetailLevel", "EnableGlobalWeighting"]),
        new("architecture.policy.json", "Architecture boundaries", "Layer boundaries, dependency directions, language enforcement, and design-pattern rules.", "Architecture", () => new ArchitecturePolicy()),
        new("repository.policy.json", "Repository governance", "Required repository files, CI presence, repository size, and TODO-density checks.", "Repository", () => new RepositoryPolicy()),
        new("security.policy.json", "Static architecture security", "Static architecture checks for secrets, cryptography, and weak TLS usage.", "Security", () => new SecurityPolicy()),
        new("security.runtime.json", "Live security probes", "Runtime probe-group switches and report metadata. This is separate from static architecture security rules.", "AegisSecurity", () => new AegisSecurityOptions()),
        new("maintainability.policy.json", "Maintainability", "Maintainability score thresholds, documentation density, file size, and scoring weights.", "Maintainability", () => new MaintainabilityPolicy()),
        new("dependency.policy.json", "Dependencies", "Package version governance, dependency freshness, pinning, and reference checks.", "Dependency", () => new DependencyPolicy()),
        new("naming.policy.json", "Naming conventions", "Naming rules and allowed names for code constructs.", "Naming", () => new NamingPolicy()),
        new("complexity.policy.json", "Complexity", "Cyclomatic complexity and method-size thresholds.", "Complexity", () => new ComplexityPolicy()),
        new("cohesion.policy.json", "Cohesion", "Class size and member-to-field ratio thresholds.", "Cohesion", () => new CohesionPolicy()),
        new("coupling.policy.json", "Coupling", "Import, module-reference, external-dependency, and coupling-ratio limits.", "Coupling", () => new CouplingPolicy()),
        new("apiconsistency.policy.json", "API consistency", "REST/API route casing, trailing slash, and pluralization conventions.", "ApiConsistency", () => new ApiConsistencyPolicy()),
        new("errorhandling.policy.json", "Error handling", "Rules for empty or generic catch blocks and logging/rethrow behavior.", "ErrorHandling", () => new ErrorHandlingPolicy()),
        new("frontend.policy.json", "Frontend", "Framework-specific conventions for Angular, React, Razor, and Blazor.", "Frontend", () => new FrontendPolicy()),
        new("dependencygraph.policy.json", "Dependency graph", "Maximum dependency count per module.", "DependencyGraph", () => new DependencyGraphPolicy()),
        new("node.policy.json", "Node.js", "Node-specific module loading, process execution, eval, scripts, and promise checks.", "Node", () => new NodePolicy()),
        new("circulardependency.policy.json", "Circular dependencies", "Cycle detection, dependency depth, severity, and cross-language detection.", "CircularDependency", () => new CircularDependencyPolicy()),
        new("configuration.policy.json", "Configuration files", "Required configuration files, environment-variable use, forbidden keys, syntax, and size limits.", "Configuration", () => new ConfigurationPolicy()),
        new("logging.policy.json", "Logging", "Structured logging, log levels, secret handling, framework hints, and configuration usage.", "Logging", () => new LoggingPolicy()),
        new("transaction.policy.json", "Transactions", "Transaction lifecycle, rollback, parameterized SQL, nested transactions, and framework hints.", "Transaction", () => new TransactionPolicy()),
        new("performance.policy.json", "Performance", "Loop, asynchronous I/O, blocking-call, allocation, and performance-weighting rules.", "Performance", () => new PerformancePolicy()),
    ];

    private static readonly HashSet<string> DictionaryObjectNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AllowedDependencies",
        "LayerHints",
        "EnforcementByLanguage",
        "FrameworkHints",
        "LayerScaling",
        "MediatorFrameworkHints",
        "MediatorApplicableLanguages",
        "RequiredFileOverrides"
    };

    private static readonly Dictionary<string, string> PropertyDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enabled"] = "Enables or disables this policy group. The related evaluator must respect this switch for it to change enforcement.",
        ["EnforceSafeRequireUsage"] = "Checks Node.js module loading against the configured safe require/import convention.",
        ["EnforceLayerBoundaries"] = "Enables checks that dependencies follow the configured layer direction rules.",
        ["AllowedDependencies"] = "Maps each source layer to the layers it is allowed to depend on. An empty array means no outgoing layer dependencies are allowed.",
        ["LayerHints"] = "Maps layer names to naming hints used to recognize projects, folders, or namespaces belonging to each layer.",
        ["EnforcementByLanguage"] = "Sets language-specific enforcement: Strict enforces rules, Advisory reports warnings, and Ignored skips evaluation.",
        ["MaxCyclomaticComplexity"] = "Maximum permitted cyclomatic complexity for a method before the evaluator reports a finding.",
        ["MaxLinesPerMethod"] = "Maximum permitted method length in lines.",
        ["MinMaintainabilityIndex"] = "Minimum acceptable maintainability index on the policy's scoring scale.",
        ["MinCommentDensity"] = "Minimum comment density required when comment-density checking is enabled.",
        ["MaxLinesPerFile"] = "Maximum permitted source-file length before the evaluator applies a finding or penalty.",
        ["AllowEmptyCatch"] = "Allows catch blocks without meaningful handling. Keep disabled to flag swallowed exceptions.",
        ["AllowGenericCatch"] = "Allows broad catch clauses. Keep disabled unless the broad catch is intentional and justified.",
        ["RequireLoggingOrRethrow"] = "Requires caught exceptions to be logged or rethrown instead of silently discarded.",
        ["EnforceLowercaseRoutes"] = "Requires API routes to use lowercase path segments.",
        ["EnforceNoTrailingSlash"] = "Flags API routes that end with a trailing slash.",
        ["CheckPluralization"] = "Checks route/resource naming for the expected pluralization convention.",
        ["MaxDependenciesPerModule"] = "Maximum dependency count allowed for a module before reporting excessive dependency breadth.",
        ["MaxNestedLoopDepth"] = "Maximum nested-loop depth before a performance finding is reported.",
        ["MaxLoopBodyLength"] = "Maximum number of lines allowed in a loop body.",
        ["RequireAsyncForIOMethods"] = "Checks that I/O-bound methods use asynchronous APIs where supported.",
        ["DisallowBlockingCalls"] = "Flags synchronous blocking patterns that can stall execution or thread-pool work.",
        ["CheckThreadSleepUsage"] = "Checks for Thread.Sleep-style blocking delays.",
        ["LargeCollectionThreshold"] = "Collection-size threshold used when checking large collection initializations.",
        ["MaxTransactionBlockLines"] = "Maximum transaction-block size before the evaluator reports an overly broad transaction scope.",
        ["RequireExplicitCommit"] = "Requires transaction completion to be explicit.",
        ["RequireRollbackOnFailure"] = "Requires transaction failure paths to roll back appropriately.",
        ["DetectRawSqlConcatenation"] = "Detects SQL assembled through string concatenation, which can enable injection vulnerabilities.",
        ["AllowParameterizedQueriesOnly"] = "Requires SQL queries to use parameters rather than interpolated or concatenated values.",
        ["RequiredFiles"] = "List of file names that the evaluator expects to find in the target repository.",
        ["ForbiddenKeys"] = "Configuration key fragments that should be treated as forbidden or sensitive.",
        ["RequiredConfigFiles"] = "Configuration files expected for the relevant logging or infrastructure conventions.",
        ["MinimumLevel"] = "Minimum logging level expected by the policy.",
        ["EnableOsProbes"] = "Enables live probes classified as operating-system probes.",
        ["EnableNetworkProbes"] = "Enables live probes classified as network probes, including the current port probe.",
        ["EnableTlsProbes"] = "Enables live probes classified as TLS probes.",
        ["EnableFileSystemProbes"] = "Enables live probes classified as filesystem probes.",
        ["EnableCloudProbes"] = "Enables live probes classified as cloud probes.",
        ["EnableWcfProbes"] = "Enables live probes classified as WCF probes.",
        ["EngineVersion"] = "Version identifier written into security reports.",
        ["PolicyVersion"] = "Version identifier written into security reports."
    };

    private readonly string _configurationDirectory;

    public ConfigurationDocumentService()
    {
        _configurationDirectory = ResolveConfigurationDirectory();
    }

    public string ConfigurationDirectory => _configurationDirectory;

    public IReadOnlyList<ConfigurationDocument> LoadDocuments()
    {
        var documents = new List<ConfigurationDocument>(PolicyDescriptors.Length);

        foreach (var descriptor in PolicyDescriptors)
        {
            var path = Path.Combine(_configurationDirectory, descriptor.FileName);

            if (!File.Exists(path))
            {
                documents.Add(new ConfigurationDocument(
                    descriptor.FileName,
                    descriptor.Title,
                    descriptor.Description,
                    path,
                    null,
                    "The policy file does not exist."));
                continue;
            }

            try
            {
                var rootNode = JsonNode.Parse(File.ReadAllText(path));
                if (rootNode is not JsonObject actualRoot)
                {
                    documents.Add(new ConfigurationDocument(
                        descriptor.FileName,
                        descriptor.Title,
                        descriptor.Description,
                        path,
                        null,
                        "The policy document must have a JSON object at its root."));
                    continue;
                }

                // Surface every property from the typed policy model, including
                // defaults that have not yet been written explicitly to JSON.
                var defaultInstance = descriptor.CreateDefaults();
                var policyType = defaultInstance.GetType();
                var defaultsNode = JsonSerializer.SerializeToNode(
                    defaultInstance,
                    policyType,
                    JsonOptions) as JsonObject ?? new JsonObject();

                if (descriptor.DefaultProperties is not null)
                {
                    var selectedDefaults = new JsonObject();
                    foreach (var propertyName in descriptor.DefaultProperties)
                    {
                        if (defaultsNode.TryGetPropertyValue(propertyName, out var propertyValue))
                        {
                            selectedDefaults[propertyName] = propertyValue?.DeepClone();
                        }
                    }

                    defaultsNode = selectedDefaults;
                }

                var defaultsRoot = new JsonObject
                {
                    [descriptor.RootSection] = defaultsNode.DeepClone()
                };

                var mergedRoot = MergeNodes(
                    JsonNode.Parse(defaultsRoot.ToJsonString()),
                    actualRoot) as JsonObject
                    ?? throw new JsonException("The merged policy document is not a JSON object.");

                var document = new ConfigurationDocument(
                    descriptor.FileName,
                    descriptor.Title,
                    descriptor.Description,
                    path,
                    mergedRoot);

                var settings = new List<ConfigurationSetting>();
                Flatten(mergedRoot, defaultsRoot, policyType, [], settings);
                document.SetSettings(settings);
                documents.Add(document);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                documents.Add(new ConfigurationDocument(
                    descriptor.FileName,
                    descriptor.Title,
                    descriptor.Description,
                    path,
                    null,
                    ex.Message));
            }
        }

        return documents;
    }

    public bool ValidateAll(IEnumerable<ConfigurationDocument> documents, out string validationMessage)
    {
        var documentList = documents.ToArray();
        var errors = documentList
            .SelectMany(document =>
                (document.IsLoaded ? Enumerable.Empty<string>() :
                    new[] { $"{document.Title}: {document.LoadError ?? "The policy document is not loaded."}" })
                .Concat(document.Settings
                    .Where(setting => setting.HasError)
                    .Select(setting => $"{document.Title} — {string.Join(" / ", setting.PathSegments)}: {setting.Error}")))
            .ToArray();

        validationMessage = errors.Length == 0
            ? string.Empty
            : string.Join(Environment.NewLine, errors);

        return errors.Length == 0;
    }

    public void SaveDocument(ConfigurationDocument document)
    {
        if (!document.IsLoaded || document.Root is null)
        {
            throw new InvalidOperationException(
                $"{document.Title} cannot be saved because its source document could not be loaded.");
        }

        var invalidSetting = document.Settings.FirstOrDefault(setting => setting.HasError);
        if (invalidSetting is not null)
        {
            throw new InvalidOperationException(
                $"{string.Join(" / ", invalidSetting.PathSegments)}: {invalidSetting.Error}");
        }

        var updatedRoot = JsonNode.Parse(document.Root.ToJsonString())!.AsObject();

        foreach (var setting in document.Settings)
        {
            if (!setting.TryCreateNode(out var value, out var error))
            {
                throw new InvalidOperationException(
                    $"{string.Join(" / ", setting.PathSegments)}: {error}");
            }

            SetNodeAtPath(updatedRoot, setting.PathSegments, value);
        }

        var json = updatedRoot.ToJsonString(JsonOptions) + Environment.NewLine;
        var temporaryPath = document.FilePath + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, document.FilePath, overwrite: true);
            document.MarkSaved(updatedRoot);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Flatten(
        JsonNode? node,
        JsonObject defaultsRoot,
        Type policyType,
        string[] path,
        ICollection<ConfigurationSetting> settings)
    {
        if (node is JsonObject jsonObject &&
            path.Length > 0 &&
            !DictionaryObjectNames.Contains(path[^1]))
        {
            foreach (var property in jsonObject)
            {
                Flatten(property.Value, defaultsRoot, policyType, [.. path, property.Key], settings);
            }

            return;
        }

        if (node is JsonObject rootObject && path.Length == 0)
        {
            foreach (var property in rootObject)
            {
                Flatten(property.Value, defaultsRoot, policyType, [property.Key], settings);
            }

            return;
        }

        var kind = GetValueKind(node);
        var text = GetEditorText(node, kind);
        var name = path.LastOrDefault() ?? "Root";
        var defaultNode = GetNodeAtPath(defaultsRoot, path);
        var defaultKind = GetValueKind(defaultNode);
        var defaultText = defaultNode is null
            ? "Not defined in typed defaults"
            : defaultKind is ConfigurationValueKind.Object or ConfigurationValueKind.Array
                ? "Structured default defined in the typed policy model"
                : GetEditorText(defaultNode, defaultKind);

        settings.Add(new ConfigurationSetting(
            path,
            kind,
            text,
            Describe(name, path, kind),
            defaultText,
            GetExpectedType(policyType, path)));
    }

    private static ConfigurationValueKind GetValueKind(JsonNode? node)
    {
        if (node is null)
            return ConfigurationValueKind.Null;

        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.String => ConfigurationValueKind.String,
            JsonValueKind.True or JsonValueKind.False => ConfigurationValueKind.Boolean,
            JsonValueKind.Number => ConfigurationValueKind.Number,
            JsonValueKind.Object => ConfigurationValueKind.Object,
            JsonValueKind.Array => ConfigurationValueKind.Array,
            _ => ConfigurationValueKind.Null
        };
    }

    private static string GetEditorText(JsonNode? node, ConfigurationValueKind kind)
    {
        if (node is null)
            return "null";

        if (kind == ConfigurationValueKind.String)
        {
            using var document = JsonDocument.Parse(node.ToJsonString());
            return document.RootElement.GetString() ?? string.Empty;
        }

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Describe(string name, string[] path, ConfigurationValueKind kind)
    {
        if (PropertyDescriptions.TryGetValue(name, out var knownDescription))
            return knownDescription;

        var friendlyName = SplitPascalCase(name).ToLowerInvariant();

        if (kind == ConfigurationValueKind.Boolean)
        {
            return $"Enables or disables the {friendlyName} rule in the {string.Join(" / ", path.Take(Math.Max(0, path.Length - 1)))} policy section. Confirm the consuming evaluator before changing enforcement.";
        }

        if (kind == ConfigurationValueKind.Number)
        {
            return $"Numeric threshold or weight for {friendlyName}. Its practical effect depends on the evaluator that consumes this property.";
        }

        if (kind is ConfigurationValueKind.Array or ConfigurationValueKind.Object)
        {
            return $"Structured policy data for {friendlyName}. Edit as valid JSON; preserve the expected keys and value types.";
        }

        return $"Value used by the {friendlyName} policy setting. Confirm the related evaluator's interpretation before changing it.";
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) &&
                (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1])))
            {
                result.Append(' ');
            }

            result.Append(value[i]);
        }

        return result.ToString();
    }

    private static Type? GetExpectedType(Type policyType, string[] path)
    {
        Type? currentType = policyType;

        // The first path segment is the JSON root section name; the model type
        // represents the object inside that section.
        foreach (var segment in path.Skip(1))
        {
            if (currentType is null)
                return null;

            var property = currentType.GetProperty(
                segment,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property is null || property.SetMethod is null)
                return null;

            currentType = property.PropertyType;
        }

        return currentType;
    }

    private static JsonNode? GetNodeAtPath(JsonObject root, string[] path)
    {
        JsonNode? current = root;
        foreach (var segment in path)
        {
            if (current is not JsonObject jsonObject || !jsonObject.TryGetPropertyValue(segment, out current))
                return null;
        }

        return current;
    }

    private static void SetNodeAtPath(JsonObject root, string[] path, JsonNode? value)
    {
        if (path.Length == 0)
            throw new InvalidOperationException("A setting path cannot be empty.");

        JsonObject current = root;

        for (var i = 0; i < path.Length - 1; i++)
        {
            if (current[path[i]] is not JsonObject next)
            {
                throw new InvalidOperationException(
                    $"The JSON path '{string.Join(" / ", path)}' no longer exists.");
            }

            current = next;
        }

        current[path[^1]] = value?.DeepClone();
    }

    private static JsonNode? MergeNodes(JsonNode? defaults, JsonNode? actual)
    {
        if (defaults is JsonObject defaultObject && actual is JsonObject actualObject)
        {
            foreach (var property in actualObject)
            {
                var targetKey = defaultObject
                    .Select(candidate => candidate.Key)
                    .FirstOrDefault(key => string.Equals(
                        key,
                        property.Key,
                        StringComparison.OrdinalIgnoreCase));

                if (targetKey is not null &&
                    defaultObject[targetKey] is JsonObject defaultChild &&
                    property.Value is JsonObject actualChild)
                {
                    _ = MergeNodes(defaultChild, actualChild);
                }
                else if (targetKey is not null)
                {
                    defaultObject[targetKey] = property.Value?.DeepClone();
                }
                else
                {
                    defaultObject[property.Key] = property.Value?.DeepClone();
                }
            }

            return defaultObject;
        }

        return actual?.DeepClone() ?? defaults?.DeepClone();
    }

    private static string ResolveConfigurationDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "config");
            if (File.Exists(Path.Combine(current.FullName, "Aegis.Wpf.csproj")) &&
                PolicyDescriptors.All(descriptor => File.Exists(Path.Combine(candidate, descriptor.FileName))))
            {
                return candidate;
            }

            current = current.Parent;
        }

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "config");
        if (PolicyDescriptors.All(descriptor => File.Exists(Path.Combine(outputDirectory, descriptor.FileName))))
        {
            return outputDirectory;
        }

        return outputDirectory;
    }

    private sealed record PolicyDescriptor(
        string FileName,
        string Title,
        string Description,
        string RootSection,
        Func<object> CreateDefaults,
        string[]? DefaultProperties = null);
}
