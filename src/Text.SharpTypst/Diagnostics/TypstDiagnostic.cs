using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.SharpTypst.Diagnostics;

/// <summary>A message produced by the Typst compiler.</summary>
/// <param name="Severity">Error, warning or hint.</param>
/// <param name="Message">The compiler's message, verbatim.</param>
/// <param name="Hints">Suggestions attached to the message.</param>
/// <param name="Trace">Call stack for errors raised inside a function, innermost first.</param>
/// <param name="Location">Position in the source, or <see langword="null"/> when the diagnostic has no span.</param>
/// <param name="SourceLine">
/// The text of the line the span starts on, or <see langword="null"/> when the diagnostic has no
/// span. Carried so a host can render a caret without holding the source.
/// </param>
public sealed record TypstDiagnostic(
    TypstSeverity Severity,
    string Message,
    IReadOnlyList<string> Hints,
    IReadOnlyList<string> Trace,
    TypstLocation? Location,
    string? SourceLine = null)
{
    /// <summary>Single-line form: <c>main.typ:12:8: error: unknown variable: `totl`</c>.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (Location is not null)
        {
            builder.Append(Location.File)
                .Append(':').Append(Location.Line)
                .Append(':').Append(Location.Column)
                .Append(": ");
        }

        builder.Append(Severity.ToString().ToLowerInvariant())
            .Append(": ")
            .Append(Message);

        return builder.ToString();
    }

    /// <summary>Multi-line form for console output.</summary>
    /// <param name="includeHints">Append the hints.</param>
    /// <param name="includeTrace">Append the call stack.</param>
    /// <param name="includeSnippet">Append the source line and a caret under the span.</param>
    public string ToDisplayString(
        bool includeHints = true,
        bool includeTrace = false,
        bool includeSnippet = true)
    {
        var builder = new StringBuilder(ToString());

        if (includeSnippet && Location is not null && SourceLine is not null)
        {
            AppendSnippet(builder, Location, SourceLine);
        }

        if (includeHints)
        {
            foreach (var hint in Hints)
            {
                builder.AppendLine().Append("  hint: ").Append(hint);
            }
        }

        if (includeTrace)
        {
            foreach (var frame in Trace)
            {
                builder.AppendLine().Append("  at ").Append(frame);
            }
        }

        return builder.ToString();
    }

    private static void AppendSnippet(StringBuilder builder, TypstLocation location, string sourceLine)
    {
        var number = location.Line.ToString(CultureInfo.InvariantCulture);
        var gutter = new string(' ', number.Length);

        var start = Math.Max(0, location.Column - 1);
        var width = location.EndLine == location.Line
            ? Math.Max(1, location.EndColumn - location.Column)
            : Math.Max(1, sourceLine.Length - start);

        builder.AppendLine()
            .Append(gutter).Append(" |")
            .AppendLine()
            .Append(number).Append(" | ").Append(sourceLine)
            .AppendLine()
            .Append(gutter).Append(" | ")
            .Append(' ', start)
            .Append('^', width);
    }
}
