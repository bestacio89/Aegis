using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Aegis.Architecture.Analysis.Integrations.Messaging;

/// <summary>
/// Discovers MassTransit publish calls and consumer contracts from C# syntax,
/// requiring grounded MassTransit receiver or interface types.
/// </summary>
public sealed class CSharpMassTransitMessageProvider
{
    private const string MassTransitNamespace = "MassTransit";
    private static readonly Lazy<IReadOnlyList<MetadataReference>> PlatformReferences =
        new(CreatePlatformReferences);

    public IReadOnlyList<MessageIntegrationEvidence> Discover(
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
            return Array.Empty<MessageIntegrationEvidence>();

        var metadataReferences = CreateMetadataReferences(module);
        if (metadataReferences.Count == 0)
            return Array.Empty<MessageIntegrationEvidence>();

        var trees = files.Select(file =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file)).ToArray();
        var compilation = CSharpCompilation.Create(
            "Aegis.MassTransitMessageDiscovery",
            trees,
            metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var roots = trees.Select(tree => tree.GetCompilationUnitRoot()).ToArray();
        var receiveEndpoints = DiscoverReceiveEndpoints(roots, compilation);
        var evidence = new List<MessageIntegrationEvidence>();

        foreach (var root in roots)
        {
            var semanticModel = compilation.GetSemanticModel(
                root.SyntaxTree,
                ignoreAccessibility: true);

            DiscoverConsumers(root, semanticModel, module, receiveEndpoints, evidence);
            DiscoverPublishCalls(root, semanticModel, module, evidence);
            DiscoverSendCalls(root, semanticModel, module, evidence);
        }

        return Array.AsReadOnly(evidence.ToArray());
    }

    private static IReadOnlyList<MetadataReference> CreateMetadataReferences(string modulePath)
    {
        var references = new List<MetadataReference>();
        var assemblyIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in PlatformReferences.Value)
        {
            if (reference is not PortableExecutableReference portable
                || portable.FilePath is not string filePath)
            {
                continue;
            }

            references.Add(reference);
            try
            {
                assemblyIdentities.Add(
                    System.Reflection.AssemblyName.GetAssemblyName(filePath).FullName ?? filePath);
            }
            catch (BadImageFormatException)
            {
                // Platform references are expected to be managed assemblies.
            }
        }

        foreach (var projectFile in Directory.EnumerateFiles(
                     modulePath,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            var assetsFile = Path.Combine(
                Path.GetDirectoryName(projectFile)!,
                "obj",
                "project.assets.json");
            if (!File.Exists(assetsFile))
                continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(assetsFile));
                var root = document.RootElement;
                if (!root.TryGetProperty("packageFolders", out var packageFolders)
                    || !root.TryGetProperty("targets", out var targets)
                    || !root.TryGetProperty("libraries", out var libraries))
                {
                    continue;
                }

                foreach (var target in targets.EnumerateObject())
                {
                    foreach (var library in target.Value.EnumerateObject())
                    {
                        if (!library.Value.TryGetProperty("compile", out var compileAssets)
                            || !libraries.TryGetProperty(library.Name, out var libraryMetadata)
                            || !libraryMetadata.TryGetProperty("path", out var libraryPathElement))
                        {
                            continue;
                        }

                        var libraryPath = libraryPathElement.GetString();
                        if (string.IsNullOrWhiteSpace(libraryPath))
                            continue;

                        foreach (var compileAsset in compileAssets.EnumerateObject())
                        {
                            if (!compileAsset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                continue;

                            foreach (var packageFolder in packageFolders.EnumerateObject())
                            {
                                var candidate = Path.GetFullPath(Path.Combine(
                                    packageFolder.Name,
                                    libraryPath,
                                    compileAsset.Name.Replace('/', Path.DirectorySeparatorChar)));
                                if (!File.Exists(candidate))
                                    continue;

                                try
                                {
                                    var identity = System.Reflection.AssemblyName
                                        .GetAssemblyName(candidate)
                                        .FullName;
                                    if (string.IsNullOrWhiteSpace(identity)
                                        || !assemblyIdentities.Add(identity))
                                    {
                                        continue;
                                    }

                                    references.Add(MetadataReference.CreateFromFile(candidate));
                                }
                                catch (BadImageFormatException)
                                {
                                    // Native and non-managed assets are not Roslyn metadata references.
                                }
                            }
                        }
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
                // An unavailable assets file cannot establish framework symbols.
                // The provider remains fail-closed instead of falling back to syntax names.
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

    private static void DiscoverConsumers(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        string modulePath,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedMessageDestination>> receiveEndpoints,
        ICollection<MessageIntegrationEvidence> evidence)
    {
        foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (declaration.BaseList is null)
                continue;

            foreach (var baseType in declaration.BaseList.Types)
            {
                var generic = baseType.Type.DescendantNodesAndSelf()
                    .OfType<GenericNameSyntax>()
                    .FirstOrDefault();
                if (generic is null
                    || !generic.Identifier.ValueText.Equals("IConsumer", StringComparison.Ordinal)
                    || !IsMassTransitType(baseType.Type, semanticModel))
                {
                    continue;
                }

                var messageType = generic.TypeArgumentList.Arguments.FirstOrDefault();
                if (messageType is null)
                    continue;

                var resolved = ResolveMessageType(messageType, null, semanticModel);
                var consumeMethod = declaration.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(method => method.Identifier.ValueText == "Consume");
                var sourceNode = (SyntaxNode?)consumeMethod ?? baseType;
                var handler = GetHandlerIdentity(declaration, consumeMethod, semanticModel);
                var location = sourceNode.GetLocation().GetLineSpan().StartLinePosition;
                var declarationEvidence = consumeMethod?.ToString() ?? baseType.ToString();
                var consumerIdentity = semanticModel.GetDeclaredSymbol(declaration)?
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var destinations = consumerIdentity is not null
                    && receiveEndpoints.TryGetValue(consumerIdentity, out var configuredDestinations)
                        ? configuredDestinations
                        : Array.Empty<ResolvedMessageDestination>();

                if (destinations.Count == 0)
                {
                    evidence.Add(CreateEvidence(
                        modulePath,
                        sourceNode,
                        "consume",
                        handler,
                        MessageIntegrationOperation.Consume,
                        resolved,
                        location,
                        declarationEvidence,
                        [
                            $"The declaration implements the MassTransit IConsumer<{resolved.Expression ?? "unknown"}> contract.",
                            resolved.Evidence
                        ]));
                    continue;
                }

                foreach (var destination in destinations)
                {
                    evidence.Add(CreateEvidence(
                        modulePath,
                        sourceNode,
                        $"consume:{destination.EvidenceKey}",
                        handler,
                        MessageIntegrationOperation.Consume,
                        resolved,
                        location,
                        $"{declarationEvidence}{Environment.NewLine}{destination.DeclarationEvidence}",
                        [
                            $"The declaration implements the MassTransit IConsumer<{resolved.Expression ?? "unknown"}> contract.",
                            resolved.Evidence,
                            destination.ResolutionEvidence
                        ],
                        destination));
                }
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ResolvedMessageDestination>> DiscoverReceiveEndpoints(
        IEnumerable<CompilationUnitSyntax> roots,
        Compilation compilation)
    {
        var destinations = new Dictionary<string, List<ResolvedMessageDestination>>(StringComparer.Ordinal);

        foreach (var receiveEndpoint in roots.SelectMany(root =>
                     root.DescendantNodes().OfType<InvocationExpressionSyntax>()))
        {
            var semanticModel = compilation.GetSemanticModel(
                receiveEndpoint.SyntaxTree,
                ignoreAccessibility: true);

            if (receiveEndpoint.Expression is not MemberAccessExpressionSyntax memberAccess
                || memberAccess.Name.Identifier.ValueText != "ReceiveEndpoint"
                || !IsMassTransitMethod(receiveEndpoint, semanticModel)
                || receiveEndpoint.ArgumentList.Arguments.FirstOrDefault()?.Expression is not ExpressionSyntax destinationExpression)
            {
                continue;
            }

            var destinationIdentity = destinationExpression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression)
                    ? literal.Token.ValueText
                    : null;
            var destinationEvidence = destinationExpression.ToString();
            var destinationKey = $"{receiveEndpoint.SyntaxTree.FilePath}|{receiveEndpoint.SpanStart}:{receiveEndpoint.Span.Length}";

            foreach (var configureConsumer in receiveEndpoint.DescendantNodes()
                         .OfType<InvocationExpressionSyntax>())
            {
                if (configureConsumer.Expression is not MemberAccessExpressionSyntax configureAccess
                    || configureAccess.Name is not GenericNameSyntax genericName
                    || !IsMassTransitMethod(configureConsumer, semanticModel)
                    || genericName.Identifier.ValueText != "ConfigureConsumer")
                {
                    continue;
                }

                var consumerTypeSyntax = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                if (consumerTypeSyntax is null)
                    continue;

                var consumerType = semanticModel.GetTypeInfo(consumerTypeSyntax).Type;
                var consumerIdentity = consumerType is not null && consumerType.TypeKind != TypeKind.Error
                    ? consumerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : consumerTypeSyntax.ToString();

                if (!destinations.TryGetValue(consumerIdentity, out var consumerDestinations))
                {
                    consumerDestinations = [];
                    destinations.Add(consumerIdentity, consumerDestinations);
                }

                consumerDestinations.Add(new ResolvedMessageDestination(
                    destinationIdentity,
                    destinationEvidence,
                    MessageDestinationKind.Unknown,
                    destinationKey,
                    destinationIdentity is not null
                        ? "MassTransit ReceiveEndpoint declares an explicit endpoint identity; the underlying broker entity kind is not inferred."
                        : "The MassTransit ReceiveEndpoint destination is dynamic; its original expression is retained without evaluation.",
                    receiveEndpoint.ToString()));
            }
        }

        return destinations.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ResolvedMessageDestination>)Array.AsReadOnly(pair.Value.ToArray()),
            StringComparer.Ordinal);
    }

    private static void DiscoverPublishCalls(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        string modulePath,
        ICollection<MessageIntegrationEvidence> evidence)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var methodName = memberAccess.Name switch
            {
                GenericNameSyntax generic => generic.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => string.Empty
            };
            if (!methodName.Equals("Publish", StringComparison.Ordinal)
                || !IsMassTransitPublisher(memberAccess.Expression, semanticModel))
            {
                continue;
            }

            var genericName = memberAccess.Name as GenericNameSyntax;
            var messageTypeSyntax = genericName?.TypeArgumentList.Arguments.FirstOrDefault();
            var messageExpression = messageTypeSyntax is null
                ? invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression
                : null;
            var resolved = ResolveMessageType(messageTypeSyntax, messageExpression, semanticModel);
            var handler = GetCallerIdentity(invocation, semanticModel);
            var location = invocation.GetLocation().GetLineSpan().StartLinePosition;
            var receiverType = GetReceiverType(memberAccess.Expression, semanticModel);

            evidence.Add(CreateEvidence(
                modulePath,
                invocation,
                "publish",
                handler,
                MessageIntegrationOperation.Publish,
                resolved,
                location,
                invocation.ToString(),
                [
                    $"The Publish call receiver is grounded as MassTransit {receiverType?.Name ?? "publisher"}.",
                    resolved.Evidence
                ]));
        }
    }

    private static void DiscoverSendCalls(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        string modulePath,
        ICollection<MessageIntegrationEvidence> evidence)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var methodName = memberAccess.Name switch
            {
                GenericNameSyntax generic => generic.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => string.Empty
            };
            if (!methodName.Equals("Send", StringComparison.Ordinal)
                || !IsMassTransitSender(memberAccess.Expression, semanticModel))
            {
                continue;
            }

            var genericName = memberAccess.Name as GenericNameSyntax;
            var messageTypeSyntax = genericName?.TypeArgumentList.Arguments.FirstOrDefault();
            var messageExpression = messageTypeSyntax is null
                ? invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression
                : null;
            var resolved = ResolveMessageType(messageTypeSyntax, messageExpression, semanticModel);
            var handler = GetCallerIdentity(invocation, semanticModel);
            var location = invocation.GetLocation().GetLineSpan().StartLinePosition;

            evidence.Add(CreateEvidence(
                modulePath,
                invocation,
                "send",
                handler,
                MessageIntegrationOperation.Send,
                resolved,
                location,
                invocation.ToString(),
                [
                    "The Send call receiver is grounded as MassTransit ISendEndpoint.",
                    resolved.Evidence
                ]));
        }
    }

    private static bool IsMassTransitSender(
        ExpressionSyntax receiver,
        SemanticModel semanticModel)
    {
        var type = GetReceiverType(receiver, semanticModel);
        return type is not null
            && type.TypeKind != TypeKind.Error
            && type.Name == "ISendEndpoint"
            && type.ContainingNamespace.ToDisplayString().Equals(
                MassTransitNamespace,
                StringComparison.Ordinal);
    }
    private static bool IsMassTransitType(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetTypeInfo(typeSyntax).Type;
        return symbol is not null
            && symbol.TypeKind != TypeKind.Error
            && symbol.ContainingNamespace.ToDisplayString().Equals(
                MassTransitNamespace,
                StringComparison.Ordinal);
    }

    private static bool IsMassTransitPublisher(
        ExpressionSyntax receiver,
        SemanticModel semanticModel)
    {
        var type = GetReceiverType(receiver, semanticModel);
        return type is not null
            && type.TypeKind != TypeKind.Error
            && (type.Name is "IPublishEndpoint" or "ConsumeContext")
            && type.ContainingNamespace.ToDisplayString().Equals(
                MassTransitNamespace,
                StringComparison.Ordinal);
    }

    private static bool IsMassTransitMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return method is not null
            && method.ContainingNamespace.ToDisplayString().Equals(
                MassTransitNamespace,
                StringComparison.Ordinal);
    }

    private static ITypeSymbol? GetReceiverType(
        ExpressionSyntax receiver,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(receiver).Symbol;
        var declaredType = symbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            IParameterSymbol parameter => parameter.Type,
            ILocalSymbol local => local.Type,
            _ => null
        };

        return declaredType ?? semanticModel.GetTypeInfo(receiver).Type;
    }

    private static ResolvedMessageType ResolveMessageType(
        TypeSyntax? typeSyntax,
        ExpressionSyntax? expression,
        SemanticModel semanticModel)
    {
        var type = typeSyntax is not null
            ? semanticModel.GetTypeInfo(typeSyntax).Type
            : expression is not null
                ? semanticModel.GetTypeInfo(expression).Type
                : null;
        var sourceExpression = typeSyntax?.ToString()
            ?? (type is not null && type.TypeKind != TypeKind.Error
                ? type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                : expression?.ToString());

        if (type is null || type.TypeKind == TypeKind.Error)
        {
            return new ResolvedMessageType(
                null,
                sourceExpression,
                sourceExpression is null
                    ? IntegrationEvidenceResolutionStatus.Unresolved
                    : IntegrationEvidenceResolutionStatus.PartiallyResolved,
                "The message contract expression is retained, but Roslyn could not resolve a concrete message type.");
        }

        if (type.TypeKind == TypeKind.TypeParameter
            || type is INamedTypeSymbol { IsAnonymousType: true })
        {
            return new ResolvedMessageType(
                null,
                sourceExpression,
                IntegrationEvidenceResolutionStatus.PartiallyResolved,
                "The message expression resolves only to a type parameter or anonymous type.");
        }

        return new ResolvedMessageType(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourceExpression,
            IntegrationEvidenceResolutionStatus.Resolved,
            "Roslyn resolved the message contract to a concrete source or platform type.");
    }

    private static string GetHandlerIdentity(
        TypeDeclarationSyntax declaration,
        MethodDeclarationSyntax? method,
        SemanticModel semanticModel)
    {
        var type = semanticModel.GetDeclaredSymbol(declaration);
        var typeName = type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ?? declaration.Identifier.ValueText;
        return method is null
            ? typeName
            : typeName + "." + method.Identifier.ValueText;
    }

    private static string GetCallerIdentity(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var method = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var type = method?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (method is null)
            return type?.Identifier.ValueText ?? "<top-level>";

        var typeName = type is null
            ? null
            : semanticModel.GetDeclaredSymbol(type)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return typeName is null
            ? method.Identifier.ValueText
            : typeName + "." + method.Identifier.ValueText;
    }

    private static MessageIntegrationEvidence CreateEvidence(
        string modulePath,
        SyntaxNode declaration,
        string operationKey,
        string handlerIdentity,
        MessageIntegrationOperation operation,
        ResolvedMessageType messageType,
        LinePosition linePosition,
        string declarationEvidence,
        IReadOnlyList<string> resolutionEvidence,
        ResolvedMessageDestination? destination = null)
    {
        var sourceFile = declaration.SyntaxTree.FilePath;
        var identity = $"{sourceFile}|{declaration.SpanStart}|{declaration.Span.Length}|{operationKey}";
        var evidenceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

        return new MessageIntegrationEvidence
        {
            EvidenceId = evidenceId,
            Language = "C#",
            Framework = "MassTransit",
            ProviderId = "roslyn-masstransit",
            ModulePath = modulePath,
            SourceFile = sourceFile,
            StartLine = linePosition.Line + 1,
            StartColumn = linePosition.Character + 1,
            Operation = operation,
            MessageTypeIdentity = messageType.Identity,
            MessageTypeExpression = messageType.Expression,
            DestinationKind = destination?.Kind ?? MessageDestinationKind.Unknown,
            DestinationIdentity = destination?.Identity,
            DestinationExpression = destination?.Expression,
            HandlerIdentity = handlerIdentity,
            ResolutionStatus = messageType.Status,
            ResolutionEvidence = Array.AsReadOnly(resolutionEvidence.ToArray()),
            DeclarationEvidence = declarationEvidence
        };
    }

    private sealed record ResolvedMessageType(
        string? Identity,
        string? Expression,
        IntegrationEvidenceResolutionStatus Status,
        string Evidence);

    private sealed record ResolvedMessageDestination(
        string? Identity,
        string? Expression,
        MessageDestinationKind Kind,
        string EvidenceKey,
        string ResolutionEvidence,
        string DeclarationEvidence);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
