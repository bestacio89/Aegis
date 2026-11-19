using System.Text.Json;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>Represents the protocol limits agreed by the host and a JVM worker.</summary>
internal sealed record JvmWorkerProtocolLimits(int MaxFrameBytes, int MaxResponseBytes)
{
    public const int MinimumFrameBytes = 256;

    public static JvmWorkerProtocolLimits Negotiate(
        JsonElement handshake,
        int expectedProtocolVersion,
        int configuredMaxFrameBytes,
        int configuredMaxResponseBytes)
    {
        ValidateConfiguredLimit(configuredMaxFrameBytes, nameof(configuredMaxFrameBytes));
        ValidateConfiguredLimit(configuredMaxResponseBytes, nameof(configuredMaxResponseBytes));

        if (handshake.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The worker protocol handshake must be a JSON object.");

        var protocolVersion = ReadRequiredPositiveInteger(handshake, "protocolVersion");
        if (protocolVersion != expectedProtocolVersion)
            throw new InvalidDataException($"The worker protocol version {protocolVersion} is not supported; expected {expectedProtocolVersion}.");

        var workerMaxFrameBytes = ReadRequiredLimit(handshake, "maxFrameBytes");
        var workerMaxResponseBytes = ReadRequiredLimit(handshake, "maxResponseBytes");
        _ = ReadRequiredPositiveInteger(handshake, "maxConcurrentRequests");

        return new JvmWorkerProtocolLimits(
            Math.Min(configuredMaxFrameBytes, workerMaxFrameBytes),
            Math.Min(configuredMaxResponseBytes, workerMaxResponseBytes));
    }

    private static int ReadRequiredLimit(JsonElement handshake, string propertyName)
    {
        var value = ReadRequiredPositiveInteger(handshake, propertyName);
        if (value < MinimumFrameBytes)
            throw new InvalidDataException($"The worker advertised an invalid {propertyName} value; at least {MinimumFrameBytes} bytes are required.");

        return value;
    }

    private static int ReadRequiredPositiveInteger(JsonElement handshake, string propertyName)
    {
        if (!handshake.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value)
            || value <= 0)
        {
            throw new InvalidDataException($"The worker protocol handshake omitted a valid positive integer '{propertyName}'.");
        }

        return value;
    }

    private static void ValidateConfiguredLimit(int value, string parameterName)
    {
        if (value < MinimumFrameBytes)
            throw new ArgumentOutOfRangeException(parameterName, value, $"Protocol limits must be at least {MinimumFrameBytes} bytes.");
    }
}