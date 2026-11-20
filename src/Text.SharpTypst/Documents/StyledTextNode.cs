namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>Emits <c>#text(…)[escaped text]</c>.</summary>
internal sealed class StyledTextNode(string text, TypstTextStyle style) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim($"#text({style.Arguments})");

        using (writer.ContentBlock())
        {
            writer.WriteText(text);
        }
    }
}