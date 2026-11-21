using System;

namespace M3scalinum.Text.Documents;

/// <summary>Options for <see cref="MarkdownImporter"/> and <see cref="TypstDocument.FromMarkdown"/>.</summary>
public sealed class TypstMarkdownImportOptions
{
    /// <summary>
    /// Resolves an image destination to the name of an asset registered with the world builder.
    /// </summary>
    /// <remarks>
    /// The importer never fetches a network resource and never reads a file. When this resolver is
    /// null, or it returns null or white space for a destination, the import fails with
    /// <see cref="TypstMarkdownImportException"/> rather than emitting an image that would not
    /// resolve at compile time.
    /// </remarks>
    public Func<string, string?>? AssetResolver { get; set; }

    /// <summary>
    /// What to do with raw HTML — an HTML block, or an inline tag inside a paragraph. Defaults to
    /// <see cref="TypstMarkdownRawHtmlPolicy.Drop"/>, which drops it and counts it.
    /// </summary>
    /// <remarks>
    /// Raw HTML is never passed through to Typst source: the model has no HTML node, and letting
    /// the text through would be a markup-injection hole.
    /// </remarks>
    public TypstMarkdownRawHtmlPolicy RawHtml { get; set; } = TypstMarkdownRawHtmlPolicy.Drop;
}

/// <summary>What the Markdown importer does with raw HTML.</summary>
public enum TypstMarkdownRawHtmlPolicy
{
    /// <summary>Drop the block or tag, count it, and keep the surrounding text.</summary>
    Drop = 0,

    /// <summary>Stop at the first raw HTML block or tag with a <see cref="TypstMarkdownImportException"/>.</summary>
    Throw = 1,
}
