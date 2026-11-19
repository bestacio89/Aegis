using System.Text;
using Aegis.Architecture.Analysis.Semantics;
using FluentAssertions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class BoundedUtf8LineReaderTests
{
    [Fact]
    public async Task ReadLineAsync_returns_a_valid_frame_within_the_limit()
    {
        const string frame = "{\"status\":\"ok\"}";
        var reader = CreateReader(frame + "\n", Encoding.UTF8.GetByteCount(frame));
        (await reader.ReadLineAsync()).Should().Be(frame);
    }

    [Fact]
    public async Task ReadLineAsync_accepts_a_frame_exactly_at_the_limit()
    {
        const string frame = "12345678";
        var reader = CreateReader(frame + "\n", frame.Length);
        (await reader.ReadLineAsync()).Should().Be(frame);
    }

    [Fact]
    public async Task ReadLineAsync_rejects_a_frame_that_exceeds_the_limit()
    {
        var reader = CreateReader("123456789\n", 8);
        var action = () => reader.ReadLineAsync().AsTask();
        await action.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*exceeds the configured byte limit*");
    }

    [Fact]
    public async Task ReadLineAsync_rejects_an_incomplete_frame_at_end_of_stream()
    {
        var reader = CreateReader("{\"status\":", 64);
        var action = () => reader.ReadLineAsync().AsTask();
        await action.Should().ThrowAsync<EndOfStreamException>()
            .WithMessage("*before the response frame delimiter*");
    }

    [Fact]
    public async Task ReadLineAsync_rejects_invalid_utf8()
    {
        var reader = new BoundedUtf8LineReader(new MemoryStream([0xC3, 0x28, 0x0A]), 16);
        var action = () => reader.ReadLineAsync().AsTask();
        await action.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*not valid UTF-8*");
    }

    [Fact]
    public async Task ReadLineAsync_preserves_bytes_buffered_after_the_current_frame()
    {
        var reader = CreateReader("first\nsecond\n", 16);
        (await reader.ReadLineAsync()).Should().Be("first");
        (await reader.ReadLineAsync()).Should().Be("second");
    }

    [Fact]
    public async Task ReadLineAsync_observes_cancellation_while_waiting_for_response_bytes()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var reader = new BoundedUtf8LineReader(new BlockingReadStream(), 16);
        var action = () => reader.ReadLineAsync(cancellation.Token).AsTask();
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static BoundedUtf8LineReader CreateReader(string content, int maximumBytes) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(content)), maximumBytes);

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
