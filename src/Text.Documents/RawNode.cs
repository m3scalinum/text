namespace M3scalinum.Text.Documents;

/// <summary>Emits its string untouched. The caller is writing Typst now.</summary>
internal sealed class RawNode(string markup) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer) => writer.WriteVerbatim(markup);
}