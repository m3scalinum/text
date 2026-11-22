using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M3scalinum.Text.Mcp.Internal;

/// <summary>
/// The wire shapes of the protocol, serialized by the source generator.
/// </summary>
/// <remarks>
/// <para>
/// Every type exchanged with a client is registered here: the repository forbids reflection-based
/// JSON, and <c>IsAotCompatible</c> makes a type that falls back to it a build error. The
/// JSON-RPC envelope itself is written by hand — a response either carries <c>result</c> or
/// <c>error</c>, and a polymorphic <c>object Result</c> is exactly what a source-generated context
/// cannot serialize — so the types below are the payloads, not the envelope.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InitializeResultDto))]
[JsonSerializable(typeof(EmptyResultDto))]
[JsonSerializable(typeof(ToolsListResultDto))]
[JsonSerializable(typeof(ToolCallResultDto))]
[JsonSerializable(typeof(TemplateListDto))]
[JsonSerializable(typeof(RenderSummaryDto))]
[JsonSerializable(typeof(ExtractedTextDto))]
[JsonSerializable(typeof(ResourcesListResultDto))]
[JsonSerializable(typeof(ResourceReadResultDto))]
[JsonSerializable(typeof(TemplateManifestDto))]
[JsonSerializable(typeof(CallJournalEntryDto))]
internal sealed partial class McpJsonContext : JsonSerializerContext;

/// <summary>The <c>initialize</c> result.</summary>
internal sealed class InitializeResultDto
{
    public string ProtocolVersion { get; init; } = string.Empty;

    public ServerCapabilitiesDto Capabilities { get; init; } = new();

    public ImplementationDto ServerInfo { get; init; } = new();
}

/// <summary>The capabilities the server declares: tools and resources. It serves no prompts.</summary>
internal sealed class ServerCapabilitiesDto
{
    public ToolCapabilitiesDto Tools { get; init; } = new();

    public ResourceCapabilitiesDto Resources { get; init; } = new();
}

/// <summary>Tool capability flags; the list never changes while the server runs.</summary>
internal sealed class ToolCapabilitiesDto
{
    public bool ListChanged { get; init; }
}

/// <summary>
/// Resource capability flags: read-only templates, so the server accepts no subscription and the
/// list never changes.
/// </summary>
internal sealed class ResourceCapabilitiesDto
{
    public bool Subscribe { get; init; }

    public bool ListChanged { get; init; }
}

/// <summary>The name and version a client displays for the server.</summary>
internal sealed class ImplementationDto
{
    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;
}

/// <summary>The empty result of a method that returns nothing but success, such as <c>ping</c>.</summary>
internal sealed class EmptyResultDto;

/// <summary>The <c>tools/list</c> result.</summary>
internal sealed class ToolsListResultDto
{
    public IReadOnlyList<ToolDefinitionDto> Tools { get; init; } = [];
}

/// <summary>One tool of the list: its name, its prose, and the JSON Schema of its arguments.</summary>
internal sealed class ToolDefinitionDto
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public JsonElement InputSchema { get; init; }
}

/// <summary>The <c>tools/call</c> result.</summary>
internal sealed class ToolCallResultDto
{
    public IReadOnlyList<ContentBlockDto> Content { get; init; } = [];

    public bool? IsError { get; init; }
}

/// <summary>One block of a tool result: text, or an embedded binary resource.</summary>
internal sealed class ContentBlockDto
{
    public string Type { get; init; } = "text";

    public string? Text { get; init; }

    public EmbeddedResourceDto? Resource { get; init; }
}

/// <summary>
/// A binary payload a client reads without a URL: the PDF travels base64-encoded here.
/// </summary>
internal sealed class EmbeddedResourceDto
{
    public string Uri { get; init; } = string.Empty;

    public string MimeType { get; init; } = "application/pdf";

    public string Blob { get; init; } = string.Empty;
}

/// <summary>What the server holds, as <c>list_templates</c> answers it.</summary>
internal sealed class TemplateListDto
{
    public IReadOnlyList<TemplateEntryDto> Templates { get; init; } = [];
}

/// <summary>One layout: identity, description, languages and the parameters it accepts.</summary>
internal sealed class TemplateEntryDto
{
    public string Id { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string? Description { get; init; }

    public IReadOnlyList<string> Languages { get; init; } = [];

    public IReadOnlyList<TemplateParameterDto> Parameters { get; init; } = [];
}

/// <summary>One declared parameter of a layout, so a client can build the call without guessing.</summary>
internal sealed class TemplateParameterDto
{
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public bool Required { get; init; }
}

/// <summary>The text block that describes the payload returned beside it.</summary>
internal sealed class RenderSummaryDto
{
    public int ByteLength { get; init; }

    public int? PageCount { get; init; }

    public string? Path { get; init; }
}

/// <summary>What <c>extract_text</c> answers: the text of each page, in page order.</summary>
internal sealed class ExtractedTextDto
{
    /// <summary>How many pages the document has.</summary>
    public int PageCount { get; init; }

    /// <summary>The text each page draws; <see cref="ExtractedPageDto.Page"/> counts from one.</summary>
    public IReadOnlyList<ExtractedPageDto> Pages { get; init; } = [];
}

/// <summary>The text of one page.</summary>
internal sealed class ExtractedPageDto
{
    /// <summary>The page number, counted from one as a reader numbers it.</summary>
    public int Page { get; init; }

    /// <summary>The text the page draws, in reading order as far as positioning allows.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>The <c>resources/list</c> result: one resource per layout of the pack.</summary>
internal sealed class ResourcesListResultDto
{
    public IReadOnlyList<ResourceDefinitionDto> Resources { get; init; } = [];
}

/// <summary>
/// One resource as <c>resources/list</c> announces it: the URI a client reads, the layout it names,
/// and the media type of its contents.
/// </summary>
internal sealed class ResourceDefinitionDto
{
    public string Uri { get; init; } = string.Empty;

    /// <summary>A display name a client shows its user, which is the layout id.</summary>
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string MimeType { get; init; } = "application/json";
}

/// <summary>The <c>resources/read</c> result.</summary>
internal sealed class ResourceReadResultDto
{
    public IReadOnlyList<ResourceContentsDto> Contents { get; init; } = [];
}

/// <summary>The contents of one resource: its URI, its media type and its text.</summary>
internal sealed class ResourceContentsDto
{
    public string Uri { get; init; } = string.Empty;

    public string MimeType { get; init; } = "application/json";

    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// The manifest of one layout, as <c>resources/read</c> answers it: what the layout declares, never
/// an instance of a document.
/// </summary>
internal sealed class TemplateManifestDto
{
    /// <summary>The catalog id, which is also the last segment of the resource URI.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The name the manifest declares.</summary>
    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    /// <summary>The entry file, relative to the template root.</summary>
    public string Entry { get; init; } = string.Empty;

    public string? Description { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public IReadOnlyList<string> Languages { get; init; } = [];

    public IReadOnlyList<TemplateParameterDto> Parameters { get; init; } = [];

    public bool AllowExtraParameters { get; init; }

    /// <summary>The oldest Typst version the layout declares support for, when it declares one.</summary>
    public string? MinTypst { get; init; }
}

/// <summary>
/// One line of the call journal: who called what, how much went in and out, and how it ended.
/// </summary>
/// <remarks>
/// Sizes are bytes of JSON, never document text: a journal a host ships to a log collector must not
/// become a second copy of the documents that passed through the server.
/// </remarks>
internal sealed class CallJournalEntryDto
{
    /// <summary>When the answer was written, in UTC.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The transport's session id, or <see langword="null"/> when it has none.</summary>
    public string? Session { get; init; }

    /// <summary>The tool the call named, as it appeared in <c>tools/call</c>.</summary>
    public string Tool { get; init; } = string.Empty;

    /// <summary>UTF-8 bytes of the call's <c>arguments</c>, or zero when it carried none.</summary>
    public int InputBytes { get; init; }

    /// <summary>UTF-8 bytes of the result the client received, or zero when it received an error.</summary>
    public int OutputBytes { get; init; }

    /// <summary>How long the call took, in whole milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary><c>ok</c>, <c>tool_error</c>, <c>invalid_params</c> or <c>quota_exceeded</c>.</summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>The JSON-RPC error code, when the call ended in one.</summary>
    public int? ErrorCode { get; init; }
}
