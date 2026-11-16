using System.Security.Cryptography;
using System.Text;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

        if (files.Length == 0 || PlatformReferences.Value.Count == 0)
            return Array.Empty<MessageIntegrationEvidence>();

        var trees = files.Select(file =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file)).ToArray();
        var compilation = CSharpCompilation.Create(
            "Aegis.MassTransitMessageDiscovery",
            trees,
            PlatformReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var evidence = new List<MessageIntegrationEvidence>();
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var hasMassTransitImport = HasMassTransitImport(root);

            DiscoverConsumers(root, semanticModel, module, hasMassTransitImport, evidence);
            DiscoverPublishCalls(root, semanticModel, module, hasMassTransitImport, evidence);
        }

        return Array.AsReadOnly(evidence.ToArray());
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

    private static bool HasMassTransitImport(CompilationUnitSyntax root) =>
        root.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(usingDirective =>
            usingDirective.Name?.ToString() is string name
            && (name.Equals(MassTransitNamespace, StringComparison.Ordinal)
                || name.StartsWith(MassTransitNamespace + ".", StringComparison.Ordinal)));

    private static void DiscoverConsumers(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        string modulePath,
        bool hasMassTransitImport,
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
                    || !IsMassTransitType(baseType.Type, semanticModel, hasMassTransitImport))
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
                        "The declaration implements the MassTransit IConsumer<T> contract.",
                        resolved.Evidence
                    ]));
            }
        }
    }

    private static void DiscoverPublishCalls(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        string modulePath,
        bool hasMassTransitImport,
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
                || !IsMassTransitPublisher(memberAccess.Expression, semanticModel, hasMassTransitImport))
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

    private static bool IsMassTransitType(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        bool hasMassTransitImport)
    {
        var symbol = semanticModel.GetTypeInfo(typeSyntax).Type;
        if (symbol is not null && symbol.TypeKind != TypeKind.Error)
            return symbol.ContainingNamespace.ToDisplayString().Equals(
                MassTransitNamespace,
                StringComparison.Ordinal);

        var typeName = typeSyntax.ToString();
        return hasMassTransitImport
            || typeName.StartsWith(MassTransitNamespace + ".", StringComparison.Ordinal)
            || typeName.StartsWith("global::" + MassTransitNamespace + ".", StringComparison.Ordinal);
    }

    private static bool IsMassTransitPublisher(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        bool hasMassTransitImport)
    {
        var type = GetReceiverType(receiver, semanticModel);
        if (type is null || type.Name is not ("IPublishEndpoint" or "ConsumeContext"))
            return false;

        if (type.TypeKind != TypeKind.Error)
        {
            return type.ContainingNamespace.ToDisplayString().Equals(
                MassTransitNamespace,
                StringComparison.Ordinal);
        }

        return hasMassTransitImport
            || receiver.ToString().StartsWith(MassTransitNamespace + ".", StringComparison.Ordinal)
            || receiver.ToString().StartsWith("global::" + MassTransitNamespace + ".", StringComparison.Ordinal);
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
        var sourceExpression = typeSyntax?.ToString() ?? expression?.ToString();
        var type = typeSyntax is not null
            ? semanticModel.GetTypeInfo(typeSyntax).Type
            : expression is not null
                ? semanticModel.GetTypeInfo(expression).Type
                : null;

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

        if (type.TypeKind == TypeKind.TypeParameter || type.TypeKind == TypeKind.Anonymous)
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
        IReadOnlyList<string> resolutionEvidence)
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
            DestinationKind = MessageDestinationKind.Unknown,
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

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
