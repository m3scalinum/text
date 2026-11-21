using System;
using System.Collections.Generic;
using M3scalinum.Text.Epub.Internal;

namespace M3scalinum.Text.Epub;

/// <summary>
/// A book ready to be written as an EPUB 3 container: the metadata, the chapters in reading order,
/// and the non-text files the chapters reference.
/// </summary>
/// <remarks>
/// The book is a plain list — nothing is written until <see cref="EpubWriter.Write(EpubBook)"/> is
/// called, and the checks that make a container valid (a resolvable reference, a name that stays
/// inside the package, a unique id) run there, so a caller can build the book in any order.
/// </remarks>
public sealed class EpubBook
{
    private readonly List<EpubChapter> _chapters = [];
    private readonly List<EpubAsset> _assets = [];
    private readonly HashSet<string> _assetNames = new(StringComparer.Ordinal);

    /// <summary>Creates an empty book with its metadata.</summary>
    /// <param name="metadata">The identifier, title and language the package declares.</param>
    public EpubBook(EpubMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        Metadata = metadata;
    }

    /// <summary>The identifier, title and language the package declares.</summary>
    public EpubMetadata Metadata { get; }

    /// <summary>The chapters, in reading order.</summary>
    public IReadOnlyList<EpubChapter> Chapters => _chapters;

    /// <summary>The non-text files the chapters may reference.</summary>
    public IReadOnlyList<EpubAsset> Assets => _assets;

    /// <summary>Appends a chapter.</summary>
    /// <param name="chapter">The chapter, whose title labels it in the navigation document.</param>
    /// <returns>This book, so calls can be chained.</returns>
    public EpubBook AddChapter(EpubChapter chapter)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        _chapters.Add(chapter);
        return this;
    }

    /// <summary>Appends a chapter built from a title and an HTML fragment.</summary>
    /// <param name="title">The label the navigation document shows.</param>
    /// <param name="html">The body content of the chapter, as HTML.</param>
    /// <returns>This book, so calls can be chained.</returns>
    public EpubBook AddChapter(string title, string html)
        => AddChapter(new EpubChapter(title, html));

    /// <summary>Adds a non-text file the chapters reference.</summary>
    /// <param name="asset">The file, whose name is its path inside the container.</param>
    /// <returns>This book, so calls can be chained.</returns>
    /// <exception cref="EpubValidationException">The name leaves the container, is reserved, or is already taken.</exception>
    public EpubBook AddAsset(EpubAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        EpubPath.ValidateAssetName(asset.Name);

        if (!_assetNames.Add(asset.Name))
        {
            throw new EpubValidationException("The asset name '" + asset.Name + "' is already taken in this book.");
        }

        _assets.Add(asset);
        return this;
    }

    /// <summary>Adds a non-text file, taking its media type from the extension.</summary>
    /// <param name="name">The path inside the container, for example <c>images/cover.png</c>.</param>
    /// <param name="content">The bytes to store.</param>
    /// <returns>This book, so calls can be chained.</returns>
    public EpubBook AddAsset(string name, ReadOnlyMemory<byte> content)
        => AddAsset(new EpubAsset(name, content));

    /// <summary>Adds a non-text file with an explicit media type.</summary>
    /// <param name="name">The path inside the container, for example <c>images/cover.png</c>.</param>
    /// <param name="mediaType">The media type an <c>item</c> in the package manifest declares.</param>
    /// <param name="content">The bytes to store.</param>
    /// <returns>This book, so calls can be chained.</returns>
    public EpubBook AddAsset(string name, string mediaType, ReadOnlyMemory<byte> content)
        => AddAsset(new EpubAsset(name, mediaType, content));

    /// <summary>
    /// Builds a book from one HTML document, splitting it into chapters at its headings.
    /// </summary>
    /// <remarks>
    /// The split level is the shallowest heading the document uses, and content before the first
    /// heading becomes a chapter under the book's title. The markup is parsed and re-serialized as
    /// XHTML, so unclosed tags and named entities are repaired rather than refused. Images and other
    /// files are not fetched: a chapter that references one needs that file added with
    /// <see cref="AddAsset(string, ReadOnlyMemory{byte})"/>, or the write fails, which is the point
    /// — an EPUB with a broken image is a package a reader cannot render.
    /// </remarks>
    /// <param name="html">The document, or the fragment, to split.</param>
    /// <param name="metadata">The metadata of the book being built.</param>
    /// <returns>A book whose chapters follow the document's headings.</returns>
    public static EpubBook FromHtml(string html, EpubMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(metadata);

        var body = HtmlParser.Body(HtmlParser.Parse(html));
        var parts = HtmlChapterSplitter.Split(body, metadata.Title);
        var book = new EpubBook(metadata);

        foreach (var (title, nodes) in parts)
        {
            book._chapters.Add(new EpubChapter(title, nodes));
        }

        return book;
    }
}
