using System.Text.Json.Serialization;

namespace M3scalinum.Text.Docx.Internal;

/// <summary>
/// The JSON contract between the native reader and this package, generated at build time.
/// </summary>
/// <remarks>
/// Deserialisation only: the native reader produces the AST and the error payload, and nothing here
/// ever writes one back out. The generator is asked for type metadata rather than the default
/// fast-path serializers, so the assembly carries no writer method for a DTO that is only ever read.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DocxAstDto))]
[JsonSerializable(typeof(DocxErrorPayloadDto))]
[JsonSerializable(typeof(DocxBlockDto))]
[JsonSerializable(typeof(DocxTableRowDto))]
[JsonSerializable(typeof(DocxTableCellDto))]
internal sealed partial class DocxJsonContext : JsonSerializerContext;
