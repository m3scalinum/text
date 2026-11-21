namespace M3scalinum.Text.Docx;

/// <summary>Vertical position of a run's characters.</summary>
public enum DocxScript
{
    /// <summary>Raised, as Word's superscript is.</summary>
    Superscript = 0,

    /// <summary>Lowered, as Word's subscript is.</summary>
    Subscript = 1,
}

/// <summary>One piece of a paragraph: text, an image, or a line break.</summary>
/// <remarks>
/// As with <see cref="DocxBlock"/>, the hierarchy is closed so a switch over the three shapes is
/// exhaustive.
/// </remarks>
public abstract class DocxInline
{
    private protected DocxInline()
    {
    }
}

/// <summary>A run of characters and the character formatting Word recorded for them.</summary>
public sealed class DocxTextRun : DocxInline
{
    internal DocxTextRun(
        string text,
        bool bold,
        bool italic,
        string? underline,
        bool strike,
        DocxScript? script,
        string? color,
        double? sizePoints)
    {
        Text = text;
        Bold = bold;
        Italic = italic;
        Underline = underline;
        Strike = strike;
        Script = script;
        Color = color;
        SizePoints = sizePoints;
    }

    /// <summary>The characters.</summary>
    public string Text { get; }

    /// <summary>Whether the run is bold.</summary>
    public bool Bold { get; }

    /// <summary>Whether the run is italic.</summary>
    public bool Italic { get; }

    /// <summary>
    /// The underline style Word names, for example <c>single</c>, or <see langword="null"/> when the
    /// run is not underlined. The style is kept verbatim: this reader does not collapse the
    /// decorations Word distinguishes.
    /// </summary>
    public string? Underline { get; }

    /// <summary>Whether the run is struck through, single or double.</summary>
    public bool Strike { get; }

    /// <summary>Superscript or subscript, when the run is raised or lowered.</summary>
    public DocxScript? Script { get; }

    /// <summary>The colour as <c>RRGGBB</c>, or <see langword="null"/> when Word recorded none.</summary>
    public string? Color { get; }

    /// <summary>The font size in points, converted from Word's half-points.</summary>
    public double? SizePoints { get; }
}

/// <summary>An inline image, identified by the part it lives in.</summary>
public sealed class DocxImageRun : DocxInline
{
    internal DocxImageRun(string name, string mimeType)
    {
        Name = name;
        MimeType = mimeType;
    }

    /// <summary>
    /// The part name inside the package, with forward slashes, for example
    /// <c>word/media/image1.png</c>. The bytes are <see cref="DocxDocument.Images"/>'s entry with
    /// this key.
    /// </summary>
    public string Name { get; }

    /// <summary>The MIME type, declared from the part's extension.</summary>
    public string MimeType { get; }
}

/// <summary>A line break inside a paragraph, which Word writes as <c>w:br</c>.</summary>
public sealed class DocxLineBreak : DocxInline
{
    internal DocxLineBreak()
    {
    }
}
