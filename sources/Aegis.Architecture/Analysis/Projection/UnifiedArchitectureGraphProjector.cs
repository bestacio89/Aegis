using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Projection;

/// <summary>
/// Builds a deterministic unified graph from existing build, semantic, and
/// integration projections. This class performs no discovery or identity inference.
/// </summary>
internal static partial class UnifiedArchitectureGraphProjector
{
    public static ArchitectureGraphProjection Project(ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var nodes = new Dictionary<string, ArchitectureGraphNode>(StringComparer.Ordinal);
        var edges = new List<ArchitectureGraphEdge>();
        var unprojected = new List<ArchitectureGraphUnprojectedEvidence>();

        var codeComponents = CreateUniqueIndex(
            context.ArchitectureComponents,
            component => component.Id);
        var logicalComponents = CreateUniqueIndex(
            context.ArchitecturalComponents,
            component => component.Id);
        var modules = CreateModuleIndex(context.Modules);

        foreach (var component in codeComponents.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var id = CreateNodeId("code", component.Id);
            nodes.TryAdd(id, new ArchitectureGraphNode
            {
                Id = id,
                Name = component.QualifiedName.Length == 0 ? component.Name : component.QualifiedName,
                Kind = ArchitectureGraphNodeKind.CodeComponent,
                Language = component.Language,
                ComponentKind = component.ComponentKind,
                ModulePath = component.BuildModulePath,
                IsTestScoped = component.TestSymbolCount > 0 && component.ProductionSymbolCount == 0
            });
        }

        foreach (var component in logicalComponents.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var id = CreateNodeId("logical", component.Id);
            nodes.TryAdd(id, new ArchitectureGraphNode
            {
                Id = id,
                Name = component.Name,
                Kind = ArchitectureGraphNodeKind.LogicalComponent,
                ComponentKind = component.Role
            });
        }

        foreach (var module in modules.Values.OrderBy(item => item.Path, PathComparer))
        {
            var id = CreateModuleNodeId(module.Path);
            nodes.TryAdd(id, new ArchitectureGraphNode
            {
                Id = id,
                Name = module.Name,
                Kind = ArchitectureGraphNodeKind.BuildModule,
                ModulePath = module.Path,
                IsTestScoped = IsTestModule(module)
            });
        }

        ProjectBuildDependencies(context, context.Modules, nodes, edges, unprojected);
        ProjectSemanticDependencies(
            context,
            context.ArchitectureComponentDependencies
                .Concat(context.ArchitectureComponentInternalDependencies)
                .Select(item => new SemanticDependencyProjection(
                    item.SourceComponentId,
                    item.TargetComponentId,
                    item.Scope,
                    item.DependencyType,
                    item.AndroidVariants,
                    item.ContributingRelationships)),
            codeComponents.Keys.ToHashSet(StringComparer.Ordinal),
            "code",
            nodes,
            edges,
            unprojected);
        ProjectSemanticDependencies(
            context,
            context.ArchitecturalComponentDependencies
                .Select(item => new SemanticDependencyProjection(
                    item.SourceComponentId,
                    item.TargetComponentId,
                    item.Scope,
                    "LogicalArchitecturalDependency",
                    item.AndroidVariants,
                    item.ContributingRelationships)),
            logicalComponents.Keys.ToHashSet(StringComparer.Ordinal),
            "logical",
            nodes,
            edges,
            unprojected);
        ProjectIntegrationEvidence(context, context.Modules, nodes, edges, unprojected);
        ProjectApiEvidence(context, context.Modules, nodes, edges, unprojected);

        return new ArchitectureGraphProjection(
            nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal),
            edges.OrderBy(edge => edge.Id, StringComparer.Ordinal),
            unprojected
                .OrderBy(item => item.Evidence.Domain)
                .ThenBy(item => item.Evidence.OccurrenceIndex)
                .ThenBy(item => item.Reason));
    }

    private static void ProjectBuildDependencies(
        ProjectArchitectureContext context,
        IEnumerable<ProjectModuleContext> modules,
        IDictionary<string, ArchitectureGraphNode> nodes,
        ICollection<ArchitectureGraphEdge> edges,
        ICollection<ArchitectureGraphUnprojectedEvidence> unprojected)
    {
        for (var index = 0; index < context.Dependencies.Count; index++)
        {
            var dependency = context.Dependencies[index];
            var evidence = new ArchitectureGraphEvidenceReference
            {
                Domain = ArchitectureGraphEvidenceDomain.BuildDependency,
                OccurrenceIndex = index,
                EvidenceId = $"{index}:{dependency.DependencyType}",
                ProviderId = dependency.DependencyType
            };

            var sourceResolved = TryResolveModule(
                dependency.SourceProjectPath,
                modules,
                out var source,
                out var sourceAmbiguous);
            var targetResolved = TryResolveModule(
                dependency.TargetProjectPath,
                modules,
                out var target,
                out var targetAmbiguous);

            if (!sourceResolved || !targetResolved)
            {
                unprojected.Add(new ArchitectureGraphUnprojectedEvidence
                {
                    Evidence = evidence,
                    Reason = sourceAmbiguous || targetAmbiguous
                        ? ArchitectureGraphUnprojectedReason.AmbiguousEndpointOwnership
                        : ArchitectureGraphUnprojectedReason.MissingEndpoint,
                    Explanation = "The build dependency does not resolve to exactly one discovered module at both endpoints."
                });
                continue;
            }

            var relationshipKind = dependency.DependencyType.Contains(
                "ProjectReference",
                StringComparison.OrdinalIgnoreCase)
                ? ArchitectureRelationshipKind.BuildProjectReference
                : ArchitectureRelationshipKind.BuildModuleDependency;

            edges.Add(new ArchitectureGraphEdge
            {
                Id = CreateEdgeId(
                    "build",
                    CreateModuleNodeId(source.Path),
                    CreateModuleNodeId(target.Path),
                    dependency.Scope,
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                SourceNodeId = CreateModuleNodeId(source.Path),
                TargetNodeId = CreateModuleNodeId(target.Path),
                Domain = ArchitectureGraphEdgeDomain.Build,
                Scope = NormalizeScope(dependency.Scope, source, target),
                ProviderRelationshipKind = dependency.DependencyType,
                RelationshipKinds = Array.AsReadOnly(new[] { relationshipKind }),
                Evidence = Array.AsReadOnly(new[] { evidence })
            });
        }
    }

    private static void ProjectSemanticDependencies(
        ProjectArchitectureContext context,
        IEnumerable<SemanticDependencyProjection> dependencies,
        IReadOnlySet<string> componentIds,
        string nodePrefix,
        IDictionary<string, ArchitectureGraphNode> nodes,
        ICollection<ArchitectureGraphEdge> edges,
        ICollection<ArchitectureGraphUnprojectedEvidence> unprojected)
    {
        foreach (var dependency in dependencies
                     .OrderBy(item => item.SourceComponentId, StringComparer.Ordinal)
                     .ThenBy(item => item.TargetComponentId, StringComparer.Ordinal)
                     .ThenBy(item => item.Scope, StringComparer.Ordinal))
        {
            if (!componentIds.Contains(dependency.SourceComponentId)
                || !componentIds.Contains(dependency.TargetComponentId))
                continue;

            var sourceId = CreateNodeId(nodePrefix, dependency.SourceComponentId);
            var targetId = CreateNodeId(nodePrefix, dependency.TargetComponentId);
            if (!nodes.ContainsKey(sourceId) || !nodes.ContainsKey(targetId))
                continue;

            // Package nodes are atomic. A self-edge would expose implementation
            // interactions below the package boundary in the unified graph/ledger.
            if (string.Equals(nodePrefix, "code", StringComparison.Ordinal)
                && string.Equals(sourceId, targetId, StringComparison.Ordinal)
                && string.Equals(nodes[sourceId].ComponentKind, "Package", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var references = new List<ArchitectureGraphEvidenceReference>();
            var kinds = new HashSet<ArchitectureRelationshipKind>();
            var providerKinds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var relationshipReference in dependency.ContributingRelationships)
            {
                var matches = context.RelationshipEvidenceRegistry?.Evidence
                    .Where(item =>
                        item.RunIndex == relationshipReference.RunIndex
                        && item.RelationshipIndex == relationshipReference.RelationshipIndex
                        && string.Equals(item.RunId, relationshipReference.RunId, StringComparison.Ordinal)
                        && string.Equals(item.Relationship.Id, relationshipReference.RelationshipId, StringComparison.Ordinal))
                    .ToArray() ?? [];

                if (matches.Length != 1)
                {
                    unprojected.Add(new ArchitectureGraphUnprojectedEvidence
                    {
                        Evidence = new ArchitectureGraphEvidenceReference
                        {
                            Domain = ArchitectureGraphEvidenceDomain.SemanticRelationship,
                            EvidenceId = $"{relationshipReference.RunId}:{relationshipReference.RelationshipId}"
                        },
                        Reason = ArchitectureGraphUnprojectedReason.AmbiguousEvidenceReference,
                        Explanation = matches.Length == 0
                            ? "The semantic relationship reference no longer resolves to an occurrence in the current snapshot."
                            : "The semantic relationship reference identifies multiple provider occurrences."
                    });
                    continue;
                }

                var match = matches[0];
                if (!string.IsNullOrWhiteSpace(match.Relationship.Kind.ToString()))
                    providerKinds.Add(match.Relationship.Kind.ToString());

                references.Add(new ArchitectureGraphEvidenceReference
                {
                    Domain = ArchitectureGraphEvidenceDomain.SemanticRelationship,
                    OccurrenceIndex = context.RelationshipEvidenceRegistry!.Evidence.ToList().FindIndex(item => ReferenceEquals(item, match)),
                    EvidenceId = match.EvidenceId,
                    ProviderId = match.Relationship.Id
                });

                var normalized = context.SemanticRelationshipNormalizationRegistry?.Evidence
                    .FirstOrDefault(item =>
                        item.Evidence.RunIndex == match.RunIndex
                        && item.Evidence.RelationshipIndex == match.RelationshipIndex);
                if (normalized is not null)
                    kinds.Add(normalized.Kind);
            }

            if (references.Count == 0)
            {
                unprojected.Add(new ArchitectureGraphUnprojectedEvidence
                {
                    Evidence = new ArchitectureGraphEvidenceReference
                    {
                        Domain = ArchitectureGraphEvidenceDomain.SemanticRelationship,
                        EvidenceId = $"{dependency.SourceComponentId}->{dependency.TargetComponentId}"
                    },
                    Reason = ArchitectureGraphUnprojectedReason.UnsupportedRelationship,
                    Explanation = "The existing semantic dependency projection has no uniquely addressable source relationship occurrence."
                });
                continue;
            }

            edges.Add(new ArchitectureGraphEdge
            {
                Id = CreateEdgeId(
                    nodePrefix,
                    sourceId,
                    targetId,
                    dependency.Scope,
                    string.Join(",", references.Select(reference => reference.OccurrenceIndex))),
                SourceNodeId = sourceId,
                TargetNodeId = targetId,
                Domain = ArchitectureGraphEdgeDomain.NativeSemantic,
                Scope = dependency.Scope,
                ProviderRelationshipKind = providerKinds.Count == 0
                    ? dependency.DependencyType
                    : string.Join(", ", providerKinds.OrderBy(kind => kind, StringComparer.Ordinal)),
                RelationshipKinds = Array.AsReadOnly(kinds.OrderBy(kind => kind).ToArray()),
                AndroidVariants = Array.AsReadOnly(dependency.AndroidVariants
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray()),
                Evidence = Array.AsReadOnly(references
                    .OrderBy(reference => reference.OccurrenceIndex)
                    .ToArray())
            });
        }
    }

    private static void ProjectIntegrationEvidence(
        ProjectArchitectureContext context,
        IEnumerable<ProjectModuleContext> modules,
        IDictionary<string, ArchitectureGraphNode> nodes,
        ICollection<ArchitectureGraphEdge> edges,
        ICollection<ArchitectureGraphUnprojectedEvidence> unprojected)
    {
        var catalog = context.IntegrationEvidenceCatalog
            ?? new IntegrationEvidenceCatalog(
                context.MessageIntegrationEvidenceRegistry,
                context.RpcIntegrationEvidenceRegistry,
                context.DataAccessEvidenceRegistry);

        foreach (var occurrence in catalog.Evidence)
        {
            var evidenceReference = new ArchitectureGraphEvidenceReference
            {
                Domain = occurrence.Domain switch
                {
                    IntegrationEvidenceDomain.Messaging => ArchitectureGraphEvidenceDomain.Messaging,
                    IntegrationEvidenceDomain.Rpc => ArchitectureGraphEvidenceDomain.Rpc,
                    IntegrationEvidenceDomain.DataAccess => ArchitectureGraphEvidenceDomain.DataAccess,
                    _ => throw new ArgumentOutOfRangeException(nameof(occurrence.Domain))
                },
                OccurrenceIndex = occurrence.OccurrenceIndex,
                EvidenceId = occurrence.Evidence.EvidenceId,
                ProviderId = occurrence.Evidence.ProviderId
            };

            if (occurrence.Evidence.ResolutionStatus != IntegrationEvidenceResolutionStatus.Resolved)
            {
                unprojected.Add(new ArchitectureGraphUnprojectedEvidence
                {
                    Evidence = evidenceReference,
                    Reason = ArchitectureGraphUnprojectedReason.UnresolvedEvidence,
                    Explanation = $"Integration evidence resolution status is {occurrence.Evidence.ResolutionStatus}; only Resolved observations are projected."
                });
                continue;
            }

            if (!TryResolveModule(occurrence.Evidence.ModulePath, modules, out var module, out var ambiguousOwner))
            {
                unprojected.Add(new ArchitectureGraphUnprojectedEvidence
                {
                    Evidence = evidenceReference,
                    Reason = ambiguousOwner
                        ? ArchitectureGraphUnprojectedReason.AmbiguousEndpointOwnership
                        : ArchitectureGraphUnprojectedReason.MissingEndpoint,
                    Explanation = "The integration observation is not owned by exactly one discovered build module."
                });
                continue;
            }

            var resource = ResolveResource(occurrence);
            if (resource is null)
            {
                unprojected.Add(new ArchitectureGraphUnprojectedEvidence
                {
                    Evidence = evidenceReference,
                    Reason = ArchitectureGraphUnprojectedReason.MissingResourceIdentity,
                    Explanation = "The resolved integration observation has no explicit resource identity for its operation."
                });
                continue;
            }

            var resourceId = CreateResourceNodeId(occurrence.Domain, occurrence.Evidence.ProviderId, resource.Identity);
            nodes.TryAdd(resourceId, new ArchitectureGraphNode
            {
                Id = resourceId,
                Name = resource.Identity,
                Kind = ArchitectureGraphNodeKind.ExternalResource,
                ResourceProvider = occurrence.Evidence.ProviderId,
                ResourceIdentity = resource.Identity,
                ResourceKind = resource.Kind
            });

            var moduleId = CreateModuleNodeId(module.Path);
            var moduleIsSource = resource.Direction != ResourceDirection.Inbound;
            edges.Add(new ArchitectureGraphEdge
            {
                Id = CreateEdgeId(
                    occurrence.Domain.ToString(),
                    moduleIsSource ? moduleId : resourceId,
                    moduleIsSource ? resourceId : moduleId,
                    IsTestModule(module) ? "Test" : "Production",
                    occurrence.OccurrenceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                SourceNodeId = moduleIsSource ? moduleId : resourceId,
                TargetNodeId = moduleIsSource ? resourceId : moduleId,
                Domain = occurrence.Domain switch
                {
                    IntegrationEvidenceDomain.Messaging => ArchitectureGraphEdgeDomain.Messaging,
                    IntegrationEvidenceDomain.Rpc => ArchitectureGraphEdgeDomain.Rpc,
                    IntegrationEvidenceDomain.DataAccess => ArchitectureGraphEdgeDomain.DataAccess,
                    _ => throw new ArgumentOutOfRangeException(nameof(occurrence.Domain))
                },
                Scope = IsTestModule(module) ? "Test" : "Production",
                RelationshipKinds = Array.AsReadOnly(new[] { occurrence.Evidence.RelationshipKind }),
                Evidence = Array.AsReadOnly(new[] { evidenceReference })
            });
        }
    }

    private static ResolvedResource? ResolveResource(IntegrationEvidenceOccurrence occurrence) =>
        occurrence.Evidence switch
        {
            MessageIntegrationEvidence message
                when message.Operation == MessageIntegrationOperation.Consume
                     && !string.IsNullOrWhiteSpace(message.DestinationIdentity)
                => new ResolvedResource(message.DestinationIdentity!, "MessageDestination", ResourceDirection.Inbound),
            MessageIntegrationEvidence message
                when !string.IsNullOrWhiteSpace(message.DestinationIdentity)
                => new ResolvedResource(message.DestinationIdentity!, "MessageDestination", ResourceDirection.Outbound),
            RpcIntegrationEvidence rpc
                when rpc.Operation == RpcIntegrationOperation.Endpoint
                     && !string.IsNullOrWhiteSpace(rpc.ServiceIdentity)
                => new ResolvedResource(rpc.ServiceIdentity!, "RpcService", ResourceDirection.Inbound),
            RpcIntegrationEvidence rpc
                when !string.IsNullOrWhiteSpace(rpc.TargetIdentity)
                => new ResolvedResource(rpc.TargetIdentity!, "RpcTarget", ResourceDirection.Outbound),
            DataAccessEvidence data
                when !string.IsNullOrWhiteSpace(data.StoreIdentity)
                => new ResolvedResource(
                    data.StoreIdentity!,
                    "DataStore",
                    data.Operation == DataIntegrationOperation.Read
                        ? ResourceDirection.Inbound
                        : ResourceDirection.Outbound),
            _ => null
        };

    private static bool TryResolveModule(
        string path,
        IEnumerable<ProjectModuleContext> modules,
        out ProjectModuleContext module,
        out bool ambiguous)
    {
        module = null!;
        ambiguous = false;
        if (!TryNormalizePath(path, out var normalizedPath))
            return false;

        var matches = modules
            .Where(candidate => TryNormalizePath(candidate.Path, out var candidatePath)
                && PathComparer.Equals(candidatePath, normalizedPath))
            .Take(2)
            .ToArray();

        ambiguous = matches.Length > 1;
        if (matches.Length != 1)
            return false;

        module = matches[0];
        return true;
    }

    private static IReadOnlyDictionary<string, ProjectModuleContext> CreateModuleIndex(
        IEnumerable<ProjectModuleContext> source)
    {
        var modules = source
            .Where(module => TryNormalizePath(module.Path, out _))
            .GroupBy(module =>
            {
                TryNormalizePath(module.Path, out var path);
                return path;
            }, PathComparer)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), PathComparer);
        return modules;
    }

    private static IReadOnlyDictionary<string, T> CreateUniqueIndex<T>(
        IEnumerable<T> source,
        Func<T, string> idSelector)
    {
        return source
            .Where(item => !string.IsNullOrWhiteSpace(idSelector(item)))
            .GroupBy(idSelector, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
    }

    private static bool TryNormalizePath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            normalizedPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizeScope(
        string scope,
        ProjectModuleContext source,
        ProjectModuleContext target) =>
        IsTestModule(source) || IsTestModule(target)
            || string.Equals(scope, "Test", StringComparison.OrdinalIgnoreCase)
            ? "Test"
            : "Production";

    private static bool IsTestModule(ProjectModuleContext module) =>
        module.IsTestProject
        || module.IsTestSupport
        || string.Equals(module.Type, "Test", StringComparison.OrdinalIgnoreCase)
        || string.Equals(module.Type, "TestSupport", StringComparison.OrdinalIgnoreCase)
        || string.Equals(module.ProjectKind, "Test", StringComparison.OrdinalIgnoreCase)
        || string.Equals(module.ProjectKind, "TestSupport", StringComparison.OrdinalIgnoreCase);

    private static string CreateModuleNodeId(string path)
    {
        if (!TryNormalizePath(path, out var normalizedPath))
            throw new ArgumentException("A module node requires a valid path.", nameof(path));
        return CreateNodeId("module", normalizedPath);
    }

    private static string CreateResourceNodeId(
        IntegrationEvidenceDomain domain,
        string provider,
        string identity) =>
        CreateNodeId(
            "resource",
            $"{domain}:{provider}:{identity}");

    private static string CreateNodeId(string prefix, string identity) =>
        $"{prefix}:{Uri.EscapeDataString(identity)}";

    private static string CreateEdgeId(
        string domain,
        string source,
        string target,
        string scope,
        string occurrence) =>
        $"{domain}:{Uri.EscapeDataString(source)}->{Uri.EscapeDataString(target)}:{scope}:{occurrence}";

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record SemanticDependencyProjection(
        string SourceComponentId,
        string TargetComponentId,
        string Scope,
        string DependencyType,
        IReadOnlyList<string> AndroidVariants,
        IReadOnlyList<SemanticRelationshipReference> ContributingRelationships);

    private enum ResourceDirection
    {
        Outbound,
        Inbound
    }

    private sealed record ResolvedResource(
        string Identity,
        string Kind,
        ResourceDirection Direction);
}
