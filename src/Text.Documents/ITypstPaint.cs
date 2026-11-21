namespace M3scalinum.Text.Documents;

/// <summary>Something Typst accepts as a paint: a colour, a gradient or a tiling.</summary>
public interface ITypstPaint
{
    /// <summary>Emits the Typst expression for this paint.</summary>
    string ToTypst();
}