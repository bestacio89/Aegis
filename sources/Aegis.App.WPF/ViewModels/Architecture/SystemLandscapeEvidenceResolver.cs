using System.IO;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// Resolves graph references against the authoritative registries in one assessment snapshot.
/// Evidence IDs are validation metadata only; the domain and occurrence index establish identity.
/// </summary>
public sealed class SystemLandscapeEvidenceResolver
{
    public IReadOnlyList<SystemLandscapeEvidenceItemViewModel> Resolve(
        ProjectArchitectureContext context,
        IReadOnlyList<ArchitectureGraphEvidenceReference> references)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(references);
        return references.Select(reference => Resolve(context, reference)).ToArray();
    }

    public SystemLandscapeEvidenceItemViewModel Resolve(
        ProjectArchitectureContext context,
        ArchitectureGraphEvidenceReference reference)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);

        return reference.Domain switch
        {
            ArchitectureGraphEvidenceDomain.SemanticRelationship => ResolveSemantic(context, reference),
            ArchitectureGraphEvidenceDomain.BuildDependency => ResolveBuild(context, reference),
            ArchitectureGraphEvidenceDomain.Messaging => ResolveIntegration(context.MessageIntegrationEvidenceRegistry?.Evidence, reference, "Messaging", context.RootPath),
            ArchitectureGraphEvidenceDomain.Rpc => ResolveIntegration(context.RpcIntegrationEvidenceRegistry?.Evidence, reference, "RPC", context.RootPath),
            ArchitectureGraphEvidenceDomain.DataAccess => ResolveIntegration(context.DataAccessEvidenceRegistry?.Evidence, reference, "Data access", context.RootPath),
            ArchitectureGraphEvidenceDomain.ApiClientRequest => ResolveApiRequest(context, reference),
            ArchitectureGraphEvidenceDomain.ApiEndpoint => ResolveApiEndpoint(context, reference),
            _ => Unavailable(reference, "The evidence domain is not supported by this application version.")
        };
    }

    private static SystemLandscapeEvidenceItemViewModel ResolveSemantic(
        ProjectArchitectureContext context,
        ArchitectureGraphEvidenceReference reference)
    {
        var evidence = At(context.RelationshipEvidenceRegistry?.Evidence, reference.OccurrenceIndex);
        if (evidence is null || !IdentityMatches(reference, evidence.EvidenceId, evidence.Relationship.Id))
            return Unavailable(reference, "The semantic relationship occurrence is absent or its identity does not match this snapshot.");

        var paths = evidence.Relationship.Provenance
            .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
            .Select(item => item.FilePath!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var details = $"Run: {evidence.RunId}; language: {evidence.Language}; engine: {evidence.EngineId}; " +
                      $"kind: {evidence.Relationship.Kind}; resolution: {evidence.Relationship.ResolutionStatus}; " +
                      $"target: {evidence.Relationship.TargetSymbolId ?? evidence.Relationship.UnresolvedTarget ?? "unresolved"}.";
        return Create(reference, evidence.EvidenceId, evidence.Relationship.Id, details, paths, context.RootPath);
    }

    private static SystemLandscapeEvidenceItemViewModel ResolveBuild(
        ProjectArchitectureContext context,
        ArchitectureGraphEvidenceReference reference)
    {
        var evidence = At(context.Dependencies, reference.OccurrenceIndex);
        if (evidence is null || !IdentityMatches(reference,
                $"{reference.OccurrenceIndex}:{evidence.DependencyType}", evidence.DependencyType))
            return Unavailable(reference, "The build dependency occurrence is absent or its identity does not match this snapshot.");

        var details = $"{evidence.Source} → {evidence.Target}; type: {evidence.DependencyType}; scope: {evidence.Scope}.";
        return Create(reference, reference.EvidenceId, evidence.DependencyType, details, [evidence.File], context.RootPath);
    }

    private static SystemLandscapeEvidenceItemViewModel ResolveIntegration<T>(
        IReadOnlyList<T>? evidence,
        ArchitectureGraphEvidenceReference reference,
        string label,
        string rootPath)
        where T : class, IIntegrationEvidence
    {
        var occurrence = At(evidence, reference.OccurrenceIndex);
        if (occurrence is null || !IdentityMatches(reference, occurrence.EvidenceId, occurrence.ProviderId))
            return Unavailable(reference, $"The {label} occurrence is absent or its identity does not match this snapshot.");

        var details = $"{label}; language: {occurrence.Language}; framework: {occurrence.Framework}; " +
                      $"module: {occurrence.ModulePath}; relationship: {occurrence.RelationshipKind}; " +
                      $"resolution: {occurrence.ResolutionStatus}. {occurrence.DeclarationEvidence}";
        return Create(reference, occurrence.EvidenceId, occurrence.ProviderId, details, [occurrence.SourceFile], rootPath);
    }

    private static SystemLandscapeEvidenceItemViewModel ResolveApiRequest(
        ProjectArchitectureContext context,
        ArchitectureGraphEvidenceReference reference)
    {
        var occurrence = At(context.ApiClientRequestDiscoveryRegistry?.Evidence, reference.OccurrenceIndex);
        if (occurrence is null || !IdentityMatches(reference, occurrence.EvidenceId, occurrence.ProviderId))
            return Unavailable(reference, "The API client-request occurrence is absent or its identity does not match this snapshot.");

        var details = $"HTTP request; language: {occurrence.Language}; framework: {occurrence.Framework}; " +
                      $"caller: {occurrence.CallerIdentity}; method: {occurrence.HttpMethod ?? "unknown"}; " +
                      $"URI: {occurrence.RequestUriExpression ?? "unresolved"}; resolution: {occurrence.ResolutionStatus}. " +
                      occurrence.DeclarationEvidence;
        return Create(reference, occurrence.EvidenceId, occurrence.ProviderId, details, [occurrence.SourceFile], context.RootPath);
    }

    private static SystemLandscapeEvidenceItemViewModel ResolveApiEndpoint(
        ProjectArchitectureContext context,
        ArchitectureGraphEvidenceReference reference)
    {
        var occurrence = At(context.ServerApiEndpointDiscoveryRegistry?.Evidence, reference.OccurrenceIndex);
        if (occurrence is null || !IdentityMatches(reference, occurrence.EvidenceId, occurrence.ProviderId))
            return Unavailable(reference, "The API endpoint occurrence is absent or its identity does not match this snapshot.");

        var details = $"HTTP endpoint; language: {occurrence.Language}; framework: {occurrence.Framework}; " +
                      $"handler: {occurrence.HandlerIdentity}; methods: {string.Join(", ", occurrence.HttpMethods)}; " +
                      $"route: {occurrence.RouteTemplate ?? "unresolved"}; resolution: {occurrence.ResolutionStatus}. " +
                      occurrence.DeclarationEvidence;
        return Create(reference, occurrence.EvidenceId, occurrence.ProviderId, details, [occurrence.SourceFile], context.RootPath);
    }

    private static SystemLandscapeEvidenceItemViewModel Create(
        ArchitectureGraphEvidenceReference reference,
        string evidenceId,
        string providerId,
        string details,
        IEnumerable<string> paths,
        string repositoryRoot)
    {
        var source = paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        var verifiedPath = TryResolveSourcePath(repositoryRoot, source, out var resolved) ? resolved : null;
        var location = verifiedPath is null
            ? "Source location unavailable or outside the assessed repository."
            : $"Source: {Path.GetRelativePath(repositoryRoot, verifiedPath)}";

        return new SystemLandscapeEvidenceItemViewModel
        {
            Title = reference.Domain.ToString(),
            Identity = $"Occurrence {reference.OccurrenceIndex}; evidence: {evidenceId}; provider: {providerId}",
            Details = details,
            SourceLocation = location,
            VerifiedSourcePath = verifiedPath
        };
    }

    private static SystemLandscapeEvidenceItemViewModel Unavailable(
        ArchitectureGraphEvidenceReference reference,
        string reason) =>
        new()
        {
            Title = reference.Domain.ToString(),
            Identity = $"Occurrence {reference.OccurrenceIndex}; evidence: {reference.EvidenceId}",
            Details = reason,
            SourceLocation = "No source navigation target is available."
        };

    private static T? At<T>(IReadOnlyList<T>? items, int index) where T : class =>
        items is not null && index >= 0 && index < items.Count ? items[index] : null;

    private static bool IdentityMatches(
        ArchitectureGraphEvidenceReference reference,
        string evidenceId,
        string providerId) =>
        (string.IsNullOrEmpty(reference.EvidenceId)
            || string.Equals(reference.EvidenceId, evidenceId, StringComparison.Ordinal))
        && (string.IsNullOrEmpty(reference.ProviderId)
            || string.Equals(reference.ProviderId, providerId, StringComparison.Ordinal));

    public static bool TryResolveSourcePath(
        string? repositoryRoot,
        string? sourcePath,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(repositoryRoot) || string.IsNullOrWhiteSpace(sourcePath))
            return false;

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
            var candidate = Path.GetFullPath(Path.IsPathRooted(sourcePath) ? sourcePath : Path.Combine(root, sourcePath));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var rootPrefix = root + Path.DirectorySeparatorChar;
            if (!string.Equals(candidate, root, comparison) && !candidate.StartsWith(rootPrefix, comparison))
                return false;
            if (!File.Exists(candidate))
                return false;
            resolvedPath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
