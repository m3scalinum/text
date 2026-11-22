using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Mcp.Internal;

/// <summary>
/// The per-client render counter: a sliding one-minute window per session, held in memory.
/// </summary>
/// <remarks>
/// <para>
/// The window is sliding, not fixed: the oldest admitted call is the one that has to leave before the
/// next one may enter, so a client cannot render twice the ceiling by straddling a minute boundary.
/// </para>
/// <para>
/// The keys are the transport's sessions, which the transport already bounds — an HTTP server
/// refuses a session it did not issue, and stdio has one bucket — so the dictionary cannot grow with
/// the request rate. A key whose window has emptied is dropped on the next admission.
/// </para>
/// </remarks>
internal sealed class RenderQuota
{
    /// <summary>How long an admitted call occupies the window.</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly int _limit;
    private readonly Dictionary<string, Queue<DateTimeOffset>> _windows = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Creates a counter.</summary>
    /// <param name="limit">Calls one client may make per window; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is not positive.</exception>
    internal RenderQuota(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        _limit = limit;
    }

    /// <summary>The ceiling one client may reach in a window.</summary>
    internal int Limit => _limit;

    /// <summary>
    /// Admits one call for <paramref name="session"/>, or reports how long the caller must wait.
    /// </summary>
    /// <param name="session">The client: a session id, or the empty string for a stdio process.</param>
    /// <param name="now">The instant of the call, so a test does not have to wait a minute.</param>
    /// <param name="retryAfterSeconds">Seconds until the oldest admitted call leaves the window.</param>
    /// <returns>Whether the call may proceed.</returns>
    internal bool TryAcquire(string session, DateTimeOffset now, out int retryAfterSeconds)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_gate)
        {
            Prune(now);

            if (!_windows.TryGetValue(session, out var window))
            {
                window = new Queue<DateTimeOffset>(_limit);
                _windows[session] = window;
            }

            if (window.Count >= _limit)
            {
                // The queue is never empty here: it holds at least one call while the ceiling is
                // reached, and Prune removed only the calls that had already expired.
                var leaves = Window - (now - window.Peek());
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(leaves.TotalSeconds));
                return false;
            }

            window.Enqueue(now);
            retryAfterSeconds = 0;
            return true;
        }
    }

    /// <summary>Drops every call that has left the window, and every key left empty.</summary>
    private void Prune(DateTimeOffset now)
    {
        List<string>? emptied = null;

        foreach (var (session, window) in _windows)
        {
            while (window.Count > 0 && now - window.Peek() >= Window)
            {
                window.Dequeue();
            }

            if (window.Count == 0)
            {
                (emptied ??= []).Add(session);
            }
        }

        if (emptied is null)
        {
            return;
        }

        foreach (var session in emptied)
        {
            _windows.Remove(session);
        }
    }
}
