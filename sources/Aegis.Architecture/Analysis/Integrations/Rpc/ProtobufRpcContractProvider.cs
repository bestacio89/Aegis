using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Integrations.Rpc;

/// <summary>
/// Discovers service and RPC method declarations from Protocol Buffers contracts.
/// It records declarations only; it does not infer client usage or runtime endpoints.
/// </summary>
public sealed class ProtobufRpcContractProvider
{
    private static readonly Regex TokenPattern = new(
        @"(?<lineComment>//[^\r\n]*)|(?<blockComment>/\*[\s\S]*?\*/)|(?<whitespace>\s+)|(?<identifier>[A-Za-z_][A-Za-z0-9_.]*)|(?<string>""(?:\\.|[^""\\])*"")|(?<symbol>[^\s])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<RpcIntegrationEvidence> Discover(
        IEnumerable<string> sourceFiles,
        string modulePath)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);

        var module = Path.GetFullPath(modulePath);
        var evidence = new List<RpcIntegrationEvidence>();

        foreach (var file in sourceFiles
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(PathComparer)
                     .Where(File.Exists)
                     .OrderBy(path => path, PathComparer))
        {
            if (!string.Equals(Path.GetExtension(file), ".proto", StringComparison.OrdinalIgnoreCase))
                continue;

            var source = File.ReadAllText(file);
            evidence.AddRange(ParseFile(source, file, module));
        }

        return Array.AsReadOnly(evidence.ToArray());
    }

    private static IReadOnlyList<RpcIntegrationEvidence> ParseFile(
        string source,
        string sourceFile,
        string modulePath)
    {
        var tokens = TokenPattern.Matches(source)
            .Where(match => !match.Groups["lineComment"].Success
                && !match.Groups["blockComment"].Success
                && !match.Groups["whitespace"].Success)
            .Select(match => new Token(match.Value, match.Index))
            .ToArray();

        var packageName = ReadPackage(tokens);
        var evidence = new List<RpcIntegrationEvidence>();

        for (var index = 0; index < tokens.Length; index++)
        {
            if (tokens[index].Value != "service")
                continue;

            if (index + 2 >= tokens.Length
                || !IsIdentifier(tokens[index + 1].Value)
                || tokens[index + 2].Value != "{")
                continue;

            var serviceName = tokens[index + 1].Value;
            var serviceIdentity = string.IsNullOrEmpty(packageName)
                ? serviceName
                : packageName + "." + serviceName;
            var serviceEnd = FindMatchingBrace(tokens, index + 2);
            if (serviceEnd < 0)
                continue;

            for (var methodIndex = index + 3; methodIndex < serviceEnd; methodIndex++)
            {
                if (tokens[methodIndex].Value != "rpc")
                    continue;

                if (methodIndex + 1 >= serviceEnd
                    || !IsIdentifier(tokens[methodIndex + 1].Value))
                    continue;

                var methodName = tokens[methodIndex + 1].Value;
                var signatureIndex = methodIndex + 2;
                var signature = ReadRpcSignature(tokens, signatureIndex, serviceEnd);
                var location = GetLineAndColumn(source, tokens[methodIndex].Offset);
                var methodIdentity = serviceIdentity + "." + methodName;
                var resolution = signature.IsComplete
                    ? IntegrationEvidenceResolutionStatus.Resolved
                    : IntegrationEvidenceResolutionStatus.PartiallyResolved;
                var resolutionEvidence = signature.IsComplete
                    ? new[] { "The Protocol Buffers parser identified a service declaration and a complete RPC method signature." }
                    : new[] { "The RPC method declaration is present, but its request/response signature is incomplete or unsupported." };

                evidence.Add(new RpcIntegrationEvidence
                {
                    EvidenceId = CreateEvidenceId(sourceFile, tokens[methodIndex].Offset, methodIdentity),
                    Language = "Protocol Buffers",
                    Framework = "Protocol Buffers gRPC",
                    ProviderId = "protobuf-service-contract",
                    ModulePath = modulePath,
                    SourceFile = sourceFile,
                    StartLine = location.Line,
                    StartColumn = location.Column,
                    Operation = RpcIntegrationOperation.Endpoint,
                    Protocol = "gRPC",
                    ServiceIdentity = serviceIdentity,
                    MethodIdentity = methodIdentity,
                    TargetIdentity = methodIdentity,
                    TargetExpression = signature.Expression,
                    ResolutionStatus = resolution,
                    ResolutionEvidence = Array.AsReadOnly(resolutionEvidence),
                    DeclarationEvidence = source.Substring(
                        tokens[methodIndex].Offset,
                        Math.Max(0, signature.EndOffset - tokens[methodIndex].Offset))
                });

                methodIndex = Math.Max(methodIndex, signature.LastTokenIndex - 1);
            }

            index = serviceEnd;
        }

        return evidence;
    }

    private static string ReadPackage(IReadOnlyList<Token> tokens)
    {
        for (var index = 0; index + 2 < tokens.Count; index++)
        {
            if (tokens[index].Value == "package"
                && IsQualifiedIdentifier(tokens[index + 1].Value)
                && tokens[index + 2].Value == ";")
                return tokens[index + 1].Value;
        }

        return string.Empty;
    }

    private static RpcSignature ReadRpcSignature(
        IReadOnlyList<Token> tokens,
        int start,
        int limit)
    {
        var index = start;
        if (index >= limit || tokens[index].Value != "(")
            return RpcSignature.Incomplete(start, tokens, limit);

        var requestStart = index;
        var requestEnd = FindMatchingParenthesis(tokens, index, limit);
        if (requestEnd < 0)
            return RpcSignature.Incomplete(start);

        index = requestEnd + 1;
        if (index >= limit || tokens[index].Value != "returns")
            return RpcSignature.Incomplete(start);
        index++;

        if (index < limit && tokens[index].Value == "stream")
            index++;

        if (index >= limit || tokens[index].Value != "(")
            return RpcSignature.Incomplete(start);

        var responseEnd = FindMatchingParenthesis(tokens, index, limit);
        if (responseEnd < 0)
            return RpcSignature.Incomplete(start);

        var requestTypeStart = requestStart + 1;
        var responseTypeStart = index + 1;
        if (requestTypeStart < requestEnd && tokens[requestTypeStart].Value == "stream")
            requestTypeStart++;
        if (responseTypeStart < responseEnd && tokens[responseTypeStart].Value == "stream")
            responseTypeStart++;

        var requestType = JoinType(tokens, requestTypeStart, requestEnd);
        var responseType = JoinType(tokens, responseTypeStart, responseEnd);
        var endIndex = responseEnd + 1;
        if (endIndex < limit && tokens[endIndex].Value == "{")
        {
            var optionsEnd = FindMatchingBrace(tokens, endIndex);
            if (optionsEnd >= 0)
                endIndex = optionsEnd;
        }
        else if (endIndex < limit && tokens[endIndex].Value == ";")
        {
            // The semicolon is part of the declaration.
        }

        return new RpcSignature(
            !string.IsNullOrWhiteSpace(requestType) && !string.IsNullOrWhiteSpace(responseType),
            requestType + " -> " + responseType,
            endIndex < tokens.Count ? tokens[endIndex].Offset + tokens[endIndex].Value.Length : 0,
            endIndex);
    }

    private static string JoinType(IReadOnlyList<Token> tokens, int start, int end) =>
        string.Concat(tokens.Skip(start).Take(end - start).Select(token => token.Value));

    private static int FindMatchingBrace(IReadOnlyList<Token> tokens, int opening)
    {
        var depth = 0;
        for (var index = opening; index < tokens.Count; index++)
        {
            if (tokens[index].Value == "{") depth++;
            else if (tokens[index].Value == "}" && --depth == 0) return index;
        }

        return -1;
    }

    private static int FindMatchingParenthesis(IReadOnlyList<Token> tokens, int opening, int limit)
    {
        var depth = 0;
        for (var index = opening; index < limit; index++)
        {
            if (tokens[index].Value == "(") depth++;
            else if (tokens[index].Value == ")" && --depth == 0) return index;
        }

        return -1;
    }

    private static (int Line, int Column) GetLineAndColumn(string source, int offset)
    {
        var line = 1;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] != '\n') continue;
            line++;
            lineStart = index + 1;
        }

        return (line, offset - lineStart + 1);
    }

    private static string CreateEvidenceId(string file, int offset, string identity)
    {
        var input = $"{file}|{offset}|{identity}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool IsQualifiedIdentifier(string value) =>
        value.Split('.').All(IsIdentifier);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record Token(string Value, int Offset);

    private sealed record RpcSignature(
        bool IsComplete,
        string Expression,
        int EndOffset,
        int LastTokenIndex)
    {
        public static RpcSignature Incomplete(
            int tokenIndex,
            IReadOnlyList<Token> tokens,
            int limit)
        {
            var endIndex = tokenIndex;
            while (endIndex < limit
                && tokens[endIndex].Value is not (";" or "}"))
                endIndex++;
            if (endIndex < limit && tokens[endIndex].Value == ";")
                endIndex++;

            var endOffset = endIndex > tokenIndex
                ? tokens[endIndex - 1].Offset + tokens[endIndex - 1].Value.Length
                : 0;
            return new RpcSignature(false, string.Empty, endOffset, endIndex);
        }
    }
}
