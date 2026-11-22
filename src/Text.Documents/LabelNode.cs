namespace M3scalinum.Text.Documents;

/// <summary>A label attached to the element that precedes it.</summary>
/// <remarks>
/// Labels are emitted as <c>&lt;name&gt;</c> on the line after the element; Typst attaches a label to
/// the last labellable element of its markup sequence, and the whitespace between the two does not
/// break that attachment. A repeated name is indexed by the writer so the document stays compilable
/// — see <see cref="TypstLabelRegistry"/>.
/// </remarks>
internal sealed class LabelNode(string name) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
        => writer.WriteVerbatim("<" + writer.Labels.Index(name) + ">");
}
