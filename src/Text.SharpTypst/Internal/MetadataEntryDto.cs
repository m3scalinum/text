using System.Text.Json;

namespace M3scalinum.Text.SharpTypst.Internal;

internal sealed class MetadataEntryDto
{
    public JsonElement Value { get; set; }

    public int Page { get; set; }
}