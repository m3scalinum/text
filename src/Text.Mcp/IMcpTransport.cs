using System;
using System.Threading;
using System.Threading.Tasks;

namespace M3scalinum.Text.Mcp;

/// <summary>
/// The protocol, with no knowledge of how a message arrived.
/// </summary>
/// <remarks>
/// <para>
/// A JSON-RPC message in, either one JSON-RPC message out or <see langword="null"/> when the message
/// is a notification or is otherwise not to be answered. The handler owns no framing: it never sees a
/// newline, a header or a socket, which is what lets <see cref="McpServer"/> serve stdio and HTTP with
/// the same dispatch.
/// </para>
/// <para>
/// The returned bytes are one complete JSON-RPC message, without a trailing newline.
/// <see cref="McpServer"/> implements this interface; a transport that needs to answer a malformed
/// frame itself does so before calling it.
/// </para>
/// </remarks>
public interface IMcpMessageHandler
{
    /// <summary>Handles one message.</summary>
    /// <param name="message">One complete JSON-RPC message, as text.</param>
    /// <param name="cancellationToken">Ends the work when the transport is closing.</param>
    /// <returns>
    /// The JSON-RPC response as UTF-8 bytes, or <see langword="null"/> when the message is not to be
    /// answered.
    /// </returns>
    Task<byte[]?> HandleMessageAsync(string message, CancellationToken cancellationToken);
}

/// <summary>
/// A framing a host can serve the protocol over: newline-delimited JSON on a pair of streams, an HTTP
/// endpoint, or anything else a host needs.
/// </summary>
/// <remarks>
/// <para>
/// The transport reads messages, hands each one to an <see cref="IMcpMessageHandler"/>, and writes
/// whatever comes back in the framing it owns. It is the only place where a byte becomes a message:
/// the protocol core never sees the stream, so a change of transport cannot change a response.
/// </para>
/// <para>
/// Implementations: <see cref="StdioTransport"/> — one message per line — and
/// <see cref="McpHttpServer"/> — the MCP streamable-HTTP shape.
/// </para>
/// </remarks>
public interface IMcpTransport
{
    /// <summary>
    /// Serves the protocol until the transport ends or <paramref name="cancellationToken"/> is
    /// cancelled.
    /// </summary>
    /// <param name="handler">The message handler, normally an <see cref="McpServer"/>.</param>
    /// <param name="cancellationToken">Stops the transport.</param>
    /// <returns>A task that completes when the transport has no more messages to serve.</returns>
    Task RunAsync(IMcpMessageHandler handler, CancellationToken cancellationToken);
}
