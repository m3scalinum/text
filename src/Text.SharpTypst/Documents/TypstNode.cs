namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>A node of the document model. Nodes emit Typst source; they never parse it.</summary>
public abstract class TypstNode
{
    internal abstract void Write(TypstMarkupWriter writer);
}