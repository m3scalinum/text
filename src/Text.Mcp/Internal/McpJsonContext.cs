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
internal sealed partial class McpJsonContext : JsonSerializerContext;

/// <summary>The <c>initialize</c> result.</summary>
internal sealed class InitializeResultDto
{
    public string ProtocolVersion { get; init; } = string.Empty;

    public ServerCapabilitiesDto Capabilities { get; init; } = new();

    public ImplementationDto ServerInfo { get; init; } = new();
}

/// <summary>The capabilities the server declares. Tools only: it serves no resources or prompts.</summary>
internal sealed class ServerCapabilitiesDto
{
    public ToolCapabilitiesDto Tools { get; init; } = new();
}

/// <summary>Tool capability flags; the list never changes while the server runs.</summary>
internal sealed class ToolCapabilitiesDto
{
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
