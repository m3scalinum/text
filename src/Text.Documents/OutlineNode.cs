namespace M3scalinum.Text.Documents;

internal sealed class OutlineNode(TypstContent? title, int depth) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim("#outline(");

        if (title is not null)
        {
            writer.WriteVerbatim("title: ");
            using (writer.ContentBlock())
            {
                title.Write(writer);
            }

            writer.WriteVerbatim(", ");
        }

        writer.WriteVerbatim($"depth: {depth})");
        writer.NewLine();
    }
}