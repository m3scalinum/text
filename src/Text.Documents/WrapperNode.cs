namespace M3scalinum.Text.Documents;

/// <summary>A node that wraps content in a Typst function call: <c>#name(…)[…]</c>.</summary>
internal sealed class WrapperNode(string function, TypstContent content, string? arguments = null) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim("#" + function);
        if (arguments is not null)
        {
            writer.WriteVerbatim("(" + arguments + ")");
        }

        using (writer.ContentBlock())
        {
            content.Write(writer);
        }
    }
}