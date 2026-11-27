using System.Text.Json;

using Aegis.Architecture.Analysis.Api;
using Aegis.Architecture.Analysis.Detectors;
using Aegis.Architecture.Analysis.Integrations.Messaging;
using Aegis.Architecture.Analysis.Integrations.Rpc;
using Aegis.Architecture.Analysis.Integrations.Data;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

namespace Aegis.Architecture.Analysis;

public static class ProjectArchitectureContextDetector
{
    public static ProjectArchitectureContext Detect(string rootPath) =>
        Detect(rootPath, new ArchitecturePolicy());

    public static ProjectArchitectureContext Detect(
        string rootPath,
        ArchitecturePolicy architecturePolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(architecturePolicy);

        var ctx = new ProjectArchitectureContext
        {
            ProjectName =
                Path.GetFileName(
                    rootPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)),

            RootPath = rootPath,

            DetectorVersion = "1.5.0",

            DetectionStrategy = "Hybrid"
        };


        var files = EnumerateGovernedFiles(rootPath, ctx.DiscoveryDiagnostics, ctx.ExcludedEntriesByReason);



        ctx.FileCount = files.Count;



        // ===============================================================
        // Technology Detection
        // ===============================================================

        LanguageAndBuildDetector.Analyze(
            ctx,
            files);


        // Discover project boundaries before framework aggregation so a
        // mixed repository does not inherit one project's framework identity.
        ModuleDetector.Analyze(
            ctx,
            files);

        LoadApplicationIntegrations(ctx);

        // Build manifests and structured dependency evidence must be available
        // before framework classification. Java framework identity is derived
        // from module-owned coordinates, never from source or directory names.
        DependencyDetector.Analyze(
            ctx,
            files,
            includeProjectReferences: false);


        FrameworkDetector.Analyze(
            ctx,
            files);

        DiscoverServerApiEndpoints(ctx, files);
        DiscoverApiClientRequests(ctx, files);
        DiscoverMessageIntegrations(ctx, files);
        DiscoverRpcIntegrations(ctx, files);
        DiscoverDataAccessIntegrations(ctx, files);
        ctx.IntegrationEvidenceCatalog = new IntegrationEvidenceCatalog(
            ctx.MessageIntegrationEvidenceRegistry,
            ctx.RpcIntegrationEvidenceRegistry,
            ctx.DataAccessEvidenceRegistry);

        // Framework discovery enriches module-owned evidence (notably Java
        // Maven/Gradle frameworks). Recompute roles only after that evidence
        // is available; the initial pass in ModuleDetector handles manifest-
        // local roles, but cannot classify frameworks discovered here.
        ModuleRoleDetector.Analyze(ctx.Modules);

        LayerDetector.Analyze(
            ctx,
            files,
            architecturePolicy);


        DomainDetector.Analyze(
            ctx);



        // ===============================================================
        // Architecture Detection
        // ===============================================================

        ArchitectureDetector.Analyze(
            ctx);


        PatternDetector.Analyze(
            ctx);


        BoundaryDetector.Analyze(
            ctx);



        // ===============================================================
        // Dependency Mapping
        // ===============================================================

        DependencyDetector.Analyze(
            ctx,
            files);



        // ===============================================================
        // Runtime Detection
        // ===============================================================

        DeployabilityDetector.Analyze(
            ctx,
            files);



        // ===============================================================
        // Project Identity
        // ===============================================================

        NatureDetector.Analyze(
            ctx,
            files);


        EntryPointDetector.Analyze(
            ctx,
            files);



        // ===============================================================
        // Final Context Enrichment
        // ===============================================================

        ArchitectureConfidenceEvaluator.Evaluate(
            ctx);

        // Framework-specific rules may be classified as not applicable only
        // after source discovery completed without coverage-affecting failures.
        ctx.FrontendFrameworkDiscoveryComplete =
            !ctx.DiscoveryDiagnostics.Any(diagnostic => diagnostic.AffectsCoverage);

        PopulateContextMetadata(
            ctx,
            files);



        return ctx;
    }



    private static List<string> EnumerateGovernedFiles(
        string rootPath,
        ICollection<AnalysisDiagnostic> diagnostics,
        IDictionary<string, int> excludedEntriesByReason)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootPath));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var directoryExclusionReason = FileExclusions.GetExclusionReason(directory);
            if (directoryExclusionReason is not null)
            {
                RecordExcludedEntry(excludedEntriesByReason, directoryExclusionReason);
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var exclusionReason = FileExclusions.GetExclusionReason(file);
                    if (exclusionReason is null)
                        files.Add(file);
                    else
                        RecordExcludedEntry(excludedEntriesByReason, exclusionReason);
                }

                foreach (var child in Directory.EnumerateDirectories(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var exclusionReason = FileExclusions.GetExclusionReason(child);
                    if (exclusionReason is not null)
                    {
                        RecordExcludedEntry(excludedEntriesByReason, exclusionReason);
                        continue;
                    }

                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch (IOException exception)
                    {
                        diagnostics.Add(new AnalysisDiagnostic
                        {
                            Code = "DirectoryMetadataUnavailable",
                            Component = nameof(ProjectArchitectureContextDetector),
                            Message = $"Unable to inspect directory metadata: {exception.Message}",
                            Target = child,
                            ExceptionType = exception.GetType().Name,
                            AffectsCoverage = true
                        });
                        continue;
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        diagnostics.Add(new AnalysisDiagnostic
                        {
                            Code = "DirectoryMetadataAccessDenied",
                            Component = nameof(ProjectArchitectureContextDetector),
                            Message = "Access to directory metadata was denied; the path was skipped.",
                            Target = child,
                            ExceptionType = exception.GetType().Name,
                            AffectsCoverage = true
                        });
                        continue;
                    }

                    pending.Push(child);
                }
            }
            catch (IOException exception)
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "DirectoryDiscoveryFailed",
                    Component = nameof(ProjectArchitectureContextDetector),
                    Message = $"Unable to enumerate entries in directory: {exception.Message}",
                    Target = directory,
                    ExceptionType = exception.GetType().Name,
                    AffectsCoverage = true
                });
                // Continue scanning readable siblings rather than aborting the repository scan.
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "DirectoryAccessDenied",
                    Component = nameof(ProjectArchitectureContextDetector),
                    Message = "Access to a directory was denied; files under that path were not assessed.",
                    Target = directory,
                    ExceptionType = exception.GetType().Name,
                    AffectsCoverage = true
                });
                // An inaccessible subtree is excluded from evidence, not treated as clean.
            }
        }

        return files;
    }

    private static void DiscoverServerApiEndpoints(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> files)
    {
        var evidence = new List<ApiEndpointEvidence>();
        var csharpProvider = new Aegis.Architecture.Analysis.Api.CSharpAspNetEndpointProvider();
        var typeScriptProvider = new Aegis.Architecture.Analysis.Api.TypeScriptServerApiEndpointProvider();

        foreach (var module in context.Modules.Where(module =>
                     !string.IsNullOrWhiteSpace(module.Path)))
        {
            var modulePath = Path.GetFullPath(module.Path);
            var moduleFiles = files.Where(file =>
            {
                var relative = Path.GetRelativePath(modulePath, Path.GetFullPath(file));
                return !Path.IsPathRooted(relative)
                    && relative != ".."
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
            }).ToArray();

            if (module.Languages.Contains("C#", StringComparer.OrdinalIgnoreCase))
            {
                var csharpFiles = moduleFiles.Where(file =>
                    string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase));
                evidence.AddRange(csharpProvider.Discover(csharpFiles, modulePath));
            }

            if (module.Languages.Any(language =>
                    language.Equals("TypeScript", StringComparison.OrdinalIgnoreCase)
                    || language.Equals("JavaScript", StringComparison.OrdinalIgnoreCase)))
            {
                var scriptFiles = moduleFiles.Where(file =>
                    Path.GetExtension(file).ToLowerInvariant() is
                        ".ts" or ".tsx" or ".mts" or ".cts" or ".js" or ".jsx" or ".mjs" or ".cjs");
                evidence.AddRange(typeScriptProvider.Discover(
                    modulePath,
                    scriptFiles,
                    context.DiscoveryDiagnostics));
            }
        }

        context.ServerApiEndpointDiscoveryRegistry =
            new ServerApiEndpointDiscoveryRegistry(evidence);
    }


    private static void DiscoverApiClientRequests(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> files)
    {
        var evidence = new List<ApiClientRequestEvidence>();
        var csharpProvider = new CSharpHttpClientRequestProvider();
        var typeScriptProvider = new Aegis.Architecture.Analysis.Api.TypeScriptApiClientRequestProvider();

        foreach (var module in context.Modules.Where(module =>
                     !string.IsNullOrWhiteSpace(module.Path)
                     && module.Languages.Contains("C#", StringComparer.OrdinalIgnoreCase)))
        {
            var modulePath = Path.GetFullPath(module.Path);
            var moduleFiles = files.Where(file =>
            {
                var relative = Path.GetRelativePath(modulePath, Path.GetFullPath(file));
                return !Path.IsPathRooted(relative)
                    && relative != ".."
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
            });

            var csharpFiles = moduleFiles.Where(file =>
                string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase));
            evidence.AddRange(csharpProvider.Discover(csharpFiles, modulePath));
        }

        foreach (var module in context.Modules.Where(module =>
                     !string.IsNullOrWhiteSpace(module.Path)
                     && module.Languages.Any(language =>
                         language.Equals("TypeScript", StringComparison.OrdinalIgnoreCase)
                         || language.Equals("JavaScript", StringComparison.OrdinalIgnoreCase))))
        {
            var modulePath = Path.GetFullPath(module.Path);
            var scriptFiles = files.Where(file =>
            {
                var relative = Path.GetRelativePath(modulePath, Path.GetFullPath(file));
                return !Path.IsPathRooted(relative)
                    && relative != ".."
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
                    && Path.GetExtension(file).ToLowerInvariant() is
                        ".ts" or ".tsx" or ".mts" or ".cts" or ".js" or ".jsx" or ".mjs" or ".cjs";
            });
            evidence.AddRange(typeScriptProvider.Discover(
                modulePath,
                scriptFiles,
                context.DiscoveryDiagnostics));
        }

        context.ApiClientRequestDiscoveryRegistry =
            new ApiClientRequestDiscoveryRegistry(evidence);
    }

    private static void DiscoverMessageIntegrations(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> files)
    {
        var evidence = new List<MessageIntegrationEvidence>();
        var provider = new CSharpMassTransitMessageProvider();

        foreach (var module in context.Modules.Where(module =>
                     !string.IsNullOrWhiteSpace(module.Path)
                     && module.Languages.Contains("C#", StringComparer.OrdinalIgnoreCase)))
        {
            var modulePath = Path.GetFullPath(module.Path);
            var moduleFiles = files.Where(file =>
            {
                var relative = Path.GetRelativePath(modulePath, Path.GetFullPath(file));
                return !Path.IsPathRooted(relative)
                    && relative != ".."
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
            });
            var csharpFiles = moduleFiles.Where(file =>
                string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase));
            evidence.AddRange(provider.Discover(csharpFiles, modulePath));
        }

        context.MessageIntegrationEvidenceRegistry =
            new IntegrationEvidenceRegistry<MessageIntegrationEvidence>(evidence);
    }

    private static void DiscoverRpcIntegrations(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> files)
    {
        var evidence = new List<RpcIntegrationEvidence>();
        var provider = new ProtobufRpcContractProvider();

        foreach (var module in context.Modules.Where(module =>
                     !string.IsNullOrWhiteSpace(module.Path)))
        {
            var modulePath = Path.GetFullPath(module.Path);
            var moduleFiles = files.Where(file =>
            {
                var relative = Path.GetRelativePath(modulePath, Path.GetFullPath(file));
                return !Path.IsPathRooted(relative)
                    && relative != ".."
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
            });

            var ownedFiles = moduleFiles.ToArray();
            var moduleContracts = provider.Discover(
                ownedFiles.Where(file =>
                    string.Equals(Path.GetExtension(file), ".proto", StringComparison.OrdinalIgnoreCase)),
                modulePath);
            evidence.AddRange(moduleContracts);

            if (module.Languages.Contains("C#", StringComparer.OrdinalIgnoreCase))
            {
                var grpcClientProvider = new CSharpGrpcClientCallProvider();
                evidence.AddRange(grpcClientProvider.Discover(
                    ownedFiles.Where(file =>
                        string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase)),
                    modulePath,
                    moduleContracts));
            }
        }

        context.RpcIntegrationEvidenceRegistry =
            new IntegrationEvidenceRegistry<RpcIntegrationEvidence>(evidence);
    }

    private static void DiscoverDataAccessIntegrations(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> files)
    {
        var evidence = new List<DataAccessEvidence>();
        var csharpProvider = new CSharpEntityFrameworkDataAccessProvider();

        foreach (var module in context.Modules.Where(module =>
                     !string.IsNullOrWhiteSpace(module.Path)
                     && module.Languages.Contains("C#", StringComparer.OrdinalIgnoreCase)))
        {
            var modulePath = Path.GetFullPath(module.Path);
            var moduleFiles = files.Where(file =>
            {
                var relative = Path.GetRelativePath(modulePath, Path.GetFullPath(file));
                return !Path.IsPathRooted(relative)
                    && relative != ".."
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
            });
            var csharpFiles = moduleFiles.Where(file =>
                string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase));
            evidence.AddRange(csharpProvider.Discover(csharpFiles, modulePath));
        }

        context.DataAccessEvidenceRegistry =
            new IntegrationEvidenceRegistry<DataAccessEvidence>(evidence);
    }

    private static void LoadApplicationIntegrations(ProjectArchitectureContext context)
    {
        try
        {
            context.ApplicationIntegrations =
                ApplicationIntegrationConfigurationStore.Load(context.RootPath);

            foreach (var integration in context.ApplicationIntegrations)
            {
                var source = context.Modules.FirstOrDefault(module =>
                    PathsEqual(module.Path, integration.SourceModulePath));
                var target = context.Modules.FirstOrDefault(module =>
                    PathsEqual(module.Path, integration.TargetModulePath));

                integration.EndpointResolutionStatus =
                    source is not null && target is not null
                        ? "Resolved"
                        : "Unresolved";

                if (source is not null)
                    integration.SourceModuleName = source.Name;
                if (target is not null)
                    integration.TargetModuleName = target.Name;

                if (integration.EndpointResolutionStatus == "Unresolved")
                {
                    context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
                    {
                        Code = "ApplicationIntegrationEndpointUnresolved",
                        Component = nameof(ApplicationIntegrationConfigurationStore),
                        Message = "A user-defined application integration references a module that was not discovered in this assessment.",
                        Target = $"{integration.SourceModulePath} -> {integration.TargetModulePath}",
                        AffectsCoverage = false
                    });
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or NotSupportedException)
        {
            context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
            {
                Code = "ApplicationIntegrationConfigurationReadFailed",
                Component = nameof(ApplicationIntegrationConfigurationStore),
                Message = $"User-defined application integrations could not be loaded: {exception.Message}",
                Target = ApplicationIntegrationConfigurationStore.GetConfigurationPath(context.RootPath),
                ExceptionType = exception.GetType().Name,
                AffectsCoverage = false
            });
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }

    private static void RecordExcludedEntry(
        IDictionary<string, int> excludedEntriesByReason,
        string reason)
    {
        excludedEntriesByReason[reason] =
            excludedEntriesByReason.TryGetValue(reason, out var count)
                ? count + 1
                : 1;
    }

    private static void PopulateContextMetadata(
        ProjectArchitectureContext ctx,
        List<string> files)
    {
        ctx.MetadataMap["ProjectName"] =
            ctx.ProjectName;


        ctx.MetadataMap["FileCount"] =
            ctx.FileCount.ToString();

        ctx.MetadataMap["ExcludedPathEntryCount"] =
            ctx.ExcludedEntriesByReason.Values.Sum().ToString();

        ctx.MetadataMap["ExcludedEntriesByReason"] =
            JsonSerializer.Serialize(ctx.ExcludedEntriesByReason);


        ctx.MetadataMap["FrontendFrameworkDiscoveryComplete"] =
            ctx.FrontendFrameworkDiscoveryComplete.ToString().ToLowerInvariant();

        ctx.MetadataMap["DetectedDependencies"] =
            ctx.DetectedDependencies.Count.ToString();

        ctx.MetadataMap["ProductionDependencyCount"] =
            ctx.ProductionDependencies.Count.ToString();

        ctx.MetadataMap["TestDependencyCount"] =
            ctx.TestDependencies.Count.ToString();


        ctx.MetadataMap["DetectedLayers"] =
            ctx.Layers.Count.ToString();


        ctx.MetadataMap["DetectedModules"] =
            ctx.ProductionModules.Count.ToString();

        ctx.MetadataMap["DetectedTestProjects"] =
            ctx.TestModules.Count.ToString();

        ctx.MetadataMap["DetectedTestSupportModules"] =
            ctx.TestSupportModules.Count.ToString();

        ctx.MetadataMap["ProjectKindBreakdown"] =
            JsonSerializer.Serialize(ctx.Modules
                .GroupBy(module => module.ProjectKind, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count()));

        ctx.MetadataMap["DetectedProjectFrameworks"] =
            JsonSerializer.Serialize(ctx.Modules
                .Select(module => new
                {
                    module.Name,
                    module.ProjectKind,
                    module.Framework,
                    module.Frameworks,
                    module.TargetFrameworks,
                    module.IsTestProject,
                    module.IsTestSupport,
                    module.IsWebProject
                }));

        ctx.MetadataMap["LastModified"] =
            files
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.UtcNow)
                .Max()
                .ToString("O");
    }
}