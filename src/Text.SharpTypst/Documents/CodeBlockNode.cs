namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class CodeBlockNode(string language, string code) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim("```" + language);
        writer.NewLine();
        writer.WriteVerbatim(code.TrimEnd('\n'));
        writer.NewLine();
        writer.WriteVerbatim("```");
        writer.NewLine();
    }
}