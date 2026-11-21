namespace M3scalinum.Text.Documents;

internal sealed class CiteNode(string key, bool prose) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        if (prose)
        {
            writer.WriteVerbatim("#cite(<" + key + ">, form: \"prose\")");
        }
        else
        {
            writer.WriteVerbatim("@" + key);
        }
    }
}