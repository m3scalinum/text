using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace M3scalinum.Text.Mcp.Internal;

/// <summary>What one read of the input produced.</summary>
internal enum LineStatus
{
    /// <summary>A complete line, newline stripped.</summary>
    Line,

    /// <summary>The stream ended and no partial line was left.</summary>
    End,

    /// <summary>A line longer than the limit was read and discarded.</summary>
    TooLong,
}

/// <summary>
/// Reads newline-delimited JSON messages from the transport, one at a time.
/// </summary>
/// <remarks>
/// MCP over stdio frames a message as a single line; there are no headers and no length prefix, so a
/// hostile client can send an endless line. A buffered reader that grew with the line would make
/// that an out-of-memory kill, so the limit is enforced while reading: the rest of the offending
/// line is consumed and discarded, and the session continues with the next one.
/// </remarks>
internal sealed class JsonLineReader
{
    private readonly Stream _input;
    private readonly int _limit;
    private readonly byte[] _buffer = new byte[16 * 1024];

    private int _start;
    private int _end;
    private bool _skipping;

    internal JsonLineReader(Stream input, int limit)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        _input = input;
        _limit = limit;
    }

    /// <summary>Reads the next message, or reports the end of the stream.</summary>
    internal async Task<(LineStatus Status, string Line)> ReadAsync(CancellationToken cancellationToken)
    {
        var line = new MemoryStream();

        while (true)
        {
            if (_start == _end)
            {
                _end = await _input.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _start = 0;

                if (_end == 0)
                {
                    if (_skipping || line.Length == 0)
                    {
                        return (LineStatus.End, string.Empty);
                    }

                    return (LineStatus.Line, Decode(line));
                }
            }

            var newline = System.Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            var count = (newline >= 0 ? newline : _end) - _start;

            if (!_skipping && line.Length + count > _limit)
            {
                // Drop what was accumulated: the message is already refused, and holding its prefix
                // would let a client spend the server's memory one truncated message at a time.
                _skipping = true;
                line = new MemoryStream();
            }

            if (!_skipping)
            {
                line.Write(_buffer, _start, count);
            }

            if (newline >= 0)
            {
                _start = newline + 1;

                if (_skipping)
                {
                    _skipping = false;
                    return (LineStatus.TooLong, string.Empty);
                }

                return (LineStatus.Line, Decode(line));
            }

            _start = _end;
        }
    }

    private static string Decode(MemoryStream line)
    {
        var bytes = line.GetBuffer().AsSpan(0, (int)line.Length);

        // A CRLF transport is legal; the message itself never ends in a carriage return.
        if (!bytes.IsEmpty && bytes[^1] == (byte)'\r')
        {
            bytes = bytes[..^1];
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
