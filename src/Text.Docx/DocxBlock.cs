namespace M3scalinum.Text.Docx;

/// <summary>One block of a Word document's body: a paragraph or a table.</summary>
/// <remarks>
/// The hierarchy is closed: every type derives from this one and none is added by a host, so a
/// <see langword="switch"/> over the two shapes is exhaustive.
/// </remarks>
public abstract class DocxBlock
{
    private protected DocxBlock()
    {
    }
}
