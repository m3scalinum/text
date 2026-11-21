namespace M3scalinum.Text.Documents;

internal sealed class BibliographyNode(string path, string? style) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim("#bibliography(" + LinkNode.Quote(path));

        if (style is not null)
        {
            writer.WriteVerbatim(", style: " + LinkNode.Quote(style));
        }

        writer.WriteVerbatim(")");
        writer.NewLine();
    }
}