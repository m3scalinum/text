using System.Text;

namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class LinkNode(string url, TypstContent? content) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim("#link(");
        writer.WriteVerbatim(Quote(url));
        writer.WriteVerbatim(")");

        using (writer.ContentBlock())
        {
            if (content is null)
            {
                writer.WriteText(url);
            }
            else
            {
                content.Write(writer);
            }
        }
    }

    internal static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        builder.Append('"');
        return builder.ToString();
    }
}