using System.Text.Json.Serialization;

namespace M3scalinum.Text.Docx.Internal;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DocxAstDto))]
[JsonSerializable(typeof(DocxErrorPayloadDto))]
[JsonSerializable(typeof(DocxBlockDto))]
[JsonSerializable(typeof(DocxTableRowDto))]
[JsonSerializable(typeof(DocxTableCellDto))]
internal sealed partial class DocxJsonContext : JsonSerializerContext;
