using System;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>Raised when a Markdown document cannot be imported.</summary>
/// <remarks>
/// The importer raises this for an image whose destination does not resolve to a registered
/// asset, and for raw HTML when <see cref="TypstMarkdownImportOptions.RawHtml"/> is
/// <see cref="TypstMarkdownRawHtmlPolicy.Throw"/>. Everything the subset covers is imported
/// silently; nothing is ever guessed or fetched.
/// </remarks>
public sealed class TypstMarkdownImportException : Exception
{
    /// <summary>Creates an exception without a message.</summary>
    public TypstMarkdownImportException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public TypstMarkdownImportException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public TypstMarkdownImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The construct that caused the failure, when the failure is about one construct.</summary>
    public string? ConstructName { get; init; }
}
