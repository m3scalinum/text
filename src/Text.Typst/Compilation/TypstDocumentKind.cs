namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// Which layout pass the compiler runs. PDF, SVG and PNG need <see cref="Paged"/>;
/// HTML, Markdown and text need <see cref="Html"/>.
/// </summary>
public enum TypstDocumentKind
{
    /// <summary>Content laid out onto pages.</summary>
    Paged,

    /// <summary>Content laid out as a semantic HTML tree.</summary>
    Html,

    /// <summary>The HTML tree plus the files declared with <c>#asset</c>.</summary>
    Bundle,
}
