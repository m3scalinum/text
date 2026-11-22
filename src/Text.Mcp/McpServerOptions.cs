using System;
using System.Collections.Generic;
using System.IO;
using M3scalinum.Text.Templates;

namespace M3scalinum.Text.Mcp;

/// <summary>
/// The limits and permissions an <see cref="McpServer"/> applies to every call.
/// </summary>
/// <remarks>
/// A tool call is written by a model, not by a programmer: the server treats it as hostile input.
/// Every value here defaults to the restrictive choice, so a host that configures nothing gets PDF
/// bytes back on stdout and nothing else — no file written, no template outside the pack, no
/// unbounded document.
/// </remarks>
public sealed class McpServerOptions
{
    /// <summary>The default ceiling on one document: 1 MiB of Typst source or invoice JSON.</summary>
    public const int DefaultMaxDocumentSize = 1024 * 1024;

    /// <summary>The default ceiling on one JSON-RPC message, framing included: 4 MiB.</summary>
    public const int DefaultMaxRequestSize = 4 * 1024 * 1024;

    /// <summary>
    /// The name reported in <c>serverInfo</c> during <c>initialize</c>.
    /// </summary>
    public string ServerName { get; init; } = "M3scalinum.Text.Mcp";

    /// <summary>
    /// The version reported in <c>serverInfo</c>; <see langword="null"/> reports the assembly
    /// version of this package.
    /// </summary>
    public string? ServerVersion { get; init; }

    /// <summary>
    /// The largest Typst source, or invoice JSON, one call may carry.
    /// </summary>
    /// <remarks>
    /// Counted in UTF-8 bytes of the document itself, not of the JSON-RPC message that wraps it.
    /// A call above the ceiling is refused before anything is compiled.
    /// </remarks>
    public int MaxDocumentSize { get; init; } = DefaultMaxDocumentSize;

    /// <summary>
    /// The largest JSON-RPC message the server will read, framing included.
    /// </summary>
    /// <remarks>
    /// A line longer than this is discarded, answered with a parse error, and the server carries
    /// on reading: an oversized message must not be able to end the session.
    /// </remarks>
    public int MaxRequestSize { get; init; } = DefaultMaxRequestSize;

    /// <summary>
    /// The template ids <c>render_document</c> may render when no source is given.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> allows every layout of <c>M3scalinum.Text.Templates</c>; an empty
    /// collection allows none, so a host can ship the invoice layout alone. An id the pack does not
    /// carry is a configuration error and throws when the server is built.
    /// </remarks>
    public IReadOnlyCollection<string>? AllowedTemplateIds { get; init; }

    /// <summary>
    /// Whether a call may name a path to write its PDF to, instead of receiving the bytes.
    /// </summary>
    /// <remarks>
    /// Default <see langword="false"/>: <em>no ambient file access</em>. With the default, the
    /// <c>outputPath</c> argument is refused with a JSON-RPC error — it is not silently ignored,
    /// because a caller that asked for a file and got bytes would believe the file exists.
    /// </remarks>
    public bool AllowFileOutput { get; init; }

    /// <summary>
    /// How long one compilation may take before the call fails.
    /// </summary>
    /// <remarks>
    /// A Typst document can ask the compiler for an unbounded amount of work. The clock covers the
    /// wait for a compiler slot as well as the compilation, so a queue cannot extend it.
    /// </remarks>
    public TimeSpan RenderTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many document calls one client may make in a sliding minute;
    /// <see langword="null"/> applies no ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <em>render</em> is a call that reads, compiles or rewrites a document — <c>render_document</c>,
    /// <c>sign_document</c>, <c>merge_documents</c> and <c>extract_text</c>. <c>list_templates</c> is
    /// metadata and is never counted. A call is counted when it is admitted, before its arguments are
    /// checked: the ceiling bounds attempts, so a caller cannot buy unlimited work with malformed
    /// calls.
    /// </para>
    /// <para>
    /// The client is the transport's session — the <c>Mcp-Session-Id</c> of the HTTP transport, and one
    /// bucket for a stdio process. The window is in memory: it survives a request, not a process, so a
    /// restart forgets it.
    /// </para>
    /// </remarks>
    public int? MaxRendersPerMinute { get; init; }

    /// <summary>
    /// Where the per-call journal is written, or <see langword="null"/> (the default) to keep none.
    /// </summary>
    /// <remarks>
    /// One JSON object per line, written after the answer is produced: the tool, the session, the
    /// sizes of the arguments and of the result in bytes, the duration, and the outcome. It carries
    /// no document text — only identifiers and sizes — so a host can reconstruct who rendered what
    /// without the journal becoming a copy of the documents. A writer that throws does not fail the
    /// call it was journaling.
    /// </remarks>
    public TextWriter? CallJournal { get; init; }

    /// <summary>
    /// Rejects a limit that cannot work, and a template id the pack does not carry.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A limit is not positive, the message ceiling is smaller than the document ceiling, or an
    /// allowed template id is blank or unknown to the pack.
    /// </exception>
    internal void Validate(TemplateCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (string.IsNullOrWhiteSpace(ServerName))
        {
            throw new ArgumentException("ServerName must not be blank");
        }

        if (string.IsNullOrWhiteSpace(ServerVersion) && ServerVersion is not null)
        {
            throw new ArgumentException("ServerVersion must be null or non-blank");
        }

        if (MaxDocumentSize <= 0)
        {
            throw new ArgumentException("MaxDocumentSize must be positive");
        }

        if (MaxRequestSize < MaxDocumentSize)
        {
            throw new ArgumentException(
                "MaxRequestSize must be at least MaxDocumentSize, or a maximal document cannot arrive");
        }

        if (RenderTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("RenderTimeout must be positive");
        }

        if (MaxRendersPerMinute is <= 0)
        {
            throw new ArgumentException(
                "MaxRendersPerMinute must be null or positive: a ceiling of zero serves nothing");
        }

        if (AllowedTemplateIds is null)
        {
            return;
        }

        foreach (var id in AllowedTemplateIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("AllowedTemplateIds must not carry a blank id");
            }

            if (!catalog.TryGet(id, null, out _))
            {
                throw new ArgumentException(
                    $"AllowedTemplateIds names '{id}', which the template pack does not carry");
            }
        }
    }
}
