namespace M3scalinum.Text.Docx;

/// <summary>The page Word laid the document out on, in points.</summary>
/// <remarks>
/// Present only when the document states its own page setup. A document that leaves Word's defaults
/// in place — A4, 2.54 cm top and bottom, 3 cm left and right — reads as
/// <see langword="null"/>, because the package then carries nothing a host could call a choice.
/// </remarks>
public sealed class DocxPageSetup
{
    internal DocxPageSetup(
        double widthPoints,
        double heightPoints,
        bool landscape,
        double marginTopPoints,
        double marginRightPoints,
        double marginBottomPoints,
        double marginLeftPoints)
    {
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        Landscape = landscape;
        MarginTopPoints = marginTopPoints;
        MarginRightPoints = marginRightPoints;
        MarginBottomPoints = marginBottomPoints;
        MarginLeftPoints = marginLeftPoints;
    }

    /// <summary>The page width.</summary>
    public double WidthPoints { get; }

    /// <summary>The page height.</summary>
    public double HeightPoints { get; }

    /// <summary>
    /// Whether the section is landscape. Word swaps width and height for a landscape section, so
    /// this is a property of the section, not something <see cref="WidthPoints"/> already says.
    /// </summary>
    public bool Landscape { get; }

    /// <summary>The top margin.</summary>
    public double MarginTopPoints { get; }

    /// <summary>The right margin.</summary>
    public double MarginRightPoints { get; }

    /// <summary>The bottom margin.</summary>
    public double MarginBottomPoints { get; }

    /// <summary>The left margin.</summary>
    public double MarginLeftPoints { get; }
}
