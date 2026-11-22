namespace M3scalinum.Text.Pdf;

/// <summary>
/// The stable codes <see cref="PdfAConverter"/> reports, one for each step it took and one for each
/// reason it stopped. A code never changes once published.
/// </summary>
/// <remarks>
/// A change and a refusal are different kinds of finding and are reported in different lists:
/// <see cref="PdfAConversionResult.Changes"/> says what the converter rewrote, and
/// <see cref="PdfAConversionResult.Refusals"/> says what it could not rewrite and therefore did not
/// pretend to. Nothing in this class is a conformance verdict — that comes from
/// <see cref="PdfConformance.Check"/> on the bytes produced.
/// </remarks>
public static class PdfAConversionCodes
{
    /// <summary>The bytes are not a PDF, so there is nothing to convert.</summary>
    public const string NotAPdf = "PDFA.NOT_A_PDF";

    /// <summary>The document is encrypted, and was refused before anything was written.</summary>
    public const string Encrypted = "PDFA.ENCRYPTED";

    /// <summary>The trailer names no catalog that resolves.</summary>
    public const string CatalogMissing = "PDFA.CATALOG_MISSING";

    /// <summary>The page tree holds no page.</summary>
    public const string NoPages = "PDFA.NO_PAGES";

    /// <summary>
    /// A page carries no structure element, and the target requires tagged content. One refusal is
    /// reported per page: a structure tree cannot be inferred from a page description.
    /// </summary>
    public const string NotTagged = "PDFA.NOT_TAGGED";

    /// <summary>A font the pages draw has no embedded font program, and one cannot be recovered.</summary>
    public const string FontNotEmbedded = "PDFA.FONT_NOT_EMBEDDED";

    /// <summary>A font has no <c>/ToUnicode</c> CMap and none can be derived from its encoding.</summary>
    public const string FontWithoutToUnicode = "PDFA.FONT_WITHOUT_TO_UNICODE";

    /// <summary>
    /// The file declares a PDF version above the one the target fixes, so constructs the later
    /// version allowed may still be present. Refused rather than downgraded.
    /// </summary>
    public const string VersionAboveTarget = "PDFA.VERSION_ABOVE_TARGET";

    /// <summary>The header, or the catalog <c>/Version</c>, was set to the version the target fixes.</summary>
    public const string VersionSet = "PDFA.VERSION_SET";

    /// <summary>The catalog gained an XMP <c>/Metadata</c> stream that declares the target.</summary>
    public const string MetadataAdded = "PDFA.METADATA_ADDED";

    /// <summary>The XMP <c>/Metadata</c> stream existed and its identifier was rewritten in place.</summary>
    public const string MetadataRewritten = "PDFA.METADATA_REWRITTEN";

    /// <summary>An <c>/OutputIntent</c> with the caller's ICC profile was written.</summary>
    public const string OutputIntentAdded = "PDFA.OUTPUT_INTENT_ADDED";

    /// <summary>The catalog gained <c>/MarkInfo &lt;&lt; /Marked true &gt;&gt;</c>.</summary>
    public const string MarkInfoAdded = "PDFA.MARK_INFO_ADDED";

    /// <summary>A font gained a generated <c>/ToUnicode</c> CMap for its <c>WinAnsiEncoding</c>.</summary>
    public const string ToUnicodeAdded = "PDFA.TO_UNICODE_ADDED";

    /// <summary>The catalog's <c>/Names /JavaScript</c> name tree was removed.</summary>
    public const string JavaScriptRemoved = "PDFA.JAVASCRIPT_REMOVED";

    /// <summary>An entry carrying an action the standard forbids was removed.</summary>
    public const string ActionRemoved = "PDFA.ACTION_REMOVED";

    /// <summary>The catalog's <c>/Lang</c> was set from <see cref="PdfAConversionOptions.Language"/>.</summary>
    public const string LanguageSet = "PDFA.LANGUAGE_SET";

    /// <summary>
    /// PDF/A-4f was asked for and the document carries no embedded file, which the profile requires:
    /// nothing was written, because a PDF/A-4 rewritten as 4f would be a 4f claim with no file in it.
    /// </summary>
    public const string A4fWithoutEmbeddedFile = "PDFA.A4F_WITHOUT_EMBEDDED_FILE";

    /// <summary>
    /// An embedded file states no <c>/AFRelationship</c>, which PDF/A-4f requires of every embedded
    /// file. One refusal is reported per file, naming it.
    /// </summary>
    public const string A4fAttachmentWithoutRelationship = "PDFA.A4F_ATTACHMENT_WITHOUT_RELATIONSHIP";

    /// <summary>An embedded file states an <c>/AFRelationship</c> this converter does not know.</summary>
    public const string A4fRelationshipUnknown = "PDFA.A4F_RELATIONSHIP_UNKNOWN";

    /// <summary>The catalog gained the <c>/AF</c> array listing the document's associated files.</summary>
    public const string A4fAssociatedFiles = "PDFA.A4F_ASSOCIATED_FILES";

    /// <summary>The catalog's <c>/Names /EmbeddedFiles</c> tree was (re)written with every embedded file.</summary>
    public const string A4fEmbeddedFilesTree = "PDFA.A4F_EMBEDDED_FILES_TREE";

    /// <summary>
    /// PDF/A-4 was asked for and the document carries embedded files. The base level permits only
    /// embedded files that are themselves PDF/A, which a rewrite cannot establish.
    /// </summary>
    public const string A4WithEmbeddedFiles = "PDFA.A4_WITH_EMBEDDED_FILES";
}
