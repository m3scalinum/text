namespace M3scalinum.Text.Documents;

internal sealed class FigureNode(TypstFigure figure) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        writer.BlankLine();
        writer.WriteVerbatim("#figure(");

        using (writer.Indent())
        {
            writer.NewLine();
            writer.WriteVerbatim(figure.ImageMarkup);

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
        }

        writer.NewLine();
        writer.WriteVerbatim(")");

        if (figure.LabelName is not null)
        {
            writer.WriteVerbatim(" <" + figure.LabelName + ">");
        }

        writer.NewLine();
    }
}