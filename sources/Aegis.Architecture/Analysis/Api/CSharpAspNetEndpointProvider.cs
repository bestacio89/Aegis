using System.Security.Cryptography;
using System.Text;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aegis.Architecture.Analysis.Api;

/// <summary>Discovers ASP.NET Core controller actions and literal Minimal API mappings from C# syntax trees.</summary>
public sealed class CSharpAspNetEndpointProvider
{
    private static readonly IReadOnlyDictionary<string, string> HttpMethods =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HttpGet"] = "GET", ["HttpPost"] = "POST", ["HttpPut"] = "PUT",
            ["HttpDelete"] = "DELETE", ["HttpPatch"] = "PATCH",
            ["HttpHead"] = "HEAD", ["HttpOptions"] = "OPTIONS"
        };

    public IReadOnlyList<ApiEndpointEvidence> Discover(IEnumerable<string> sourceFiles, string modulePath)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        var module = Path.GetFullPath(modulePath);
        var results = new List<ApiEndpointEvidence>();

        foreach (var sourceFile in sourceFiles.Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath).Distinct(PathComparer).OrderBy(path => path, PathComparer))
        {
            if (!File.Exists(sourceFile)) continue;
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), path: sourceFile).GetCompilationUnitRoot();
            DiscoverControllerActions(root, sourceFile, module, results);
            DiscoverMinimalApiMappings(root, sourceFile, module, results);
        }

        return Array.AsReadOnly(results.ToArray());
    }

    private static void DiscoverControllerActions(
        CompilationUnitSyntax root, string sourceFile, string modulePath, ICollection<ApiEndpointEvidence> results)
    {
        foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var prefix = FindRouteAttribute(type.AttributeLists);
            foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
            {
                var attributes = method.AttributeLists.SelectMany(list => list.Attributes).ToArray();
                var verbAttributes = attributes
                    .Where(attribute => TryGetHttpMethod(NormalizeAttributeName(attribute.Name.ToString()), out _))
                    .ToArray();
                var selected = verbAttributes.Length > 0
                    ? verbAttributes
                    : attributes.Where(attribute => NormalizeAttributeName(attribute.Name.ToString())
                        .Equals("Route", StringComparison.OrdinalIgnoreCase)).ToArray();

                foreach (var attribute in selected)
                {
                    var name = NormalizeAttributeName(attribute.Name.ToString());
                    var hasMethod = TryGetHttpMethod(name, out var httpMethod);
                    var route = ReadRouteArgument(attribute);
                    var combined = CombineRoutes(prefix.Value, route.Value);
                    var status = ResolveStatus(prefix.Status, route.Status);
                    results.Add(CreateEvidence(sourceFile, modulePath, method,
                        type.Identifier.ValueText + "." + method.Identifier.ValueText,
                        hasMethod ? [httpMethod] : Array.Empty<string>(), combined, status,
                        prefix.Evidence.Concat(route.Evidence), method.ToString()));
                }
            }
        }
    }

    private static void DiscoverMinimalApiMappings(
        CompilationUnitSyntax root, string sourceFile, string modulePath, ICollection<ApiEndpointEvidence> results)
    {
        var groupPrefixes = new Dictionary<string, RouteValue>(StringComparer.Ordinal);
        foreach (var declaration in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declaration.Initializer?.Value is InvocationExpressionSyntax invocation
                && invocation.Expression.ToString().EndsWith("MapGroup", StringComparison.Ordinal))
                groupPrefixes[declaration.Identifier.ValueText] = ReadFirstRouteArgument(invocation);
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => string.Empty
            };
            if (!TryGetMinimalApiMethod(name, out var httpMethod)) continue;

            var route = ReadFirstRouteArgument(invocation);
            var receiver = invocation.Expression is MemberAccessExpressionSyntax access
                ? access.Expression.ToString() : string.Empty;
            var prefix = groupPrefixes.TryGetValue(receiver, out var group) ? group : RouteValue.Empty;
            var handler = invocation.ArgumentList.Arguments.Skip(1).FirstOrDefault()?.Expression.ToString()
                ?? "unresolved-handler";

            results.Add(CreateEvidence(sourceFile, modulePath, invocation, handler, [httpMethod],
                CombineRoutes(prefix.Value, route.Value), ResolveStatus(prefix.Status, route.Status),
                prefix.Evidence.Concat(route.Evidence), invocation.ToString()));
        }
    }

    private static ApiEndpointEvidence CreateEvidence(
        string sourceFile, string modulePath, SyntaxNode declaration, string handler,
        IReadOnlyList<string> methods, string? route, ApiEndpointResolutionStatus status,
        IEnumerable<string> composition, string declarationEvidence)
    {
        var position = declaration.GetLocation().GetLineSpan().StartLinePosition;
        var identity = sourceFile + "|" + declaration.SpanStart + "|" + declaration.Span.Length + "|" + handler;
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new ApiEndpointEvidence
        {
            EvidenceId = id, Language = "C#", Framework = "ASP.NET Core",
            ProviderId = "roslyn-aspnet-endpoints", ModulePath = modulePath,
            SourceFile = sourceFile, StartLine = position.Line + 1, StartColumn = position.Character + 1,
            HandlerIdentity = handler, HttpMethods = Array.AsReadOnly(methods.ToArray()),
            RouteTemplate = route, ResolutionStatus = status,
            RouteCompositionEvidence = Array.AsReadOnly(composition.ToArray()),
            DeclarationEvidence = declarationEvidence
        };
    }

    private static RouteValue FindRouteAttribute(SyntaxList<AttributeListSyntax> lists)
    {
        foreach (var attribute in lists.SelectMany(list => list.Attributes))
            if (NormalizeAttributeName(attribute.Name.ToString()).Equals("Route", StringComparison.OrdinalIgnoreCase))
                return ReadRouteArgument(attribute);
        return RouteValue.Empty;
    }

    private static RouteValue ReadRouteArgument(AttributeSyntax attribute)
    {
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault(item => item.NameEquals is null && item.NameColon is null);
        return argument is null ? RouteValue.Empty : ReadString(argument.Expression);
    }

    private static RouteValue ReadFirstRouteArgument(InvocationExpressionSyntax invocation)
    {
        var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
        return argument is null ? RouteValue.Dynamic : ReadString(argument.Expression);
    }

    private static RouteValue ReadString(ExpressionSyntax expression)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var value = literal.Token.ValueText;
            var unresolvedToken = value.Contains("[controller]", StringComparison.OrdinalIgnoreCase)
                || value.Contains("[action]", StringComparison.OrdinalIgnoreCase);
            return new RouteValue(value,
                unresolvedToken ? ApiEndpointResolutionStatus.PartiallyResolved : ApiEndpointResolutionStatus.Resolved,
                ["literal route"]);
        }
        if (expression is InterpolatedStringExpressionSyntax)
            return new RouteValue(expression.ToString(), ApiEndpointResolutionStatus.PartiallyResolved, ["interpolated route expression"]);
        return new RouteValue(expression.ToString(), ApiEndpointResolutionStatus.Dynamic, ["non-constant route expression"]);
    }

    private static string? CombineRoutes(string? prefix, string? route)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return route;
        if (string.IsNullOrWhiteSpace(route)) return prefix;
        return prefix.TrimEnd('/') + "/" + route.TrimStart('/');
    }

    private static ApiEndpointResolutionStatus ResolveStatus(ApiEndpointResolutionStatus prefix, ApiEndpointResolutionStatus route)
    {
        if (prefix == ApiEndpointResolutionStatus.Dynamic || route == ApiEndpointResolutionStatus.Dynamic) return ApiEndpointResolutionStatus.Dynamic;
        if (prefix == ApiEndpointResolutionStatus.Unsupported || route == ApiEndpointResolutionStatus.Unsupported) return ApiEndpointResolutionStatus.Unsupported;
        if (prefix == ApiEndpointResolutionStatus.PartiallyResolved || route == ApiEndpointResolutionStatus.PartiallyResolved) return ApiEndpointResolutionStatus.PartiallyResolved;
        return ApiEndpointResolutionStatus.Resolved;
    }

    private static string NormalizeAttributeName(string name)
    {
        var simple = name.Split('.').Last();
        return simple.EndsWith("Attribute", StringComparison.Ordinal) ? simple[..^9] : simple;
    }

    private static bool TryGetHttpMethod(string name, out string method) => HttpMethods.TryGetValue(name, out method!);

    private static bool TryGetMinimalApiMethod(string name, out string method)
    {
        var normalized = name.StartsWith("Map", StringComparison.Ordinal) ? name[3..] : string.Empty;
        return HttpMethods.TryGetValue("Http" + normalized, out method!);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record RouteValue(string? Value, ApiEndpointResolutionStatus Status, IReadOnlyList<string> Evidence)
    {
        public static RouteValue Empty { get; } = new(null, ApiEndpointResolutionStatus.Resolved, Array.Empty<string>());
        public static RouteValue Dynamic { get; } = new(null, ApiEndpointResolutionStatus.Dynamic, ["missing route argument"]);
    }
}
