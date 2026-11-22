namespace M3scalinum.Text.Documents;

/// <summary>A reference to the label <paramref name="label"/>.</summary>
/// <remarks>
/// <para>
/// The model emits the reference and nothing else: Typst resolves it against the laid-out document,
/// which is the only place the number and the page exist. A reference may name a label that appears
/// later in the document, and one that never appears at all — the latter is a compile error Typst
/// reports (<c>label `x` does not exist in the document</c>), not something this node can detect.
/// </para>
/// <para>
/// The reference needs the target to be numbered. A figure is numbered by default; a heading is
/// only numbered once the document sets <c>#set heading(numbering: …)</c>, which
/// <see cref="TypstDocument.Label(TypstLabelKind, string?)"/> does when it labels a section.
/// </para>
/// </remarks>
internal sealed class ReferenceNode(string label, string? supplement = null) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.WriteVerbatim("#ref(<" + label + ">");

        if (supplement is not null)
        {
            writer.WriteVerbatim(", supplement: ");
            using (writer.ContentBlock())
            {
                writer.WriteText(supplement);
            }
        }

        writer.WriteVerbatim(")");
    }
}
