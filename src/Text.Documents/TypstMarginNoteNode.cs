using System.Text;

namespace M3scalinum.Text.Documents;

/// <summary>
/// Emits a margin note: an inline zero-size box at the marker, holding a placed note, then the
/// marker itself.
/// </summary>
/// <remarks>
/// <c>#box(place(start, dx: …)[…])</c> anchors the placement to a zero-size box on the marker's line,
/// which is the only way in Typst 0.15.1 to keep a note level with its marker: a top-level
/// <c>#place</c> would break the paragraph, and <c>place(float: true)</c> only stacks at the top or
/// bottom of a page.
/// </remarks>
internal sealed class TypstMarginNoteNode(TypstMarginNote note) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        var call = new StringBuilder("#box(place(start, dx: ");
        call.Append(note.Offset).Append(')');

        writer.BlankLine();
        writer.WriteVerbatim(call.ToString());

        using (writer.ContentBlock())
        {
            if (note.Width is { } width)
            {
                writer.WriteVerbatim("#block(width: " + width + ")");

                using (writer.ContentBlock())
                {
                    note.Content.Write(writer);
                }
            }
            else
            {
                note.Content.Write(writer);
            }
        }

        writer.WriteVerbatim(")");
        writer.WriteVerbatim("#super");

        using (writer.ContentBlock())
        {
            writer.WriteText(note.Marker);
        }

        writer.NewLine();
    }
}
