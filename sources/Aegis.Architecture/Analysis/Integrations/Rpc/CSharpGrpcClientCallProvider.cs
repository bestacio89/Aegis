using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aegis.Architecture.Analysis.Integrations.Rpc;

/// <summary>
/// Discovers calls bound to generated gRPC .NET client types deriving from
/// Grpc.Core.ClientBase&lt;T&gt;. Receiver expressions are retained, not evaluated.
/// </summary>
public sealed class CSharpGrpcClientCallProvider
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> PlatformReferences =
        new(CreatePlatformReferences);

    public IReadOnlyList<RpcIntegrationEvidence> Discover(
        IEnumerable<string> sourceFiles,
        string modulePath,
        IReadOnlyCollection<RpcIntegrationEvidence>? serviceContracts = null)
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
            return Array.Empty<RpcIntegrationEvidence>();

        var references = CreateMetadataReferences(module);
        var trees = files.Select(file =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file)).ToArray();
        var compilation = CSharpCompilation.Create(
            "Aegis.GrpcClientDiscovery",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var evidence = new List<RpcIntegrationEvidence>();
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (method is null || !IsGeneratedGrpcClient(method.ContainingType))
                    continue;

                var clientType = method.ContainingType;
                var serviceIdentity = GetServiceIdentity(clientType);
                if (string.IsNullOrWhiteSpace(serviceIdentity))
                    continue;

                var methodName = method.Name.EndsWith("Async", StringComparison.Ordinal)
                    ? method.Name[..^"Async".Length]
                    : method.Name;
                if (string.IsNullOrWhiteSpace(methodName))
                    continue;

                var serviceName = GetServiceName(clientType);
                var matchingContracts = serviceContracts?
                    .Where(contract => contract.Operation == RpcIntegrationOperation.Endpoint
                        && contract.ServiceIdentity?.Split('.').Last().Equals(serviceName, StringComparison.Ordinal) == true
                        && contract.MethodIdentity?.EndsWith("." + methodName, StringComparison.Ordinal) == true)
                    .ToArray() ?? Array.Empty<RpcIntegrationEvidence>();
                var contractMatch = matchingContracts.Length == 1 ? matchingContracts[0] : null;
                var methodIdentity = contractMatch?.MethodIdentity ?? serviceIdentity + "." + methodName;
                if (contractMatch is not null)
                    serviceIdentity = contractMatch.ServiceIdentity ?? serviceIdentity;
                var receiver = invocation.Expression is MemberAccessExpressionSyntax memberAccess
                    ? memberAccess.Expression.ToString()
                    : clientType.Name;
                var location = invocation.GetLocation().GetLineSpan();
                var sourceFile = tree.FilePath;
                var evidenceInput = $"{sourceFile}|{invocation.SpanStart}|{invocation.Span.Length}|{methodIdentity}";
                evidence.Add(new RpcIntegrationEvidence
                {
                    EvidenceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidenceInput))),
                    Language = "C#",
                    Framework = "gRPC for .NET",
                    ProviderId = "csharp-roslyn-grpc-client",
                    ModulePath = module,
                    SourceFile = sourceFile,
                    StartLine = location.StartLinePosition.Line + 1,
                    StartColumn = location.StartLinePosition.Character + 1,
                    Operation = RpcIntegrationOperation.Call,
                    Protocol = "gRPC",
                    ServiceIdentity = serviceIdentity,
                    MethodIdentity = methodIdentity,
                    TargetIdentity = methodIdentity,
                    TargetExpression = receiver,
                    HandlerIdentity = GetCallerIdentity(invocation),
                    ResolutionStatus = matchingContracts.Length > 1
                        ? IntegrationEvidenceResolutionStatus.PartiallyResolved
                        : IntegrationEvidenceResolutionStatus.Resolved,
                    ResolutionEvidence = Array.AsReadOnly(new[]
                    {
                        "Roslyn resolved the invocation to a method declared by a client type deriving from Grpc.Core.ClientBase<T>.",
                        "The generated client type supplies the service identity; the Async suffix is removed to recover the RPC contract method name.",
                        contractMatch is not null
                            ? "The generated client service and method uniquely match a Protocol Buffers service contract in the same module."
                            : matchingContracts.Length > 1
                                ? "Multiple Protocol Buffers service contracts match the generated client service and method; contract identity remains ambiguous."
                                : "No unique Protocol Buffers contract match was available; the generated client identity is retained.",
                        "The receiver expression is retained without evaluating channel configuration or network destination."
                    }),
                    DeclarationEvidence = invocation.ToString()
                });
            }
        }

        return Array.AsReadOnly(evidence.ToArray());
    }

    private static bool IsGeneratedGrpcClient(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var definition = current.OriginalDefinition;
            if (definition.MetadataName == "ClientBase`1"
                && definition.ContainingNamespace.ToDisplayString() == "Grpc.Core")
                return true;
        }

        return false;
    }

    private static string GetServiceName(INamedTypeSymbol clientType)
    {
        if (clientType.ContainingType is not null)
            return clientType.ContainingType.Name;

        const string clientSuffix = "Client";
        return clientType.Name.EndsWith(clientSuffix, StringComparison.Ordinal)
            ? clientType.Name[..^clientSuffix.Length]
            : clientType.Name;
    }

    private static string GetServiceIdentity(INamedTypeSymbol clientType)
    {
        var serviceType = clientType.ContainingType;
        var serviceName = serviceType?.Name
            ?? (clientType.Name.EndsWith("Client", StringComparison.Ordinal)
                ? clientType.Name[..^"Client".Length]
                : null);
        if (string.IsNullOrWhiteSpace(serviceName))
            return string.Empty;

        var namespaceName = clientType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : clientType.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? serviceName
            : namespaceName + "." + serviceName;
    }

    private static string? GetCallerIdentity(SyntaxNode node)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null)
            return null;
        var type = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return type is null
            ? method.Identifier.ValueText
            : type.Identifier.ValueText + "." + method.Identifier.ValueText;
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
                            var candidate = Path.GetFullPath(Path.Combine(
                                folder.Name,
                                libraryPath,
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
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException or JsonException
                or ArgumentException or NotSupportedException)
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

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(PathComparer)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
