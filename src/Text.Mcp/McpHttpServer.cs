using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using M3scalinum.Text.Mcp.Internal;

namespace M3scalinum.Text.Mcp;

/// <summary>
/// Serves the protocol over HTTP/1.1, in the MCP streamable-HTTP shape: a client posts one JSON-RPC
/// message, the server answers with one JSON-RPC message, or <c>202 Accepted</c> when there is
/// nothing to answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <see cref="TcpListener"/> and a hand-written HTTP/1.1 parser.</b> <c>HttpListener</c> on
/// Windows binds through the HTTP Server API, which needs a URL reservation the process usually does
/// not have — a test host would have to run elevated — and it is not verified here to be trim- and
/// AOT-clean, which this package's <c>IsAotCompatible</c> requires. <see cref="TcpListener"/> is in
/// the shared framework, needs no reservation on loopback, forces no dependency, and the protocol
/// this server needs is a request line, headers and a <c>Content-Length</c> body. A smaller surface
/// is also a smaller attack surface, which matters more here than HTTP completeness: no chunked
/// encoding, no upgrades, no HTTP/2.
/// </para>
/// <para>
/// The transport is the only place that knows about the network. It hands each body to an
/// <see cref="IMcpMessageHandler"/> — normally an <see cref="McpServer"/> — and writes whatever comes
/// back; dispatch is the same code the stdio transport drives.
/// </para>
/// <para>
/// Sessions are the streamable-HTTP idea of a conversation: <c>initialize</c> issues
/// <c>Mcp-Session-Id</c>, every later request must carry it, and <c>DELETE</c> ends it. They carry no
/// document and no compiler state — naming one is all the server remembers.
/// </para>
/// </remarks>
public sealed class McpHttpServer : IMcpTransport, IAsyncDisposable
{
    /// <summary>The header that names a session.</summary>
    public const string SessionHeader = "Mcp-Session-Id";

    private static readonly byte[] Continue = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();

    private readonly McpServerOptions _serverOptions;
    private readonly McpHttpServerOptions _options;
    private readonly byte[] _tokenHash;
    private readonly Dictionary<string, DateTimeOffset> _sessions = new(StringComparer.Ordinal);
    private readonly object _sessionsGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<long, Task> _connections = [];
    private readonly object _connectionsGate = new();
    private long _connectionCount;

    private TcpListener? _listener;
    private CancellationTokenSource? _link;
    private Task? _accept;
    private Uri? _endpoint;

    /// <summary>Creates a server; nothing is bound until <see cref="StartAsync"/>.</summary>
    /// <param name="serverOptions">The protocol limits, applied to every request.</param>
    /// <param name="httpOptions">Where to listen, the bearer token, and the session cap.</param>
    /// <exception cref="ArgumentNullException">An option object is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A value cannot work.</exception>
    public McpHttpServer(McpServerOptions serverOptions, McpHttpServerOptions httpOptions)
    {
        ArgumentNullException.ThrowIfNull(serverOptions);
        ArgumentNullException.ThrowIfNull(httpOptions);

        httpOptions.Validate();

        _serverOptions = serverOptions;
        _options = httpOptions;
        _tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(httpOptions.BearerToken));
    }

    /// <summary>The protocol limits this server applies.</summary>
    public McpServerOptions ServerOptions => _serverOptions;

    /// <summary>The HTTP surface: address, endpoint path and session cap.</summary>
    public McpHttpServerOptions Options => _options;

    /// <summary>The port actually bound; the one the operating system chose when <c>Port</c> was 0.</summary>
    /// <exception cref="InvalidOperationException">The server has not been started.</exception>
    public int Port => _listener?.LocalEndpoint is IPEndPoint endpoint
        ? endpoint.Port
        : throw new InvalidOperationException("the server has not been started");

    /// <summary>Where a client posts, as <c>http://127.0.0.1:PORT/mcp</c>.</summary>
    /// <exception cref="InvalidOperationException">The server has not been started.</exception>
    public Uri Endpoint => _endpoint ?? throw new InvalidOperationException("the server has not been started");

    /// <summary>Binds the port and starts accepting connections.</summary>
    /// <param name="handler">The protocol handler, normally an <see cref="McpServer"/>.</param>
    /// <param name="cancellationToken">Closes the listener and the connections it serves.</param>
    /// <remarks>
    /// Binding happens before this returns, so <see cref="Port"/> is readable as soon as it does.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The server was already started.</exception>
    /// <exception cref="SocketException">The address and port cannot be bound.</exception>
    public Task StartAsync(IMcpMessageHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_listener is not null)
        {
            throw new InvalidOperationException("the server has already been started");
        }

        var listener = new TcpListener(_options.Address, _options.Port);
        listener.Start(backlog: 64);

        var bound = (IPEndPoint)listener.LocalEndpoint;

        _listener = listener;
        _link = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        _endpoint = new Uri($"http://{FormatAddress(bound.Address)}:{bound.Port}{_options.Path}");
        _accept = AcceptAsync(handler, _link.Token);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Starts the server, then serves until <paramref name="cancellationToken"/> is cancelled or
    /// <see cref="DisposeAsync"/> is called.
    /// </remarks>
    public async Task RunAsync(IMcpMessageHandler handler, CancellationToken cancellationToken)
    {
        await StartAsync(handler, cancellationToken).ConfigureAwait(false);

        await _accept!.ConfigureAwait(false);
    }

    /// <summary>Stops the listener and forgets every session.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);

        _listener?.Stop();

        if (_accept is not null)
        {
            try
            {
                await _accept.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The accept loop ends with the token it was given; that is the shutdown.
            }
        }

        Task[] pending;

        lock (_connectionsGate)
        {
            pending = [.. _connections.Values];
        }

        if (pending.Length > 0)
        {
            try
            {
                // The cancellation above ends each connection's read; the wait is for them to finish
                // closing their sockets before the tokens they use are disposed.
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A connection that will not stop must not stop the host from shutting down.
            }
        }

        _link?.Dispose();
        _stop.Dispose();

        lock (_sessionsGate)
        {
            _sessions.Clear();
        }
    }

    /// <summary>Accepts connections until the token is cancelled; each one is served on its own.</summary>
    private async Task AcceptAsync(IMcpMessageHandler handler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // A connection that died before it was accepted is not a reason to stop listening.
                continue;
            }

            var id = Interlocked.Increment(ref _connectionCount);
            var connection = ServeAsync(client, handler, cancellationToken);

            lock (_connectionsGate)
            {
                _connections[id] = connection;
            }

            _ = ForgetAsync(id, connection);
        }
    }

    /// <summary>Remembers nothing about a connection once it is done.</summary>
    private async Task ForgetAsync(long id, Task connection)
    {
        await connection.ConfigureAwait(false);

        lock (_connectionsGate)
        {
            _connections.Remove(id);
        }
    }

    /// <summary>Reads requests off one connection until it ends or the server stops.</summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A connection is untrusted input: whatever it does, it may end its own "
            + "connection and must not be able to end the listener.")]
    private async Task ServeAsync(TcpClient client, IMcpMessageHandler handler, CancellationToken cancellationToken)
    {
        using (client)
        {
            var socket = client.Client;
            await using var stream = client.GetStream();
            var wire = new HttpWireReader(stream);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    turn.CancelAfter(_options.RequestTimeout);

                    var head = await wire.ReadHeadAsync(_options.MaxHeaderBytes, turn.Token)
                        .ConfigureAwait(false);

                    if (head is null)
                    {
                        return;
                    }

                    var keepAlive = await ExchangeAsync(stream, wire, head, handler, turn.Token)
                        .ConfigureAwait(false);

                    if (!keepAlive)
                    {
                        await DrainAndShutdownAsync(socket).ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (HttpWireException exception)
            {
                // The request never became a message: answer with the status the parser chose, so the
                // client learns why instead of seeing its connection disappear.
                try
                {
                    await WriteBareResponseAsync(stream, exception.StatusCode).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    // The client is already gone: there is no one left to read the status.
                    return;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The connection is the unit of failure: a client that sends nonsense, disappears, or
                // lets its request time out loses that connection, not the server.
            }
        }
    }

    /// <summary>Answers one request; returns whether the connection may carry another.</summary>
    private async Task<bool> ExchangeAsync(
        Stream stream,
        HttpWireReader wire,
        HttpRequestHead head,
        IMcpMessageHandler handler,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(head.Path, _options.Path, StringComparison.Ordinal))
        {
            await RejectAsync(stream, head, 404).ConfigureAwait(false);
            return false;
        }

        if (head.Method == "DELETE")
        {
            return await DeleteAsync(stream, head, cancellationToken).ConfigureAwait(false);
        }

        if (head.Method != "POST")
        {
            await RejectAsync(stream, head, 405, ("Allow", "POST, DELETE")).ConfigureAwait(false);
            return false;
        }

        if (!IsAuthorized(head.Header("authorization")))
        {
            await RejectAsync(stream, head, 401, ("WWW-Authenticate", "Bearer realm=\"mcp\""))
                .ConfigureAwait(false);
            return false;
        }

        var contentType = head.Header("content-type");

        if (contentType is not null
            && !contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(stream, head, 415).ConfigureAwait(false);
            return false;
        }

        var length = head.ContentLength;

        if (length is null or 0)
        {
            await RejectAsync(stream, head, 411).ConfigureAwait(false);
            return false;
        }

        if (length > _serverOptions.MaxRequestSize)
        {
            // Refused on the declared length, before a single body byte is stored.
            await RejectAsync(stream, head, 413).ConfigureAwait(false);
            return false;
        }

        var sessionId = head.Header(SessionHeader);

        if (sessionId is not null && !TouchSession(sessionId))
        {
            // Refused before the body is read: an unknown session costs the server nothing.
            await RejectAsync(stream, head, 404).ConfigureAwait(false);
            return false;
        }

        if (string.Equals(head.Header("expect"), "100-continue", StringComparison.OrdinalIgnoreCase))
        {
            // A client that waits for this would otherwise sit until its own timeout.
            await stream.WriteAsync(Continue, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var body = await wire.ReadBodyAsync(length.Value, _serverOptions.MaxRequestSize, cancellationToken)
            .ConfigureAwait(false);

        var message = Encoding.UTF8.GetString(body);

        string? issued = null;

        if (sessionId is null)
        {
            if (!IsInitialize(message))
            {
                await RejectAsync(stream, head, 404).ConfigureAwait(false);
                return false;
            }

            issued = CreateSession();

            if (issued is null)
            {
                await RejectAsync(stream, head, 503).ConfigureAwait(false);
                return false;
            }
        }

        var response = await handler.HandleMessageAsync(message, cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            await WriteResponseAsync(stream, head, 202, contentType: null, body: null, issued, cancellationToken)
                .ConfigureAwait(false);
            return head.KeepAlive;
        }

        var accept = head.Header("accept") ?? string.Empty;
        var eventStream = accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase)
            && !accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);

        if (eventStream)
        {
            var framed = Encoding.UTF8.GetBytes($"event: message\ndata: {Encoding.UTF8.GetString(response)}\n\n");

            await WriteResponseAsync(
                stream, head, 200, "text/event-stream; charset=utf-8", framed, issued, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await WriteResponseAsync(
                stream, head, 200, "application/json; charset=utf-8", response, issued, cancellationToken)
                .ConfigureAwait(false);
        }

        return head.KeepAlive;
    }

    /// <summary>Ends the session the request names.</summary>
    private async Task<bool> DeleteAsync(Stream stream, HttpRequestHead head, CancellationToken cancellationToken)
    {
        if (!IsAuthorized(head.Header("authorization")))
        {
            await RejectAsync(stream, head, 401, ("WWW-Authenticate", "Bearer realm=\"mcp\""))
                .ConfigureAwait(false);
            return false;
        }

        var sessionId = head.Header(SessionHeader);
        var closed = false;

        if (sessionId is not null)
        {
            lock (_sessionsGate)
            {
                closed = _sessions.Remove(sessionId);
            }
        }

        if (!closed)
        {
            await RejectAsync(stream, head, 404).ConfigureAwait(false);
            return false;
        }

        await WriteResponseAsync(stream, head, 204, contentType: null, body: null, issued: null, cancellationToken)
            .ConfigureAwait(false);

        return head.KeepAlive;
    }

    /// <summary>Compares the bearer token without telling the caller how far it matched.</summary>
    private bool IsAuthorized(string? authorization)
    {
        var provided = authorization is not null
            && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authorization["Bearer ".Length..].Trim()
                : string.Empty;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            _tokenHash);
    }

    /// <summary>Registers a session, or <see langword="null"/> when the cap is reached.</summary>
    private string? CreateSession()
    {
        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid().ToString("N");

        lock (_sessionsGate)
        {
            Prune(now);

            if (_sessions.Count >= _options.MaxSessions)
            {
                return null;
            }

            _sessions[sessionId] = now;
        }

        return sessionId;
    }

    /// <summary>Records activity on a session, or reports that it is unknown or expired.</summary>
    private bool TouchSession(string sessionId)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_sessionsGate)
        {
            if (!_sessions.TryGetValue(sessionId, out var lastUsed)
                || now - lastUsed > _options.SessionIdleTimeout)
            {
                _sessions.Remove(sessionId);
                return false;
            }

            _sessions[sessionId] = now;
            return true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        List<string>? expired = null;

        foreach (var (sessionId, lastUsed) in _sessions)
        {
            if (now - lastUsed > _options.SessionIdleTimeout)
            {
                (expired ??= []).Add(sessionId);
            }
        }

        if (expired is null)
        {
            return;
        }

        foreach (var sessionId in expired)
        {
            _sessions.Remove(sessionId);
        }
    }

    /// <summary>Answers a transport-level refusal: no JSON-RPC body, and the connection closes.</summary>
    private static Task RejectAsync(
        Stream stream,
        HttpRequestHead head,
        int status,
        params (string Name, string Value)[] headers)
        => WriteResponseAsync(stream, head, status, contentType: null, body: null, issued: null,
            CancellationToken.None, headers);

    /// <summary>Answers a request the parser refused, with nothing but the status.</summary>
    private static async Task WriteBareResponseAsync(Stream stream, int status)
    {
        var text = new StringBuilder(128)
            .Append("HTTP/1.1 ").Append(status).Append(' ').Append(ReasonPhrase(status)).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Content-Length: 0\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(text)).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>Writes one HTTP response: status line, headers, optional body, flushed.</summary>
    private static async Task WriteResponseAsync(
        Stream stream,
        HttpRequestHead head,
        int status,
        string? contentType,
        byte[]? body,
        string? issued,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] headers)
    {
        var text = new StringBuilder(256);

        text.Append("HTTP/1.1 ").Append(status).Append(' ').Append(ReasonPhrase(status)).Append("\r\n");
        text.Append("Cache-Control: no-store\r\n");

        if (contentType is not null)
        {
            text.Append("Content-Type: ").Append(contentType).Append("\r\n");
        }

        if (issued is not null)
        {
            text.Append(SessionHeader).Append(": ").Append(issued).Append("\r\n");
        }

        foreach (var (name, value) in headers)
        {
            text.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        text.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");

        // A refusal closes the connection: nothing of the refused request is drained from this side,
        // so telling the client to reuse it would be a promise the server does not keep.
        var keepAlive = head.KeepAlive && status < 300;

        text.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(text.ToString()), cancellationToken).ConfigureAwait(false);

        if (body is { Length: > 0 })
        {
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the sending side, then reads and discards what the client is still sending, so a
    /// refusal is read by the client instead of arriving as a reset.
    /// </summary>
    private static async Task DrainAndShutdownAsync(Socket socket)
    {
        try
        {
            socket.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var buffer = new byte[4096];
        var drained = 0;

        try
        {
            while (drained < 64 * 1024)
            {
                var read = await socket.ReceiveAsync(buffer, SocketFlags.None, budget.Token).ConfigureAwait(false);

                if (read == 0)
                {
                    return;
                }

                drained += read;
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException
            or SocketException
            or ObjectDisposedException)
        {
            // The client stopped sending, or the socket is gone: either way there is nothing to drain.
        }
    }

    /// <summary>Whether a message is an <c>initialize</c> request, the only message that opens a session.</summary>
    private static bool IsInitialize(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("method", out var method)
                && method.ValueKind == JsonValueKind.String
                && string.Equals(method.GetString(), "initialize", StringComparison.Ordinal)
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind is not JsonValueKind.Null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string FormatAddress(IPAddress address)
    {
        var text = address.ToString();

        return address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{text}]" : text;
    }

    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        411 => "Length Required",
        413 => "Content Too Large",
        415 => "Unsupported Media Type",
        431 => "Request Header Fields Too Large",
        503 => "Service Unavailable",
        _ => "Error",
    };
}
