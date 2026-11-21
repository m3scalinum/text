using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace M3scalinum.Text.Mcp.Internal;

/// <summary>A request the wire refused, with the status the server answers with.</summary>
internal sealed class HttpWireException : Exception
{
    internal HttpWireException(int statusCode, string message)
        : base(message) => StatusCode = statusCode;

    /// <summary>The HTTP status the server answers with.</summary>
    internal int StatusCode { get; }
}

/// <summary>The head of one HTTP/1.1 request, parsed.</summary>
internal sealed class HttpRequestHead
{
    internal HttpRequestHead(
        string method,
        string target,
        string version,
        Dictionary<string, string> headers)
    {
        Method = method;
        Target = target;
        Version = version;
        Headers = headers;
    }

    /// <summary>The method, upper case as sent.</summary>
    internal string Method { get; }

    /// <summary>The request target, query string included.</summary>
    internal string Target { get; }

    /// <summary>The protocol version, <c>HTTP/1.1</c> here.</summary>
    internal string Version { get; }

    /// <summary>The headers, keyed case-insensitively.</summary>
    internal Dictionary<string, string> Headers { get; }

    /// <summary>The request path: the target with any query string removed.</summary>
    internal string Path
    {
        get
        {
            var query = Target.IndexOf('?', StringComparison.Ordinal);

            return query < 0 ? Target : Target[..query];
        }
    }

    /// <summary>A header, or <see langword="null"/> when it was not sent.</summary>
    internal string? Header(string name)
        => Headers.TryGetValue(name, out var value) ? value : null;

    /// <summary>Whether the connection asks to stay open after the response.</summary>
    internal bool KeepAlive
    {
        get
        {
            var connection = Header("connection");

            if (connection is not null
                && connection.Contains("close", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return Version == "HTTP/1.1"
                || (connection?.Contains("keep-alive", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    /// <summary>
    /// The declared body length, or <see langword="null"/> when the request carries no body.
    /// </summary>
    internal long? ContentLength
    {
        get
        {
            var value = Header("content-length");

            if (value is null)
            {
                return null;
            }

            return long.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var length)
                ? length
                : throw new HttpWireException(400, "Content-Length is not a number");
        }
    }
}

/// <summary>
/// Reads HTTP/1.1 requests off a connection, one at a time, keeping the bytes of the next request
/// when a read brought more than one.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written, because the package must stay dependency-free and AOT-clean and this is the smaller
/// surface: a request line, headers, a <c>Content-Length</c> body. Nothing else is accepted — no
/// chunked encoding, no upgrades, no HTTP/2 — and what is not accepted is refused with the status
/// that says so, before the body is read.
/// </para>
/// <para>
/// The header block is capped as it is read, so a client that never sends the blank line cannot grow
/// the server's memory. The body is read only after its declared length has been checked against
/// <see cref="McpServerOptions.MaxRequestSize"/>, so an oversized body is refused without being
/// buffered at all.
/// </para>
/// </remarks>
internal sealed class HttpWireReader
{
    private static readonly byte[] Terminator = "\r\n\r\n"u8.ToArray();

    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8 * 1024];

    private int _start;
    private int _end;

    internal HttpWireReader(Stream stream) => _stream = stream;

    /// <summary>Reads one request head, or <see langword="null"/> at a clean end of the connection.</summary>
    /// <exception cref="HttpWireException">The request is not one this server accepts.</exception>
    internal async Task<HttpRequestHead?> ReadHeadAsync(int maxHeaderBytes, CancellationToken cancellationToken)
    {
        var head = new MemoryStream(512);

        while (true)
        {
            if (_start == _end)
            {
                _end = await _stream
                    .ReadAsync(_buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                _start = 0;

                if (_end == 0)
                {
                    if (head.Length == 0)
                    {
                        return null;
                    }

                    throw new HttpWireException(400, "the connection ended inside a request head");
                }
            }

            var previous = (int)head.Length;
            head.Write(_buffer, _start, _end - _start);

            if (head.Length > maxHeaderBytes)
            {
                throw new HttpWireException(431, "the request head is too large");
            }

            var terminator = IndexOfTerminator(head.GetBuffer(), (int)head.Length);

            if (terminator >= 0)
            {
                // Give back whatever followed the blank line: it is the first bytes of the body, or
                // the next request on a kept-alive connection.
                _start += Math.Max(0, (terminator + Terminator.Length) - previous);
                return Parse(head.GetBuffer().AsSpan(0, terminator));
            }

            _start = _end;
        }
    }

    /// <summary>
    /// Reads exactly <paramref name="length"/> bytes of body, never more than
    /// <paramref name="limit"/>.
    /// </summary>
    /// <exception cref="HttpWireException">
    /// The body is larger than the limit, or the connection ended before it was complete.
    /// </exception>
    internal async Task<byte[]> ReadBodyAsync(long length, int limit, CancellationToken cancellationToken)
    {
        if (length > limit)
        {
            throw new HttpWireException(413, $"the body exceeds the {limit} byte limit");
        }

        var body = new MemoryStream((int)Math.Min(length, 64 * 1024));
        var chunk = new byte[(int)Math.Min(length, 16 * 1024)];
        long remaining = length;

        while (remaining > 0)
        {
            if (_start < _end)
            {
                var count = (int)Math.Min(_end - _start, remaining);
                body.Write(_buffer, _start, count);
                _start += count;
                remaining -= count;
            }
            else
            {
                var read = await _stream
                    .ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, remaining)), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    throw new HttpWireException(400, "the connection ended inside the body");
                }

                body.Write(chunk, 0, read);
                remaining -= read;
            }

            if (body.Length > limit)
            {
                throw new HttpWireException(413, $"the body exceeds the {limit} byte limit");
            }
        }

        return body.ToArray();
    }

    /// <summary>
    /// Finds the blank line that ends the head.
    /// </summary>
    /// <remarks>
    /// The whole accumulated block is scanned, not just its tail: a single read can carry the head
    /// *and* several kilobytes of body, and a tail-only scan would miss a terminator that arrived
    /// with the first bytes. The block is capped at <c>MaxHeaderBytes</c>, so the scan is bounded.
    /// </remarks>
    private static int IndexOfTerminator(byte[] buffer, int length)
        => buffer.AsSpan(0, length).IndexOf(Terminator);

    private static HttpRequestHead Parse(ReadOnlySpan<byte> head)
    {
        var lines = Encoding.Latin1.GetString(head).Split("\r\n", StringSplitOptions.None);
        var parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            throw new HttpWireException(400, "the request line is not HTTP/1.x");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];

            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0)
            {
                throw new HttpWireException(400, "a header line has no colon");
            }

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (headers.TryGetValue(name, out var existing)
                && !string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)
                && string.Equals(name, "content-length", StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpWireException(400, "Content-Length was sent twice with different values");
            }

            headers[name] = value;
        }

        if (headers.ContainsKey("transfer-encoding"))
        {
            // A chunked body is refused rather than mis-read: the length is what bounds the read, and
            // a body of unknown length is the one thing this transport will not buffer.
            throw new HttpWireException(411, "Transfer-Encoding is not supported; send Content-Length");
        }

        return new HttpRequestHead(parts[0], parts[1], parts[2], headers);
    }
}
