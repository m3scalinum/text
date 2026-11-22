namespace M3scalinum.Text.Documents;

internal sealed class FigureNode(TypstFigure figure) : TypstNode
{
    /// <summary>The figure this node emits, so a label added later can land on it.</summary>
    internal TypstFigure Target => figure;

    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim("#figure(");

        using (writer.Indent())
        {
            writer.NewLine();
            writer.WriteVerbatim(figure.ImageMarkup);

            if (figure.AltText is { } alt)
            {
                writer.WriteVerbatim(",");
                writer.NewLine();
                writer.WriteVerbatim("alt: " + LinkNode.Quote(alt));
            }

            if (figure.Caption is not null)
            {
                writer.WriteVerbatim(",");
                writer.NewLine();
                writer.WriteVerbatim("caption: ");
                using (writer.ContentBlock())
                {
                    figure.Caption.Write(writer);
                }
            }

            if (figure.LabelName is not null)
            {
                // Pinned so `#ref(<label>)` prints "Figure 3" whatever the document language is.
                // Without a label there is nothing to reference and the arguments stay implicit,
                // which is the markup every document emitted before labels were numbered.
                writer.WriteVerbatim(",");
                writer.NewLine();
                writer.WriteVerbatim("kind: image,");
                writer.NewLine();
                writer.WriteVerbatim("supplement: [Figure],");
            }
        }

        writer.NewLine();
        writer.WriteVerbatim(")");

        if (figure.LabelName is { } label)
        {
            writer.WriteVerbatim(" <" + writer.Labels.Index(label) + ">");
        }

        writer.NewLine();
    }
}