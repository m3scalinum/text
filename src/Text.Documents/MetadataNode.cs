namespace M3scalinum.Text.Documents;

internal sealed class MetadataNode(string key, string value, string? label) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim("#metadata((" + key + ": " + LinkNode.Quote(value) + "))");

        if (label is not null)
        {
            writer.WriteVerbatim(" <" + label + ">");
        }
    }
}