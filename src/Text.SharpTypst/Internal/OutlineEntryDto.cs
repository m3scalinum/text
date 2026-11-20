namespace M3scalinum.Text.SharpTypst.Internal;

internal sealed class OutlineEntryDto
{
    public int Level { get; set; }

    public string? Title { get; set; }

    public int Page { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public string? Label { get; set; }
}