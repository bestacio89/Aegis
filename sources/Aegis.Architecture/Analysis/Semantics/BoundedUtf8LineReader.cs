using System.Text;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Reads bounded UTF-8 lines from a byte stream without materialising an
/// unbounded response or losing bytes read beyond the current frame.
/// </summary>
internal sealed class BoundedUtf8LineReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly Stream _stream;
    private readonly int _maximumLineBytes;
    private readonly byte[] _buffer = new byte[4096];
    private int _bufferOffset;
    private int _bufferLength;

    public BoundedUtf8LineReader(Stream stream, int maximumLineBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLineBytes);
        _stream = stream;
        _maximumLineBytes = maximumLineBytes;
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        using var line = new MemoryStream();
        while (true)
        {
            if (_bufferOffset == _bufferLength)
            {
                _bufferLength = await _stream.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _bufferOffset = 0;
                if (_bufferLength == 0)
                {
                    if (line.Length == 0)
                        return null;
                    throw new EndOfStreamException("The stream ended before the response frame delimiter.");
                }
            }

            var value = _buffer[_bufferOffset++];
            if (value == (byte)'\n')
            {
                var bytes = line.ToArray();
                var length = bytes.Length > 0 && bytes[^1] == (byte)'\r' ? bytes.Length - 1 : bytes.Length;
                try
                {
                    return StrictUtf8.GetString(bytes, 0, length);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new InvalidDataException("The response frame is not valid UTF-8.", exception);
                }
            }

            if (line.Length >= _maximumLineBytes)
                throw new InvalidDataException("The response frame exceeds the configured byte limit.");
            line.WriteByte(value);
        }
    }
}
