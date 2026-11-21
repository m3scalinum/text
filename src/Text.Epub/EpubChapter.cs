using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Epub;

/// <summary>
/// One chapter of a book: a title for the navigation document, and the HTML that becomes its XHTML
/// content document.
/// </summary>
/// <remarks>
/// <see cref="Html"/> is a fragment — the part that belongs inside <c>&lt;body&gt;</c> — not a whole
/// document; the writer supplies the <c>html</c>, <c>head</c>, namespace declarations and the link to
/// the book's stylesheet. A complete document is accepted too: its <c>head</c> is dropped and its
/// body is taken as the fragment. The markup is parsed and re-serialized as XML, so unclosed tags,
/// <c>&lt;br&gt;</c> and named entities are repaired rather than refused.
/// </remarks>
public sealed class EpubChapter
{
    /// <summary>Creates a chapter.</summary>
    /// <param name="title">The label the navigation document shows.</param>
    /// <param name="html">The body content of the chapter, as HTML.</param>
    public EpubChapter(string title, string html)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(html);

        if (title.Trim().Length == 0)
        {
            throw new ArgumentException("A chapter needs a title: the navigation document has nothing else to show.", nameof(title));
        }

        Title = title;
        Html = html;
    }

    /// <summary>Creates a chapter that is already parsed, which the splitter produces.</summary>
    internal EpubChapter(string title, IReadOnlyList<Internal.HtmlNode> nodes)
    {
        Title = title;
        Html = string.Empty;
        Nodes = nodes;
    }

    /// <summary>The label the navigation document shows.</summary>
    public string Title { get; }

    /// <summary>The body content of the chapter, as HTML.</summary>
    public string Html { get; }

    /// <summary>The body content already parsed, when the chapter came from <see cref="EpubBook.FromHtml"/>.</summary>
    internal IReadOnlyList<Internal.HtmlNode>? Nodes { get; }
}

/// <summary>One non-text file of the book — an image, a font, an audio clip — stored as it is.</summary>
/// <remarks>
/// <see cref="Name"/> is the path inside the container, relative to the package document's directory,
/// and it must be a plain relative path: no <c>..</c>, no leading slash, no backslash, no scheme.
/// The name a chapter references in an <c>img src</c> is exactly this value, which is what lets the
/// writer check that every reference resolves.
/// </remarks>
public sealed class EpubAsset
{
    /// <summary>Creates an asset with an explicit media type.</summary>
    /// <param name="name">The path inside the container, for example <c>images/cover.png</c>.</param>
    /// <param name="mediaType">The media type an <c>item</c> in the package manifest declares.</param>
    /// <param name="content">The bytes to store.</param>
    public EpubAsset(string name, string mediaType, ReadOnlyMemory<byte> content)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(mediaType);

        if (mediaType.Trim().Length == 0)
        {
            throw new ArgumentException("An asset needs a media type: the manifest has no default.", nameof(mediaType));
        }

        Name = name;
        MediaType = mediaType;
        Content = content;
    }

    /// <summary>Creates an asset, taking the media type from the file extension.</summary>
    /// <param name="name">The path inside the container, for example <c>images/cover.png</c>.</param>
    /// <param name="content">The bytes to store.</param>
    /// <exception cref="EpubValidationException">The extension is not one with a known media type.</exception>
    public EpubAsset(string name, ReadOnlyMemory<byte> content)
        : this(name, EpubMediaTypes.FromName(name), content)
    {
    }

    /// <summary>The path inside the container, relative to the package document.</summary>
    public string Name { get; }

    /// <summary>The media type the manifest declares.</summary>
    public string MediaType { get; }

    /// <summary>The bytes stored under <see cref="Name"/>.</summary>
    public ReadOnlyMemory<byte> Content { get; }
}
