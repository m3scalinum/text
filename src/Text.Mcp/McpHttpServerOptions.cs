using System;
using System.Net;

namespace M3scalinum.Text.Mcp;

/// <summary>
/// The network surface of <see cref="McpHttpServer"/>: where it listens, who may call it, and how many
/// sessions it keeps.
/// </summary>
/// <remarks>
/// <para>
/// Every value defaults to the restrictive choice: loopback only, a session per <c>initialize</c> and
/// a cap on how many may exist at once. <see cref="BearerToken"/> has no default — a network server
/// with no credential is not a default this package will pick for a host, so the host must state one.
/// </para>
/// <para>
/// The sizing limits are not repeated here: <see cref="McpServerOptions.MaxRequestSize"/> and
/// <see cref="McpServerOptions.MaxDocumentSize"/> apply to an HTTP request exactly as they apply to a
/// line on stdio.
/// </para>
/// </remarks>
public sealed class McpHttpServerOptions
{
    /// <summary>The default endpoint path a client posts to.</summary>
    public const string DefaultPath = "/mcp";

    /// <summary>The default ceiling on concurrent sessions.</summary>
    public const int DefaultMaxSessions = 16;

    /// <summary>The default ceiling on one request's header block: 16 KiB.</summary>
    public const int DefaultMaxHeaderBytes = 16 * 1024;

    /// <summary>The bearer token every request must carry.</summary>
    /// <remarks>
    /// Required. Compared in constant time against the <c>Authorization: Bearer …</c> header, so a
    /// caller learns only whether it matched, not how far into the token it got.
    /// </remarks>
    public required string BearerToken { get; init; }

    /// <summary>
    /// The address to listen on. Defaults to <see cref="IPAddress.Loopback"/>, so the server is not
    /// reachable from the network unless the host says so.
    /// </summary>
    public IPAddress Address { get; init; } = IPAddress.Loopback;

    /// <summary>
    /// The TCP port. Defaults to 0, which asks the operating system for a free port — the server
    /// reports the one it got through <see cref="McpHttpServer.Port"/>.
    /// </summary>
    public int Port { get; init; }

    /// <summary>The only path that is served. Defaults to <see cref="DefaultPath"/>.</summary>
    public string Path { get; init; } = DefaultPath;

    /// <summary>
    /// How many sessions may live at once. Defaults to <see cref="DefaultMaxSessions"/>; an
    /// <c>initialize</c> beyond it is refused with <c>503</c>.
    /// </summary>
    public int MaxSessions { get; init; } = DefaultMaxSessions;

    /// <summary>
    /// How long a session may go untouched before it is forgotten. Defaults to one hour.
    /// </summary>
    /// <remarks>
    /// A session carries no document and no compiler state — it is only a name the server agrees to
    /// remember — so expiry costs a client its session id, not its work in progress.
    /// </remarks>
    public TimeSpan SessionIdleTimeout { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long one request may take, from the first byte of its head to the last byte of its body.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The largest header block one request may carry.</summary>
    public int MaxHeaderBytes { get; init; } = DefaultMaxHeaderBytes;

    /// <summary>Rejects a value that cannot work.</summary>
    /// <exception cref="ArgumentException">
    /// The token is blank, the path is not an absolute path, a limit is not positive, or the port is
    /// out of range.
    /// </exception>
    /// <exception cref="ArgumentNullException">The address is <see langword="null"/>.</exception>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Address);

        if (string.IsNullOrWhiteSpace(BearerToken))
        {
            throw new ArgumentException(
                "BearerToken must not be blank: an HTTP surface with no credential is not served",
                nameof(BearerToken));
        }

        if (string.IsNullOrEmpty(Path) || Path[0] != '/')
        {
            throw new ArgumentException("Path must be an absolute path such as /mcp", nameof(Path));
        }

        if (Port is < 0 or > 65535)
        {
            throw new ArgumentException("Port must be between 0 and 65535", nameof(Port));
        }

        if (MaxSessions <= 0)
        {
            throw new ArgumentException("MaxSessions must be positive", nameof(MaxSessions));
        }

        if (SessionIdleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("SessionIdleTimeout must be positive", nameof(SessionIdleTimeout));
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("RequestTimeout must be positive", nameof(RequestTimeout));
        }

        if (MaxHeaderBytes < 256)
        {
            throw new ArgumentException(
                "MaxHeaderBytes must be at least 256, or a request line does not fit", nameof(MaxHeaderBytes));
        }
    }
}
