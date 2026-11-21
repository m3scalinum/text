using System;

namespace M3scalinum.Text.Mcp.Internal;

/// <summary>
/// A tool call the server refuses before doing any work: an unknown tool, a missing argument, an
/// argument of the wrong shape, or a file path it is not allowed to write.
/// </summary>
/// <remarks>
/// The message is what the client sees, so it names the argument and what was wrong with it rather
/// than saying "invalid arguments".
/// </remarks>
internal sealed class McpToolArgumentsException : Exception
{
    internal McpToolArgumentsException(string message)
        : base(message)
    {
    }

    internal McpToolArgumentsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
