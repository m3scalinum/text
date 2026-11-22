namespace M3scalinum.Text.Docx;

/// <summary>Why a document could not be read.</summary>
/// <remarks>
/// The values are the status codes the native reader returns, so a host can log the number it was
/// given without a mapping table. Codes 1 to 3 and 9 to 10 are the ones
/// <c>docs/specifications/02-native-abi.md</c> defines.
/// </remarks>
public enum DocxErrorCode
{
    /// <summary>The bytes are not a zip package, or one of its parts does not parse.</summary>
    MalformedPackage = 11,

    /// <summary>The package is a zip package but lacks a part a DOCX must carry.</summary>
    MissingPart = 12,

    /// <summary>The document is larger than <see cref="DocxOptions.MaxDocumentBytes"/>.</summary>
    Limit = 9,

    /// <summary>An argument was null, empty, or outside its range.</summary>
    InvalidArgument = 1,

    /// <summary>The native library reported a handle it does not own.</summary>
    InvalidHandle = 2,

    /// <summary>The native library caught a panic; the bug is in this package.</summary>
    Panic = 3,

    /// <summary>A part of the document is outside what this reader implements.</summary>
    Unsupported = 10,

    /// <summary>The native library failed in a way it does not classify.</summary>
    Internal = 13,

    /// <summary>A part declares a DTD, an external entity, or entities that expand past the reader's bound.</summary>
    XmlPolicy = 14,
}
