using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp.Internal;

namespace M3scalinum.Text.Mcp;

/// <summary>
/// Serves the protocol on a pair of streams: one UTF-8 JSON-RPC message per line, no headers, no
/// length prefix.
/// </summary>
/// <remarks>
/// <para>
/// This is the transport a host embeds in its own process — <see cref="McpServer.RunStdioAsync"/>
/// binds it to standard input and output. End of input is a clean shutdown: <see cref="RunAsync"/>
/// returns, it does not throw.
/// </para>
/// <para>
/// The loop is sequential: a message is answered before the next is read. A line longer than the
/// limit is discarded as it is read and answered with a parse error, so a hostile line cannot grow a
/// buffer or end the session.
/// </para>
/// </remarks>
public sealed class StdioTransport : IMcpTransport
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly int _maxRequestSize;

    /// <summary>Creates a transport over a pair of streams.</summary>
    /// <param name="input">Where requests are read from, one JSON message per line.</param>
    /// <param name="output">Where responses are written, one JSON message per line.</param>
    /// <param name="maxRequestSize">
    /// The largest line the transport reads, in bytes; a longer one is discarded. Defaults to
    /// <see cref="McpServerOptions.DefaultMaxRequestSize"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">A stream is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRequestSize"/> is not positive.</exception>
    public StdioTransport(
        Stream input,
        Stream output,
        int maxRequestSize = McpServerOptions.DefaultMaxRequestSize)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequestSize);

        _input = input;
        _output = output;
        _maxRequestSize = maxRequestSize;
    }

    /// <inheritdoc />
    public async Task RunAsync(IMcpMessageHandler handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var reader = new JsonLineReader(_input, _maxRequestSize);

        while (true)
        {
            var (status, line) = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            switch (status)
            {
                case LineStatus.End:
                    return;

                case LineStatus.TooLong:
                    await WriteAsync(
                        McpServer.FrameError(
                            null,
                            McpServer.ParseError,
                            $"parse error: the message exceeds the {_maxRequestSize} byte limit"),
                        cancellationToken).ConfigureAwait(false);
                    continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            var response = await handler.HandleMessageAsync(line, cancellationToken).ConfigureAwait(false);

            if (response is not null)
            {
                await WriteAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Writes one message and the newline that frames it, then flushes.</summary>
    private async Task WriteAsync(byte[] message, CancellationToken cancellationToken)
    {
        await _output.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await _output.WriteAsync(Newline, cancellationToken).ConfigureAwait(false);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static readonly byte[] Newline = [(byte)'\n'];
}
