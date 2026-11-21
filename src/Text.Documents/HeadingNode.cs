namespace M3scalinum.Text.Documents;

internal sealed class HeadingNode(int level, TypstContent content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim(new string('=', level) + " ");
        content.Write(writer);
        writer.NewLine();
    }
}