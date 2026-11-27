using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Projects provider-resolved semantic relationships onto language-level architectural
/// components. Self-edges retain relationships internal to one code container; this graph remains distinct from build-module edges.
/// </summary>
internal static class SemanticArchitectureComponentDetector
{
    private const string DependencyType = "SemanticComponentDependency";

    private static readonly HashSet<SemanticRelationshipKind> ComponentRelationshipKinds =
    [
        SemanticRelationshipKind.References,
        SemanticRelationshipKind.Calls,
        SemanticRelationshipKind.Inherits,
        SemanticRelationshipKind.Implements,
        SemanticRelationshipKind.DependsOn,
        SemanticRelationshipKind.UsesAnnotation,
        SemanticRelationshipKind.Injects,
        SemanticRelationshipKind.Registers,
        SemanticRelationshipKind.LoadsDynamically
    ];

    public static void Analyze(ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var semanticRunEntries = context.SemanticAnalysisRuns
            .Select((run, runIndex) => (Run: run, RunIndex: runIndex))
            .Where(entry => IsUsableSemanticRun(entry.Run))
            .ToArray();
        var semanticRuns = semanticRunEntries.Select(entry => entry.Run).ToArray();
        var evidenceRegistry = SemanticRelationshipEvidenceRegistry.Create(context.SemanticAnalysisRuns);
        var symbolIndex = SemanticSymbolIndex.Create(context.SemanticAnalysisRuns);
        context.RelationshipEvidenceRegistry = evidenceRegistry;
        context.SemanticSymbolIndex = symbolIndex;
        context.SemanticRelationshipNormalizationRegistry =
            SemanticRelationshipNormalizationRegistry.Create(evidenceRegistry, symbolIndex);

        // Client request declarations are source evidence, not graph edges.
        // Semantic runs are authoritative for native-provider observations.
        // Remove their previous registry projections by object identity, then
        // append every run occurrence so duplicate provider IDs remain distinct.
        var semanticClientEvidence = context.SemanticAnalysisRuns
            .SelectMany(run => run.ApiClientRequests)
            .ToArray();
        var semanticClientEvidenceSet = new HashSet<ApiClientRequestEvidence>(
            semanticClientEvidence,
            ReferenceEqualityComparer.Instance);
        var existingClientEvidence = context.ApiClientRequestDiscoveryRegistry?.Evidence
            .Where(evidence => !semanticClientEvidenceSet.Contains(evidence))
            ?? Enumerable.Empty<ApiClientRequestEvidence>();
        context.ApiClientRequestDiscoveryRegistry =
            new ApiClientRequestDiscoveryRegistry(existingClientEvidence.Concat(semanticClientEvidence));

        if (semanticRuns.Length == 0)
            return;

        context.ArchitectureComponentDependencies.RemoveAll(dependency =>
            dependency.DependencyType.Equals(DependencyType, StringComparison.OrdinalIgnoreCase));
        context.ArchitectureComponentInternalDependencies.Clear();

        // Semantic runs are already scoped to a build module. Their ProjectPath
        // is the authoritative owner identity; the discovered module inventory
        // enriches the display name but must not gate package discovery. In
        // particular, duplicate or incomplete module records must not erase a
        // valid language-level component graph.
        var modulesByPath = context.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .GroupBy(module => NormalizePath(module.Path), PathComparer)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                PathComparer);

        ProjectModuleContext ResolveModule(string path, string language)
        {
            var modulePath = NormalizePath(path);
            if (modulesByPath.TryGetValue(modulePath, out var candidates))
            {
                var languageMatch = candidates.FirstOrDefault(module =>
                    module.Languages.Contains(language, StringComparer.OrdinalIgnoreCase));
                var androidMatch = candidates.FirstOrDefault(module =>
                    module.GradleBuildModel?.Android is not null
                    && module.Languages.Contains(language, StringComparer.OrdinalIgnoreCase));
                return androidMatch ?? languageMatch ?? candidates[0];
            }

            var moduleName = Path.GetFileName(modulePath);
            return new ProjectModuleContext
            {
                Name = string.IsNullOrWhiteSpace(moduleName) ? context.ProjectName : moduleName,
                Path = modulePath,
                Languages = [language]
            };
        }

        var semanticLanguages = semanticRuns
            .Select(run => run.Language)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Symbol IDs are opaque and project-scoped by the shared semantic contract.
        // Keep module identity in the key; a workspace-wide ID-only map can collapse
        // unrelated declarations when different modules emit the same ID.
        var symbolComponents = semanticRunEntries
            .SelectMany(entry =>
            {
                var run = entry.Run;
                var module = ResolveModule(run.ProjectPath, run.Language);
                return symbolIndex.ForRun(entry.RunIndex)
                    .Select(occurrence => occurrence.Symbol)
                    .Where(IsProjectSourceSymbol)
                    .Where(symbol =>
                        !string.IsNullOrWhiteSpace(symbol.Id)
                        && !string.IsNullOrWhiteSpace(symbol.FilePath)
                        && (!string.IsNullOrWhiteSpace(symbol.ContainerKind)
                            || !string.IsNullOrWhiteSpace(symbol.ContainerName)))
                    .Select(symbol =>
                    {
                        var componentKind = InferComponentKind(run.Language, symbol.ContainerKind);
                        var componentName = componentKind.Equals("Project", StringComparison.OrdinalIgnoreCase)
                            ? module.Name
                            : string.IsNullOrWhiteSpace(symbol.ContainerName)
                                ? "<default>"
                                : symbol.ContainerName.Trim();
                        var modulePath = NormalizePath(run.ProjectPath);
                        var componentId = CreateComponentId(modulePath, run.Language, componentKind, componentName);
                        var sourceOwnership = ResolveAndroidSourceOwnership(module, symbol.FilePath!, run.Language);
                        if (symbol.IsTestCode || module.IsTestProject || module.IsTestSupport)
                            sourceOwnership = sourceOwnership with { Ownership = "Test" };
                        var isTestCode = sourceOwnership.Ownership == "Test";
                        return new SymbolComponent(
                            symbol.Id,
                            symbol.QualifiedName,
                            componentId,
                            componentName,
                            componentKind,
                            module.Name,
                            modulePath,
                            run.Language,
                            Path.GetFullPath(symbol.FilePath!),
                            isTestCode,
                            sourceOwnership.Ownership,
                            sourceOwnership.SourceSetName,
                            sourceOwnership.Variants,
                            sourceOwnership.ModuleKind,
                            sourceOwnership.TestKind);
                    });
            })
            .GroupBy(
                item => SemanticSymbolIdentity.Create(item.Language, item.ModulePath, item.SymbolId),
                SemanticSymbolIdentity.Comparer)
            .Select(group => new
            {
                group.Key,
                Components = group
                    .GroupBy(item => item.ComponentId, StringComparer.Ordinal)
                    .Select(componentGroup => componentGroup.First())
                    .ToArray()
            })
            .Where(item => item.Components.Length == 1)
            .ToDictionary(
                item => item.Key,
                item => item.Components[0],
                SemanticSymbolIdentity.Comparer);

        var componentNodes = symbolComponents.Values
            .GroupBy(component => component.ComponentId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var productionSymbols = group.Where(item => item.SourceOwnership == "Production").ToArray();
                var testSymbols = group.Where(item => item.SourceOwnership == "Test").ToArray();
                var files = group.Select(item => item.FilePath)
                    .Distinct(PathComparer)
                    .OrderBy(path => path, PathComparer)
                    .ToList();

                return new ArchitectureComponentContext
                {
                    Id = first.ComponentId,
                    Name = GetComponentDisplayName(first.ComponentName),
                    QualifiedName = first.ComponentName,
                    Language = first.Language,
                    ComponentKind = first.ComponentKind,
                    BuildModuleName = first.ModuleName,
                    BuildModulePath = first.ModulePath,
                    AndroidModuleKind = first.AndroidModuleKind,
                    AndroidVariants = group.SelectMany(item => item.AndroidVariants)
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    AndroidSourceSets = group.Select(item => item.SourceSetName)
                        .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    SourceOwnerships = group.Select(item => item.SourceOwnership)
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    AndroidTestKinds = group.Select(item => item.AndroidTestKind)
                        .Where(value => !string.IsNullOrWhiteSpace(value)
                            && !value.Equals(nameof(GradleAndroidTestKind.None), StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    SourceFileCount = files.Count,
                    ProductionSourceFileCount = productionSymbols
                        .Select(item => item.FilePath)
                        .Distinct(PathComparer)
                        .Count(),
                    TestSourceFileCount = testSymbols
                        .Select(item => item.FilePath)
                        .Distinct(PathComparer)
                        .Count(),
                    SymbolCount = group.Select(item => item.SymbolId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    ProductionSymbolCount = productionSymbols
                        .Select(item => item.SymbolId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    TestSymbolCount = testSymbols
                        .Select(item => item.SymbolId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    SourceFiles = files
                };
            })
            .OrderBy(component => component.BuildModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        context.ArchitectureComponents.RemoveAll(component => semanticLanguages.Contains(component.Language));
        context.ArchitectureComponents.AddRange(componentNodes);

        var edges = new Dictionary<EdgeKey, EdgeAccumulator>();
        var projectionMetrics = new List<(SemanticAnalysisRun Run, RunProjectionMetrics Metrics)>();

        foreach (var (run, runIndex) in semanticRunEntries)
        {
            var metrics = new RunProjectionMetrics();
            projectionMetrics.Add((run, metrics));

            var sourceModule = ResolveModule(run.ProjectPath, run.Language);
            var runSymbols = symbolIndex.ForRun(runIndex)
                .Select(occurrence => occurrence.Symbol)
                .Where(IsProjectSourceSymbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Id))
                .GroupBy(symbol => symbol.Id, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

            var candidateEvidence = evidenceRegistry.ForRun(runIndex)
                .Where(item => ComponentRelationshipKinds.Contains(item.Relationship.Kind))
                .ToArray();
            metrics.ObservedRelationships = candidateEvidence.Length;

            foreach (var evidence in candidateEvidence.Where(item =>
                         (item.Relationship.ResolutionStatus is SemanticResolutionStatus.Resolved
                             or SemanticResolutionStatus.PartiallyResolved)
                         && !string.IsNullOrWhiteSpace(item.Relationship.TargetSymbolId)))
            {
                var relationship = evidence.Relationship;
                metrics.ResolvedCandidates++;

                if (!runSymbols.TryGetValue(relationship.SourceSymbolId, out var sourceSymbol))
                {
                    metrics.MissingSourceSymbols++;
                    continue;
                }

                if (!symbolComponents.TryGetValue(
                        SemanticSymbolIdentity.Create(
                            string.IsNullOrWhiteSpace(sourceSymbol.Language) ? run.Language : sourceSymbol.Language,
                            run.ProjectPath,
                            relationship.SourceSymbolId),
                        out var sourceComponent))
                {
                    metrics.MissingSourceComponents++;
                    continue;
                }

                var targetComponent = ResolveTargetComponent(
                    context,
                    run,
                    relationship.TargetSymbolId!,
                    sourceComponent,
                    relationship.IsTestCode || sourceSymbol.IsTestCode
                        || sourceComponent.SourceOwnership == "Test"
                        || sourceModule.IsTestProject
                        || sourceModule.IsTestSupport,
                    symbolComponents,
                    symbolIndex);
                if (targetComponent is null)
                {
                    metrics.UnresolvedTargets++;
                    continue;
                }

                if (!TryResolveCompatibleAndroidVariants(
                        sourceComponent,
                        targetComponent,
                        out var compatibleAndroidVariants))
                {
                    metrics.IncompatibleVariants++;
                    continue;
                }

                var targetModule = ResolveModule(targetComponent.ModulePath, targetComponent.Language);
                var isTest = relationship.IsTestCode
                    || sourceSymbol.IsTestCode
                    || sourceComponent.SourceOwnership == "Test"
                    || targetComponent.SourceOwnership == "Test"
                    || sourceComponent.IsTestCode
                    || targetComponent.IsTestCode
                    || sourceModule.IsTestProject
                    || sourceModule.IsTestSupport
                    || targetModule?.IsTestProject == true
                    || targetModule?.IsTestSupport == true;

                var hasUnknownAndroidOwnership = sourceComponent.SourceOwnership == "Unknown"
                    || targetComponent.SourceOwnership == "Unknown";
                var scope = hasUnknownAndroidOwnership
                    ? "Unknown"
                    : isTest ? "Test" : "Production";
                var variantKey = string.Join("|", compatibleAndroidVariants);
                var key = new EdgeKey(sourceComponent.ComponentId, targetComponent.ComponentId, scope, variantKey);
                if (!edges.TryGetValue(key, out var accumulator))
                {
                    accumulator = new EdgeAccumulator(sourceComponent, targetComponent, compatibleAndroidVariants);
                    edges.Add(key, accumulator);
                }

                accumulator.Add(evidence);
                metrics.ProjectedRelationships++;
            }
        }

        foreach (var (run, metrics) in projectionMetrics)
        {
            run.Diagnostics.RemoveAll(diagnostic =>
                diagnostic.Code == "SemanticArchitectureComponentProjectionBaseline");
            run.Diagnostics.Add(new AnalysisDiagnostic
            {
                Code = "SemanticArchitectureComponentProjectionBaseline",
                Component = nameof(SemanticArchitectureComponentDetector),
                Source = "SemanticArchitectureComponentProjection",
                Language = run.Language,
                EngineId = run.EngineId,
                RunId = run.RunId,
                ProjectPath = run.ProjectPath,
                Message =
                    $"Observed relationships={metrics.ObservedRelationships}; "
                    + $"resolved candidates={metrics.ResolvedCandidates}; "
                    + $"unresolved or targetless observations={metrics.ObservedRelationships - metrics.ResolvedCandidates}; "
                    + $"missing source symbols={metrics.MissingSourceSymbols}; "
                    + $"missing source components={metrics.MissingSourceComponents}; "
                    + $"unresolved or ambiguous targets={metrics.UnresolvedTargets}; "
                    + $"incompatible target variants={metrics.IncompatibleVariants}; "
                    + $"projected relationships={metrics.ProjectedRelationships}.",
                Target = run.ProjectPath,
                AffectsCoverage = false
            });
        }

        var projectedEdges = edges
            .Select(pair => pair.Value.ToContext(pair.Key.Scope))
            .OrderBy(edge => edge.SourceComponentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.TargetComponentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        context.ArchitectureComponentDependencies.AddRange(
            projectedEdges.Where(edge => !edge.IsInternal));
        context.ArchitectureComponentInternalDependencies.AddRange(
            projectedEdges.Where(edge => edge.IsInternal));

        var sourceSymbolCount = semanticRuns.Sum(run => run.Symbols.Count(IsProjectSourceSymbol));
        var componentSymbolCount = semanticRuns.Sum(run => run.Symbols.Count(symbol =>
            IsProjectSourceSymbol(symbol)
                && (!string.IsNullOrWhiteSpace(symbol.ContainerKind)
                    || !string.IsNullOrWhiteSpace(symbol.ContainerName))));
        var candidateRelationshipCount = semanticRunEntries.Sum(entry =>
            evidenceRegistry.ForRun(entry.RunIndex).Count(item =>
                ComponentRelationshipKinds.Contains(item.Relationship.Kind)
                && (item.Relationship.ResolutionStatus is SemanticResolutionStatus.Resolved
                    or SemanticResolutionStatus.PartiallyResolved)
                && !string.IsNullOrWhiteSpace(item.Relationship.TargetSymbolId)));

        if (componentNodes.Count == 0 && sourceSymbolCount > 0)
        {
            foreach (var run in semanticRuns.Where(run =>
                         run.Symbols.Any(IsProjectSourceSymbol)
                         && !run.Diagnostics.Any(diagnostic =>
                             diagnostic.Code == "SemanticArchitectureComponentProjectionEmpty")))
            {
                run.Diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "SemanticArchitectureComponentProjectionEmpty",
                    Component = nameof(SemanticArchitectureComponentDetector),
                    Message = $"Semantic analysis produced {run.Symbols.Count(IsProjectSourceSymbol)} source symbols, but none could be projected into architectural components. Component symbols={componentSymbolCount}; projected components={componentNodes.Count}.",
                    Target = run.ProjectPath,
                    AffectsCoverage = true
                });
            }
        }

        AegisDiagnostics.Report(
            nameof(SemanticArchitectureComponentDetector),
            componentSymbolCount == 0 && sourceSymbolCount > 0 ? DiagnosticLevel.Warning : DiagnosticLevel.Info,
            $"Semantic architectural component projection: runs={semanticRuns.Length}; sourceSymbols={sourceSymbolCount}; componentSymbols={componentSymbolCount}; components={componentNodes.Count}; candidateRelationships={candidateRelationshipCount}; unresolvedObservations={projectionMetrics.Sum(item => item.Metrics.ObservedRelationships - item.Metrics.ResolvedCandidates)}; unresolvedTargets={projectionMetrics.Sum(item => item.Metrics.UnresolvedTargets)}; incompatibleVariants={projectionMetrics.Sum(item => item.Metrics.IncompatibleVariants)}; projectedRelationships={projectionMetrics.Sum(item => item.Metrics.ProjectedRelationships)}; projectedEdges={edges.Count}; productionEdges={context.ProductionComponentDependencies.Count}; testEdges={context.TestComponentDependencies.Count}.");
    }

    private static SymbolComponent? ResolveTargetComponent(
        ProjectArchitectureContext context,
        SemanticAnalysisRun run,
        string targetSymbolId,
        SymbolComponent sourceComponent,
        bool includeTestDependencies,
        IReadOnlyDictionary<SemanticSymbolIdentity, SymbolComponent> symbolComponents,
        SemanticSymbolIndex symbolIndex)
    {
        // External symbols emitted by a provider describe the resolved target,
        // but ProjectPath may still identify the consumer analysis context.
        // Use a distinct path as an exact owner only when it differs from the run.
        var externalSymbols = symbolIndex.FindByProviderId(targetSymbolId)
            .Where(occurrence => ReferenceEquals(occurrence.Run, run)
                && occurrence.Symbol.Origin == SemanticSymbolOrigin.External)
            .Select(occurrence => occurrence.Symbol)
            .ToArray();

        if (externalSymbols.Length > 0)
        {
            var candidates = new Dictionary<SemanticSymbolIdentity, SymbolComponent>(SemanticSymbolIdentity.Comparer);
            var sourceProjectPath = NormalizePath(run.ProjectPath);
            var reachableDependencyPaths = ResolveReachableDependencyPaths(
                context.Dependencies,
                sourceProjectPath,
                includeTestDependencies);

            foreach (var externalSymbol in externalSymbols.Where(symbol =>
                         !string.IsNullOrWhiteSpace(symbol.OwningProjectPath)))
            {
                if (!SemanticSymbolIdentity.TryCreate(run, externalSymbol, out var exactIdentity)
                    || exactIdentity is null)
                {
                    continue;
                }

                if (symbolComponents.TryGetValue(exactIdentity, out var exactTarget)
                    && TryResolveCompatibleAndroidVariants(sourceComponent, exactTarget, out _))
                {
                    candidates[exactIdentity] = exactTarget;
                }
            }

            // An explicit owner is authoritative. If it cannot be matched to a
            // declaration in that project, do not silently fall back to a
            // different project merely because its qualified name happens to match.
            if (externalSymbols.Any(symbol => !string.IsNullOrWhiteSpace(symbol.OwningProjectPath)))
                return candidates.Count == 1 ? candidates.Values.Single() : null;

            foreach (var externalSymbol in externalSymbols)
            {
                if (string.IsNullOrWhiteSpace(externalSymbol.QualifiedName))
                    continue;

                var targetLanguage = string.IsNullOrWhiteSpace(externalSymbol.Language)
                    ? run.Language
                    : externalSymbol.Language;
                var projectCandidates = symbolIndex.FindByQualifiedName(
                        targetLanguage,
                        externalSymbol.QualifiedName)
                    .Where(occurrence => IsUsableSemanticRun(occurrence.Run)
                        && IsProjectSourceSymbol(occurrence.Symbol)
                        && occurrence.Identity is not null)
                    .Select(occurrence => occurrence.Identity!)
                    .Distinct(SemanticSymbolIdentity.Comparer)
                    .Where(identity => symbolComponents.ContainsKey(identity))
                    .Select(identity => symbolComponents[identity])
                    .Where(component => !PathComparer.Equals(component.ModulePath, sourceProjectPath))
                    .Where(component => reachableDependencyPaths.Contains(component.ModulePath))
                    .ToArray();

                // A qualified name plus a reachable module does not identify a
                // particular Android variant. Filtering by intersection first is
                // unsafe: it can make a debug reference appear to target the debug
                // declaration even when the compiler actually resolved a release
                // declaration. Require one owner/declaration candidate before
                // applying variant compatibility; otherwise fail closed.
                if (projectCandidates.Length != 1)
                    continue;

                var candidate = projectCandidates[0];
                if (!TryResolveCompatibleAndroidVariants(sourceComponent, candidate, out _))
                    continue;

                var identity = SemanticSymbolIdentity.Create(candidate.Language, candidate.ModulePath, candidate.SymbolId);
                candidates[identity] = candidate;
            }

            return candidates.Count == 1 ? candidates.Values.Single() : null;
        }

        // Same-run source declarations are already scoped by the run's project.
        var sameProjectTarget = SemanticSymbolIdentity.Create(
            run.Language,
            run.ProjectPath,
            targetSymbolId);
        if (symbolComponents.TryGetValue(sameProjectTarget, out var localTarget)
            && TryResolveCompatibleAndroidVariants(sourceComponent, localTarget, out _))
        {
            return localTarget;
        }

        // Preserve the existing Java/C# relationship contract when the target ID
        // is unique across the workspace. Never choose arbitrarily across projects.
        var uniqueIdentities = symbolIndex.FindByProviderId(targetSymbolId)
            .Where(occurrence => IsUsableSemanticRun(occurrence.Run)
                && IsProjectSourceSymbol(occurrence.Symbol)
                && occurrence.Identity is not null)
            .Select(occurrence => occurrence.Identity!)
            .Distinct(SemanticSymbolIdentity.Comparer)
            .Take(2)
            .ToArray();

        return uniqueIdentities.Length == 1
            && symbolComponents.TryGetValue(uniqueIdentities[0], out var uniqueTarget)
                ? uniqueTarget
                : null;
    }

    private static bool TryResolveCompatibleAndroidVariants(
        SymbolComponent source,
        SymbolComponent target,
        out IReadOnlyList<string> compatibleVariants)
    {
        compatibleVariants = [];

        var sourceIsAndroid = !string.IsNullOrWhiteSpace(source.AndroidModuleKind);
        var targetIsAndroid = !string.IsNullOrWhiteSpace(target.AndroidModuleKind);

        if (!sourceIsAndroid && !targetIsAndroid)
            return true;

        // Preserve the known Android endpoint's variant provenance when the
        // opposite endpoint is a non-Android JVM module.
        if (sourceIsAndroid && !targetIsAndroid)
        {
            if (source.AndroidVariants.Count == 0)
                return false;

            compatibleVariants = source.AndroidVariants
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return true;
        }

        if (!sourceIsAndroid && targetIsAndroid)
        {
            if (target.AndroidVariants.Count == 0)
                return false;

            compatibleVariants = target.AndroidVariants
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return true;
        }

        // Missing variant evidence is not proof of compatibility. Do not project
        // an Android-to-Android edge based only on a qualified-name match.
        if (source.AndroidVariants.Count == 0 || target.AndroidVariants.Count == 0)
            return false;

        var intersection = source.AndroidVariants
            .Intersect(target.AndroidVariants, StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (intersection.Length == 0)
            return false;

        compatibleVariants = intersection;
        return true;
    }

    private static HashSet<string> ResolveReachableDependencyPaths(
        IReadOnlyCollection<ArchitectureDependencyContext> dependencies,
        string sourceProjectPath,
        bool includeTestDependencies)
    {
        var reachable = new HashSet<string>(PathComparer);
        var pending = new Queue<string>();
        pending.Enqueue(sourceProjectPath);

        while (pending.TryDequeue(out var currentPath))
        {
            foreach (var dependency in dependencies.Where(dependency =>
                         !string.IsNullOrWhiteSpace(dependency.SourceProjectPath)
                         && !string.IsNullOrWhiteSpace(dependency.TargetProjectPath)
                         && PathComparer.Equals(NormalizePath(dependency.SourceProjectPath), currentPath)
                         && (dependency.Scope.Equals("Production", StringComparison.OrdinalIgnoreCase)
                             || includeTestDependencies
                                 && dependency.Scope.Equals("Test", StringComparison.OrdinalIgnoreCase))))
            {
                var targetPath = NormalizePath(dependency.TargetProjectPath);
                if (PathComparer.Equals(targetPath, sourceProjectPath)
                    || !reachable.Add(targetPath))
                {
                    continue;
                }

                pending.Enqueue(targetPath);
            }
        }

        return reachable;
    }

    private static AndroidSourceOwnership ResolveAndroidSourceOwnership(
        ProjectModuleContext module,
        string filePath,
        string language)
    {
        var android = module.GradleBuildModel?.Android;
        if (android is null)
        {
            var isAndroidModule = string.Equals(module.Framework, "Android", StringComparison.OrdinalIgnoreCase)
                || module.Frameworks.Contains("Android", StringComparer.OrdinalIgnoreCase);
            var ownership = isAndroidModule
                ? "Unknown"
                : module.IsTestProject || module.IsTestSupport ? "Test" : "Production";
            return new AndroidSourceOwnership(ownership, null, [], null, null);
        }

        var normalizedFile = NormalizePath(filePath);
        var matchingSourceSets = android.SourceSets
            .Select(sourceSet => new
            {
                SourceSet = sourceSet,
                Directories = sourceSet.JavaSourceDirectories
                    .Concat(sourceSet.KotlinSourceDirectories)
                    .Concat(sourceSet.GeneratedJavaSourceDirectories)
                    .Concat(sourceSet.GeneratedKotlinSourceDirectories)
                    .Where(directory => !string.IsNullOrWhiteSpace(directory))
                    .Select(NormalizePath)
                    .Distinct(PathComparer)
                    .Where(directory => IsPathWithin(normalizedFile, directory))
                    .OrderByDescending(directory => directory.Length)
                    .ToArray()
            })
            .Where(item => item.Directories.Length > 0)
            .OrderByDescending(item => item.Directories[0].Length)
            .ToArray();

        if (matchingSourceSets.Length == 0
            || matchingSourceSets.Length > 1
            && matchingSourceSets[0].Directories[0].Length == matchingSourceSets[1].Directories[0].Length)
        {
            return new AndroidSourceOwnership("Unknown", null, [], android.ModuleKind, null);
        }

        var match = matchingSourceSets[0].SourceSet;
        return new AndroidSourceOwnership(
            match.Ownership.ToString(),
            match.Name,
            match.AppliesToVariants,
            android.ModuleKind,
            match.TestKind.ToString());
    }

    private static bool IsPathWithin(string filePath, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, filePath);
        return !Path.IsPathRooted(relativePath)
            && relativePath != "."
            && relativePath != ".."
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record AndroidSourceOwnership(
        string Ownership,
        string? SourceSetName,
        IReadOnlyList<string> Variants,
        string? ModuleKind,
        string? TestKind);

    private static string CreateComponentId(string modulePath, string language, string componentKind, string componentName) =>
        $"{language.ToLowerInvariant()}-{componentKind.ToLowerInvariant()}:{modulePath}:{componentName}";

    private static bool IsUsableSemanticRun(SemanticAnalysisRun run) =>
        !string.IsNullOrWhiteSpace(run.Language)
        && !string.IsNullOrWhiteSpace(run.EngineId)
        && !string.IsNullOrWhiteSpace(run.ProjectPath)
        && run.Status is SemanticAnalysisStatus.Completed or SemanticAnalysisStatus.PartiallyCompleted;

    private static bool IsProjectSourceSymbol(SemanticSymbol symbol) =>
        symbol.Origin is SemanticSymbolOrigin.Source or SemanticSymbolOrigin.Generated;

    private static string InferComponentKind(string language, string? providerKind)
    {
        // C# architectural boundaries are build projects, not namespaces.
        // Other providers may supply a more precise language-native container kind.
        if (language.Equals("C#", StringComparison.OrdinalIgnoreCase)
            || language.Equals("CSharp", StringComparison.OrdinalIgnoreCase))
        {
            return "Project";
        }

        if (!string.IsNullOrWhiteSpace(providerKind))
            return providerKind.Trim();

        return language.ToLowerInvariant() switch
        {
            "java" or "kotlin" or "python" => "Package",
            "typescript" or "javascript" => "Module",
            _ => "Module"
        };
    }

    private static string GetComponentDisplayName(string qualifiedName)
    {
        var separator = Math.Max(qualifiedName.LastIndexOf('.'), qualifiedName.LastIndexOf('/'));
        return separator < 0 ? qualifiedName : qualifiedName[(separator + 1)..];
    }

    private static string GetComponentLabel(SymbolComponent component) =>
        component.ComponentName;

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class RunProjectionMetrics
    {
        public int ObservedRelationships { get; set; }

        public int ResolvedCandidates { get; set; }

        public int MissingSourceSymbols { get; set; }

        public int MissingSourceComponents { get; set; }

        public int UnresolvedTargets { get; set; }

        public int IncompatibleVariants { get; set; }

        public int ProjectedRelationships { get; set; }
    }

    private sealed record SymbolComponent(
        string SymbolId,
        string QualifiedName,
        string ComponentId,
        string ComponentName,
        string ComponentKind,
        string ModuleName,
        string ModulePath,
        string Language,
        string FilePath,
        bool IsTestCode,
        string SourceOwnership,
        string? SourceSetName,
        IReadOnlyList<string> AndroidVariants,
        string? AndroidModuleKind,
        string? AndroidTestKind);

    private sealed record EdgeKey(string SourceComponentId, string TargetComponentId, string Scope, string AndroidVariantKey);

    private sealed class EdgeAccumulator
    {
        private readonly SymbolComponent _source;
        private readonly SymbolComponent _target;
        private readonly IReadOnlyList<string> _compatibleAndroidVariants;
        private readonly HashSet<string> _evidenceIds = new(StringComparer.Ordinal);
        private readonly List<SemanticRelationshipReference> _relationshipReferences = [];
        private readonly HashSet<SemanticRelationshipKind> _relationshipKinds = [];
        private int _unreferencedRelationshipCount;
        private readonly HashSet<string> _evidenceFiles = new(PathComparer);
        private int _resolvedRelationshipCount;
        private int _partiallyResolvedRelationshipCount;

        public EdgeAccumulator(
            SymbolComponent source,
            SymbolComponent target,
            IReadOnlyList<string> compatibleAndroidVariants)
        {
            _source = source;
            _target = target;
            _compatibleAndroidVariants = compatibleAndroidVariants;
        }

        public void Add(SemanticRelationshipEvidence evidence)
        {
            if (!_evidenceIds.Add(evidence.EvidenceId))
                return;

            var relationship = evidence.Relationship;
            _relationshipReferences.Add(new SemanticRelationshipReference
            {
                RunId = evidence.RunId,
                RelationshipId = relationship.Id,
                RunIndex = evidence.RunIndex,
                RelationshipIndex = evidence.RelationshipIndex
            });

            if (string.IsNullOrWhiteSpace(relationship.Id))
                _unreferencedRelationshipCount++;

            _relationshipKinds.Add(relationship.Kind);
            if (relationship.ResolutionStatus == SemanticResolutionStatus.Resolved)
                _resolvedRelationshipCount++;
            else if (relationship.ResolutionStatus == SemanticResolutionStatus.PartiallyResolved)
                _partiallyResolvedRelationshipCount++;

            foreach (var file in relationship.Provenance
                         .Select(provenance => provenance.FilePath)
                         .Where(file => !string.IsNullOrWhiteSpace(file)))
            {
                _evidenceFiles.Add(Path.GetFullPath(file!));
            }
        }

        public ArchitectureComponentDependencyContext ToContext(string scope) => new()
        {
            SourceComponentId = _source.ComponentId,
            SourceComponentName = GetComponentLabel(_source),
            TargetComponentId = _target.ComponentId,
            TargetComponentName = GetComponentLabel(_target),
            Scope = scope,
            DependencyType = DependencyType,
            IsInternal = string.Equals(
                _source.ComponentId,
                _target.ComponentId,
                StringComparison.Ordinal),
            RelationshipCount = _relationshipReferences.Count,
            ResolvedRelationshipCount = _resolvedRelationshipCount,
            PartiallyResolvedRelationshipCount = _partiallyResolvedRelationshipCount,
            ContributingRelationships = _relationshipReferences
                .OrderBy(reference => reference.RunIndex)
                .ThenBy(reference => reference.RelationshipIndex)
                .ToList(),
            UnreferencedRelationshipCount = _unreferencedRelationshipCount,
            RelationshipKinds = _relationshipKinds.OrderBy(kind => kind).ToList(),
            EvidenceFiles = _evidenceFiles.OrderBy(file => file, PathComparer).ToList(),
            AndroidVariants = _compatibleAndroidVariants.ToList(),
            AndroidSourceSets = new[] { _source.SourceSetName, _target.SourceSetName }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            AndroidModuleKinds = new[] { _source.AndroidModuleKind, _target.AndroidModuleKind }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            AndroidTestKinds = new[] { _source.AndroidTestKind, _target.AndroidTestKind }
                .Where(value => !string.IsNullOrWhiteSpace(value)
                    && !value.Equals(nameof(GradleAndroidTestKind.None), StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }
}
