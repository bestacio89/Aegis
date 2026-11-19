using System.Text.Json;
using Aegis.Architecture.Analysis.Semantics;
using FluentAssertions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JvmWorkerProtocolLimitsTests
{
    [Fact]
    public void Negotiate_uses_the_worker_limits_when_the_worker_is_more_restrictive()
    {
        using var document = CreateHandshake(1, 1024, 2048, 2);

        var limits = JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, 4096, 8192);

        limits.Should().Be(new JvmWorkerProtocolLimits(1024, 2048));
    }

    [Fact]
    public void Negotiate_preserves_the_host_limits_when_the_worker_advertises_larger_values()
    {
        using var document = CreateHandshake(1, 8192, 16384, 2);

        var limits = JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, 2048, 4096);

        limits.Should().Be(new JvmWorkerProtocolLimits(2048, 4096));
    }

    [Fact]
    public void Negotiate_rejects_an_unsupported_protocol_version()
    {
        using var document = CreateHandshake(2, 1024, 2048, 1);

        var action = () => JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, 4096, 4096);

        action.Should().Throw<InvalidDataException>().WithMessage("*protocol version 2*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void Negotiate_rejects_invalid_worker_frame_limits(int maxFrameBytes)
    {
        using var document = CreateHandshake(1, maxFrameBytes, 2048, 1);

        var action = () => JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, 4096, 4096);

        action.Should().Throw<InvalidDataException>().WithMessage("*maxFrameBytes*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void Negotiate_rejects_invalid_worker_response_limits(int maxResponseBytes)
    {
        using var document = CreateHandshake(1, 1024, maxResponseBytes, 1);

        var action = () => JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, 4096, 4096);

        action.Should().Throw<InvalidDataException>().WithMessage("*maxResponseBytes*");
    }

    [Fact]
    public void Negotiate_rejects_a_missing_required_handshake_field()
    {
        using var document = JsonDocument.Parse("""{"protocolVersion":1,"maxFrameBytes":1024,"maxConcurrentRequests":1}""");

        var action = () => JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, 4096, 4096);

        action.Should().Throw<InvalidDataException>().WithMessage("*maxResponseBytes*");
    }

    [Fact]
    public void Negotiate_rejects_a_handshake_field_with_the_wrong_json_type()
    {
        using var document = JsonDocument.Parse("""{"protocolVersion":1,"maxFrameBytes":"1024","maxResponseBytes":2048,"maxConcurrentRequests":1}""");

        var action = () => JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, 4096, 4096);

        action.Should().Throw<InvalidDataException>().WithMessage("*maxFrameBytes*");
    }

    [Fact]
    public void Negotiate_rejects_a_non_positive_worker_concurrency_value()
    {
        using var document = CreateHandshake(1, 1024, 2048, 0);

        var action = () => JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, 4096, 4096);

        action.Should().Throw<InvalidDataException>().WithMessage("*maxConcurrentRequests*");
    }

    [Fact]
    public void Negotiate_rejects_a_non_object_handshake()
    {
        using var document = JsonDocument.Parse("[]");

        var action = () => JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, 4096, 4096);

        action.Should().Throw<InvalidDataException>().WithMessage("*JSON object*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void Negotiate_rejects_invalid_host_limits(int configuredLimit)
    {
        using var document = CreateHandshake(1, 1024, 2048, 1);

        var action = () => JvmWorkerProtocolLimits.Negotiate(document.RootElement, 1, configuredLimit, 4096);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("configuredMaxFrameBytes");
    }

    [Fact]
    public void HasSameLaunchConfiguration_compares_argument_values_not_collection_identity()
    {
        var left = new JvmWorkerLaunchOptions("java", ".", new[] { "-Xmx1g", "-jar", "worker.jar" }, 1, TimeSpan.FromSeconds(5), 1024, 2048, "worker");
        var equivalent = new JvmWorkerLaunchOptions("java", ".", new[] { "-Xmx1g", "-jar", "worker.jar" }, 1, TimeSpan.FromSeconds(5), 1024, 2048, "worker");
        var changed = equivalent with { Arguments = new[] { "-Xmx2g", "-jar", "worker.jar" } };

        JvmWorkerProcess.HasSameLaunchConfiguration(left, equivalent).Should().BeTrue();
        JvmWorkerProcess.HasSameLaunchConfiguration(left, changed).Should().BeFalse();
    }

    [Fact]
    public void HasSameLaunchConfiguration_detects_changed_protocol_limits()
    {
        var options = new JvmWorkerLaunchOptions("java", ".", ["-jar", "worker.jar"], 1, TimeSpan.FromSeconds(5), 1024, 2048, "worker");

        JvmWorkerProcess.HasSameLaunchConfiguration(options, options with { MaxResponseBytes = 4096 }).Should().BeFalse();
    }

    private static JsonDocument CreateHandshake(
        int protocolVersion,
        int maxFrameBytes,
        int maxResponseBytes,
        int maxConcurrentRequests) =>
        JsonDocument.Parse($"""{{"protocolVersion":{protocolVersion},"maxFrameBytes":{maxFrameBytes},"maxResponseBytes":{maxResponseBytes},"maxConcurrentRequests":{maxConcurrentRequests}}}""");
}