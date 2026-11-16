using System.Security.Cryptography;
using System.Text;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aegis.Architecture.Analysis.Api;

/// <summary>Discovers ASP.NET Core controller actions and Minimal API mappings from C# syntax trees.</summary>
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
            if (declaration.Initializer?.Value is not InvocationExpressionSyntax invocation
                || !IsMapGroup(invocation))
                continue;

            var receiver = GetReceiver(invocation);
            var parent = ResolveGroupPrefix(receiver, groupPrefixes, new HashSet<string>(StringComparer.Ordinal));
            var route = ReadFirstRouteArgument(invocation);
            groupPrefixes[declaration.Identifier.ValueText] = new RouteValue(
                CombineRoutes(parent.Value, route.Value),
                ResolveStatus(parent.Status, route.Status),
                parent.Evidence.Concat(route.Evidence).ToArray());
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!TryGetMinimalApiMethods(invocation, out var methods, out var methodsResolved)) continue;

            var route = ReadFirstRouteArgument(invocation);
            var prefix = ResolveGroupPrefix(GetReceiver(invocation), groupPrefixes, new HashSet<string>(StringComparer.Ordinal));
            var handler = invocation.ArgumentList.Arguments.Skip(invocation.Expression.ToString().EndsWith("MapMethods", StringComparison.Ordinal) ? 2 : 1)
                .FirstOrDefault()?.Expression.ToString() ?? "unresolved-handler";
            var status = ResolveStatus(prefix.Status, route.Status);
            var composition = prefix.Evidence.Concat(route.Evidence).ToList();
            if (!methodsResolved)
            {
                composition.Add("HTTP methods are not statically resolvable");
                status = ResolveStatus(status, ApiEndpointResolutionStatus.PartiallyResolved);
            }

            results.Add(CreateEvidence(sourceFile, modulePath, invocation, handler, methods,
                CombineRoutes(prefix.Value, route.Value), status, composition, invocation.ToString()));
        }
    }

    private static RouteValue ResolveGroupPrefix(
        ExpressionSyntax? receiver,
        IReadOnlyDictionary<string, RouteValue> groupPrefixes,
        ISet<string> visited)
    {
        if (receiver is null) return RouteValue.Empty;
        if (receiver is IdentifierNameSyntax identifier)
        {
            if (!visited.Add(identifier.Identifier.ValueText)) return RouteValue.Dynamic;
            return groupPrefixes.TryGetValue(identifier.Identifier.ValueText, out var group)
                ? group : RouteValue.Empty;
        }

        if (receiver is InvocationExpressionSyntax invocation && IsMapGroup(invocation))
        {
            var parent = ResolveGroupPrefix(GetReceiver(invocation), groupPrefixes, visited);
            var route = ReadFirstRouteArgument(invocation);
            return new RouteValue(
                CombineRoutes(parent.Value, route.Value),
                ResolveStatus(parent.Status, route.Status),
                parent.Evidence.Concat(route.Evidence).ToArray());
        }

        return RouteValue.Empty;
    }

    private static bool IsMapGroup(InvocationExpressionSyntax invocation) =>
        GetMethodName(invocation).Equals("MapGroup", StringComparison.Ordinal);

    private static ExpressionSyntax? GetReceiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax member ? member.Expression : null;

    private static string GetMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => string.Empty
        };

    private static bool TryGetMinimalApiMethods(
        InvocationExpressionSyntax invocation, out IReadOnlyList<string> methods, out bool resolved)
    {
        var name = GetMethodName(invocation);
        if (name.Equals("MapMethods", StringComparison.Ordinal))
        {
            var argument = invocation.ArgumentList.Arguments.Skip(1).FirstOrDefault()?.Expression;
            methods = ReadMethodCollection(argument, out resolved);
            return true;
        }

        if (name.StartsWith("Map", StringComparison.Ordinal)
            && HttpMethods.TryGetValue("Http" + name[3..], out var method))
        {
            methods = [method];
            resolved = true;
            return true;
        }

        methods = Array.Empty<string>();
        resolved = false;
        return false;
    }

    private static IReadOnlyList<string> ReadMethodCollection(ExpressionSyntax? expression, out bool resolved)
    {
        IEnumerable<ExpressionSyntax>? expressions = expression switch
        {
            CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>()
                .Select(element => element.Expression),
            ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions,
            ArrayCreationExpressionSyntax array when array.Initializer is not null => array.Initializer.Expressions,
            _ => null
        };

        if (expressions is null)
        {
            resolved = false;
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in expressions)
        {
            if (item is not LiteralExpressionSyntax literal
                || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                resolved = false;
                return Array.Empty<string>();
            }

            var value = literal.Token.ValueText.Trim();
            if (value.Length == 0)
            {
                resolved = false;
                return Array.Empty<string>();
            }
            values.Add(value.ToUpperInvariant());
        }

        resolved = values.Count > 0;
        return Array.AsReadOnly(values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record RouteValue(string? Value, ApiEndpointResolutionStatus Status, IReadOnlyList<string> Evidence)
    {
        public static RouteValue Empty { get; } = new(null, ApiEndpointResolutionStatus.Resolved, Array.Empty<string>());
        public static RouteValue Dynamic { get; } = new(null, ApiEndpointResolutionStatus.Dynamic, ["missing route argument"]);
    }
}
