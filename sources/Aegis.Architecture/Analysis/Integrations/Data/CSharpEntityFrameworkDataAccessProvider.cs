using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aegis.Architecture.Analysis.Integrations.Data;

/// <summary>
/// Discovers Entity Framework Core query execution and tracked entity mutations
/// from Roslyn-resolved framework symbols. Query composition alone is not access.
/// </summary>
public sealed class CSharpEntityFrameworkDataAccessProvider
{
    private const string DbContextType = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DbSetType = "Microsoft.EntityFrameworkCore.DbSet<T>";

    private static readonly Lazy<IReadOnlyList<MetadataReference>> PlatformReferences =
        new(CreatePlatformReferences);

    private static readonly HashSet<string> ReadMethods = new(StringComparer.Ordinal)
    {
        "ToList", "ToArray", "First", "FirstOrDefault", "Single", "SingleOrDefault",
        "Last", "LastOrDefault", "Any", "All", "Count", "LongCount", "Min", "Max",
        "Sum", "Average", "ToDictionary", "ToHashSet", "Find", "FindAsync",
        "ToListAsync", "ToArrayAsync", "FirstAsync", "FirstOrDefaultAsync",
        "SingleAsync", "SingleOrDefaultAsync", "LastAsync", "LastOrDefaultAsync",
        "AnyAsync", "CountAsync", "LongCountAsync", "ToDictionaryAsync"
    };

    private static readonly HashSet<string> WriteMethods = new(StringComparer.Ordinal)
    {
        "Add", "AddAsync", "AddRange", "AddRangeAsync", "Update", "UpdateRange",
        "Attach", "AttachRange", "Remove", "RemoveRange",
        "ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync"
    };

    private static readonly HashSet<string> BulkWriteMethods = new(StringComparer.Ordinal)
    {
        "ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync"
    };

    public IReadOnlyList<DataAccessEvidence> Discover(
        IEnumerable<string> sourceFiles,
        string modulePath)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);

        var module = Path.GetFullPath(modulePath);
        var files = sourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .Where(File.Exists)
            .OrderBy(path => path, PathComparer)
            .ToArray();

        if (files.Length == 0)
            return Array.Empty<DataAccessEvidence>();

        var references = CreateMetadataReferences(module);
        if (references.Count == 0)
            return Array.Empty<DataAccessEvidence>();

        var trees = files.Select(file =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file)).ToArray();
        var compilation = CSharpCompilation.Create(
            "Aegis.EntityFrameworkDataAccessDiscovery",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var evidence = new List<DataAccessEvidence>();
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
                    continue;

                if (TryDiscoverRead(invocation, method, semanticModel, module, out var read))
                    evidence.Add(read);
                else if (TryDiscoverWrite(invocation, method, semanticModel, module, out var write))
                    evidence.Add(write);
                else if (TryDiscoverMapping(invocation, method, semanticModel, module, out var mapping))
                    evidence.Add(mapping);
            }
        }

        return Array.AsReadOnly(evidence.ToArray());
    }

    private static bool TryDiscoverRead(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        string module,
        out DataAccessEvidence evidence)
    {
        evidence = null!;
        if (!ReadMethods.Contains(method.Name)
            || !IsEntityFrameworkMethod(method)
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        var entityType = GetQueryableEntityType(receiverType);
        if (entityType is null && method.Name is "Find" or "FindAsync")
            entityType = GetDbSetEntityType(receiverType);

        if (entityType is null)
            return false;

        evidence = CreateEvidence(
            invocation,
            method,
            module,
            DataIntegrationOperation.Read,
            GetStoreIdentity(receiverType, memberAccess.Expression, semanticModel),
            entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            memberAccess.Expression.ToString(),
            "Roslyn resolved an Entity Framework Core query execution method over a typed query or DbSet.",
            IntegrationEvidenceResolutionStatus.Resolved);
        return true;
    }

    private static bool TryDiscoverWrite(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        string module,
        out DataAccessEvidence evidence)
    {
        evidence = null!;
        if (!WriteMethods.Contains(method.Name)
            || !IsEntityFrameworkMethod(method))
            return false;

        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        var receiverType = memberAccess is null
            ? semanticModel.GetEnclosingSymbol(invocation.SpanStart)?.ContainingType
            : semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType is null)
            return false;

        // DbContext exposes Add/Update/Attach/Remove as inherited instance methods.
        // In derived contexts these are commonly invoked without an explicit "this."
        // receiver, so Roslyn binds them correctly but the syntax has no member access.
        var isImplicitDbContextMutation = memberAccess is null
            && IsDbContext(receiverType)
            && IsDbContext(method.ContainingType);
        if (memberAccess is null && !isImplicitDbContextMutation)
            return false;

        var queryEntityType = GetQueryableEntityType(receiverType);
        var isBulkQueryMutation = BulkWriteMethods.Contains(method.Name)
            && method.ContainingType.Name == "EntityFrameworkQueryableExtensions"
            && queryEntityType is not null;
        if (!isBulkQueryMutation
            && !IsDbContext(receiverType)
            && GetDbSetEntityType(receiverType) is null)
            return false;

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var argumentType = argument is null ? null : semanticModel.GetTypeInfo(argument).Type;
        var entityType = isBulkQueryMutation
            ? queryEntityType
            : argumentType ?? GetDbSetEntityType(receiverType);
        if (entityType is null)
            return false;

        evidence = CreateEvidence(
            invocation,
            method,
            module,
            DataIntegrationOperation.Write,
            memberAccess is null
                ? receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : GetStoreIdentity(receiverType, memberAccess.Expression, semanticModel),
            entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            isBulkQueryMutation
                ? memberAccess!.Expression.ToString()
                : argument?.ToString() ?? memberAccess?.Expression.ToString() ?? "this",
            "Roslyn resolved an Entity Framework Core tracked-entity mutation method.",
            IntegrationEvidenceResolutionStatus.Resolved);
        return true;
    }

    private static bool TryDiscoverMapping(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel semanticModel,
        string module,
        out DataAccessEvidence evidence)
    {
        evidence = null!;
        if (method.Name is not ("ToTable" or "ToView")
            || method.ContainingNamespace.ToDisplayString() != "Microsoft.EntityFrameworkCore"
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var builderType = semanticModel.GetTypeInfo(memberAccess.Expression).Type as INamedTypeSymbol;
        if (builderType is null || !builderType.Name.Contains("EntityTypeBuilder", StringComparison.Ordinal))
            return false;

        var entityType = builderType.TypeArguments.FirstOrDefault();
        var nameArgument = GetArgumentForParameter(invocation, method, "name");
        if (entityType is null || nameArgument is null)
            return false;

        var nameIdentity = semanticModel.GetConstantValue(nameArgument);
        var schemaArgument = GetArgumentForParameter(invocation, method, "schema");
        var schemaIdentity = schemaArgument is null
            ? default
            : semanticModel.GetConstantValue(schemaArgument);
        var resolved = nameIdentity.HasValue && nameIdentity.Value is string storeObjectName
            && !string.IsNullOrWhiteSpace(storeObjectName);
        var schema = schemaIdentity.HasValue && schemaIdentity.Value is string schemaName
            && !string.IsNullOrWhiteSpace(schemaName)
                ? schemaName
                : null;
        var targetIdentity = resolved
            ? schema is null ? (string)nameIdentity.Value! : $"{schema}.{nameIdentity.Value}"
            : null;
        var owner = semanticModel.GetEnclosingSymbol(invocation.SpanStart)?.ContainingType;
        var storeIdentity = owner?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var entityIdentity = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        evidence = CreateEvidence(
            invocation, method, module, DataIntegrationOperation.Mapping, storeIdentity,
            entityIdentity, nameArgument.ToString(),
            resolved
                ? "Roslyn resolved an Entity Framework Core relational entity mapping and its literal store-object identity."
                : "Roslyn resolved an Entity Framework Core relational entity mapping; the store-object identity is dynamic or unavailable.",
            resolved ? IntegrationEvidenceResolutionStatus.Resolved : IntegrationEvidenceResolutionStatus.Dynamic,
            sourceDataIdentity: entityIdentity,
            targetDataIdentity: targetIdentity);
        return true;
    }
    private static ExpressionSyntax? GetArgumentForParameter(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        string parameterName)
    {
        var parameterIndex = -1;
        for (var index = 0; index < method.Parameters.Length; index++)
        {
            if (method.Parameters[index].Name.Equals(parameterName, StringComparison.Ordinal))
            {
                parameterIndex = index;
                break;
            }
        }

        if (parameterIndex < 0)
            return null;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon is not null)
            {
                if (argument.NameColon.Name.Identifier.ValueText.Equals(parameterName, StringComparison.Ordinal))
                    return argument.Expression;
                continue;
            }

            var positionalIndex = invocation.ArgumentList.Arguments
                .TakeWhile(candidate => candidate != argument)
                .Count(candidate => candidate.NameColon is null);
            if (positionalIndex == parameterIndex)
                return argument.Expression;
        }

        return null;
    }

    private static DataAccessEvidence CreateEvidence(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        string module,
        DataIntegrationOperation operation,
        string? storeIdentity,
        string? entityIdentity,
        string dataObjectExpression,
        string resolutionEvidence,
        IntegrationEvidenceResolutionStatus status,
        string? sourceDataIdentity = null,
        string? targetDataIdentity = null)
    {
        var location = invocation.GetLocation().GetLineSpan();
        var sourceFile = invocation.SyntaxTree.FilePath;
        var identityInput = $"{sourceFile}|{invocation.SpanStart}|{invocation.Span.Length}|{method.ToDisplayString()}";
        var evidenceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityInput)));

        return new DataAccessEvidence
        {
            EvidenceId = evidenceId,
            Language = "C#",
            Framework = "Entity Framework Core",
            ProviderId = "csharp-roslyn-efcore-symbols",
            ModulePath = module,
            SourceFile = sourceFile,
            StartLine = location.StartLinePosition.Line + 1,
            StartColumn = location.StartLinePosition.Character + 1,
            Operation = operation,
            Technology = "Entity Framework Core",
            StoreIdentity = storeIdentity,
            DataObjectIdentity = entityIdentity,
            DataObjectExpression = dataObjectExpression,
            SourceDataIdentity = sourceDataIdentity,
            TargetDataIdentity = targetDataIdentity,
            QueryExpression = invocation.ToString(),
            ResolutionStatus = status,
            ResolutionEvidence = Array.AsReadOnly(new[] { resolutionEvidence }),
            DeclarationEvidence = invocation.ToString()
        };
    }

    private static bool IsEntityFrameworkMethod(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        var namespaceName = containingType.ContainingNamespace.ToDisplayString();
        if (namespaceName != "Microsoft.EntityFrameworkCore"
            && namespaceName != "System.Linq")
            return false;

        return IsDbContext(containingType)
            || GetDbSetEntityType(containingType) is not null
            || namespaceName == "Microsoft.EntityFrameworkCore"
                && containingType.Name.EndsWith("EntityFrameworkQueryableExtensions", StringComparison.Ordinal)
            || namespaceName == "System.Linq"
                && containingType.Name is "Queryable" or "Enumerable";
    }

    private static ITypeSymbol? GetQueryableEntityType(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>")
                return current.TypeArguments[0];

            foreach (var implemented in current.AllInterfaces)
            {
                if (implemented.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>")
                    return implemented.TypeArguments[0];
            }
        }

        return null;
    }

    private static ITypeSymbol? GetDbSetEntityType(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString() == DbSetType)
                return current.TypeArguments[0];
        }

        return null;
    }

    private static bool IsDbContext(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == DbContextType)
                return true;
        }

        return false;
    }

    private static string? GetStoreIdentity(
        ITypeSymbol? receiverType,
        ExpressionSyntax receiver,
        SemanticModel semanticModel)
    {
        if (receiverType is null)
            return null;

        if (IsDbContext(receiverType))
            return receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // A DbSet property can be an implicit-this identifier inside a derived
        // DbContext (for example, Orders.Where(...)), not a member-access node.
        // Inspect all simple names in the receiver chain and retain only symbols
        // whose declared type is an actual EF Core DbSet<T>.
        var dbSetProperty = receiver.DescendantNodesAndSelf()
            .OfType<SimpleNameSyntax>()
            .Select(name => semanticModel.GetSymbolInfo(name).Symbol as IPropertySymbol)
            .FirstOrDefault(property => property is not null
                && GetDbSetEntityType(property.Type) is not null);

        return dbSetProperty?.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ?? receiver.ToString();
    }

    private static IReadOnlyList<MetadataReference> CreateMetadataReferences(string modulePath)
    {
        var references = PlatformReferences.Value.ToList();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references.OfType<PortableExecutableReference>())
        {
            if (reference.FilePath is not string filePath)
                continue;
            try
            {
                identities.Add(System.Reflection.AssemblyName.GetAssemblyName(filePath).FullName ?? filePath);
            }
            catch (BadImageFormatException)
            {
                // Ignore non-managed platform assets.
            }
        }

        foreach (var projectFile in Directory.EnumerateFiles(modulePath, "*.csproj", SearchOption.AllDirectories))
        {
            var assetsFile = Path.Combine(Path.GetDirectoryName(projectFile)!, "obj", "project.assets.json");
            if (!File.Exists(assetsFile))
                continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(assetsFile));
                var root = document.RootElement;
                if (!root.TryGetProperty("packageFolders", out var packageFolders)
                    || !root.TryGetProperty("targets", out var targets)
                    || !root.TryGetProperty("libraries", out var libraries))
                    continue;

                foreach (var target in targets.EnumerateObject())
                foreach (var library in target.Value.EnumerateObject())
                {
                    if (!library.Value.TryGetProperty("compile", out var compileAssets)
                        || !libraries.TryGetProperty(library.Name, out var metadata)
                        || !metadata.TryGetProperty("path", out var libraryPathElement))
                        continue;

                    var libraryPath = libraryPathElement.GetString();
                    if (string.IsNullOrWhiteSpace(libraryPath))
                        continue;

                    foreach (var asset in compileAssets.EnumerateObject())
                    {
                        if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            continue;
                        foreach (var folder in packageFolders.EnumerateObject())
                        {
                            var candidate = Path.GetFullPath(Path.Combine(folder.Name, libraryPath,
                                asset.Name.Replace('/', Path.DirectorySeparatorChar)));
                            if (!File.Exists(candidate))
                                continue;
                            try
                            {
                                var identity = System.Reflection.AssemblyName.GetAssemblyName(candidate).FullName;
                                if (string.IsNullOrWhiteSpace(identity) || !identities.Add(identity))
                                    continue;
                                references.Add(MetadataReference.CreateFromFile(candidate));
                            }
                            catch (BadImageFormatException)
                            {
                                // Native assets are not Roslyn metadata references.
                            }
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or JsonException or ArgumentException or NotSupportedException)
            {
                // Missing or malformed restore assets cannot establish framework symbols.
            }
        }

        return Array.AsReadOnly(references.ToArray());
    }

    private static IReadOnlyList<MetadataReference> CreatePlatformReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
            return Array.Empty<MetadataReference>();

        return trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(PathComparer)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
