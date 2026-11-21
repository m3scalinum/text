using System;

namespace M3scalinum.Text.Documents;

/// <summary>Options for <see cref="TypstHtmlImporter"/> and <see cref="TypstDocument.FromHtml"/>.</summary>
public sealed class TypstHtmlImportOptions
{
    /// <summary>
    /// Resolves an <c>img</c> source to the name of an asset registered with the world builder.
    /// </summary>
    /// <remarks>
    /// The importer never fetches a network resource and never reads a file. When this resolver is
    /// null, or it returns null or white space for a source, the import fails with
    /// <see cref="TypstHtmlImportException"/> rather than emitting an image that would not
    /// resolve at compile time.
    /// </remarks>
    public Func<string, string?>? AssetResolver { get; set; }

    /// <summary>
    /// What to do with an element outside the supported subset. Defaults to
    /// <see cref="TypstHtmlUnknownElementPolicy.Skip"/>, which unwraps and counts it.
    /// </summary>
    public TypstHtmlUnknownElementPolicy UnknownElements { get; set; } = TypstHtmlUnknownElementPolicy.Skip;

    /// <summary>
    /// Reads the <c>style</c> attribute of inline elements. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// When enabled, only three declarations are honoured: <c>font-weight: bold</c> (or a weight of
    /// 600 and above), <c>font-style: italic</c> and <c>text-decoration</c> containing
    /// <c>underline</c> or <c>line-through</c>. Every other declaration — colour, size, spacing,
    /// display, margin, width — is ignored: this is not a CSS engine and does not do layout. A
    /// document that needs CSS fidelity must be rendered by a browser, not imported here.
    /// </remarks>
    public bool HonourInlineStyles { get; set; }
}
