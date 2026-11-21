using System;

namespace M3scalinum.Text.SharpTypst.Documents;

/// <summary>Raised when an HTML document cannot be imported.</summary>
/// <remarks>
/// The importer raises this for an <c>img</c> whose source does not resolve to a registered asset,
/// and for an unsupported element when <see cref="TypstHtmlImportOptions.UnknownElements"/> is
/// <see cref="TypstHtmlUnknownElementPolicy.Throw"/>. Everything the subset covers is imported
/// silently.
/// </remarks>
public sealed class TypstHtmlImportException : Exception
{
    /// <summary>Creates an exception without a message.</summary>
    public TypstHtmlImportException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public TypstHtmlImportException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public TypstHtmlImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The tag name that caused the failure, when the failure is about one element.</summary>
    public string? ElementName { get; init; }
}
