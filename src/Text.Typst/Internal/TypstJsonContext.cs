using System.Text.Json.Serialization;

namespace M3scalinum.Text.Typst.Internal;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorldConfigDto))]
[JsonSerializable(typeof(CompileOptionsDto))]
[JsonSerializable(typeof(ExportOptionsDto))]
[JsonSerializable(typeof(BundleManifestDto))]
[JsonSerializable(typeof(DiagnosticPayloadDto))]
[JsonSerializable(typeof(EvaluationDto))]
[JsonSerializable(typeof(CompileMetricsDto))]
[JsonSerializable(typeof(PageFingerprintDto))]
[JsonSerializable(typeof(PdfFontInventoryDto))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class TypstJsonContext : JsonSerializerContext;