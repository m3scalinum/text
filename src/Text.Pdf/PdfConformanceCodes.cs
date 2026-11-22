namespace M3scalinum.Text.Pdf;

/// <summary>
/// The stable codes <see cref="PdfConformance"/> reports. A code never changes once published;
/// a check that is removed keeps its code reserved so a caller's filter does not silently match
/// something else.
/// </summary>
public static class PdfConformanceCodes
{
    /// <summary>The bytes are not a PDF, or the header is unreadable.</summary>
    public const string NotAPdf = "PDF.NOT_A_PDF";

    /// <summary>The cross-reference is missing, out of range, or points at the wrong objects.</summary>
    public const string XrefInvalid = "PDF.XREF_INVALID";

    /// <summary>The trailer has no usable <c>/Size</c>, or <c>/Size</c> does not cover the objects.</summary>
    public const string TrailerInvalid = "PDF.TRAILER_INVALID";

    /// <summary>The trailer names no catalog, or it does not resolve.</summary>
    public const string CatalogMissing = "PDF.CATALOG_MISSING";

    /// <summary>The catalog is not <c>/Type /Catalog</c>.</summary>
    public const string CatalogInvalid = "PDF.CATALOG_INVALID";

    /// <summary>The catalog names no usable page tree, or the tree has no page.</summary>
    public const string PagesInvalid = "PDF.PAGES_INVALID";

    /// <summary>An object's syntax is broken, so it could not be read; the checks see it as missing.</summary>
    public const string ObjectUnreadable = "PDF.OBJECT_UNREADABLE";

    /// <summary>The file is encrypted; its content cannot be checked.</summary>
    public const string Encrypted = "PDF.ENCRYPTED";

    /// <summary>The header (or catalog <c>/Version</c>) is outside the range the standard allows.</summary>
    public const string VersionMismatch = "PDF.VERSION_MISMATCH";

    /// <summary>The catalog names no XMP metadata stream.</summary>
    public const string MetadataMissing = "PDF.METADATA_MISSING";

    /// <summary>The metadata stream uses an encoding this reader does not decode.</summary>
    public const string MetadataUnreadable = "PDF.METADATA_UNREADABLE";

    /// <summary>The XMP identifier does not declare the claimed standard and level.</summary>
    public const string XmpStandardMismatch = "PDF.XMP_STANDARD_MISMATCH";

    /// <summary>A font used by a page has no embedded font program.</summary>
    public const string FontNotEmbedded = "PDF.FONT_NOT_EMBEDDED";

    /// <summary>A font used by a page has no <c>/ToUnicode</c> CMap.</summary>
    public const string FontWithoutToUnicode = "PDF.FONT_TO_UNICODE_MISSING";

    /// <summary>The catalog has no <c>/MarkInfo</c> dictionary whose <c>/Marked</c> is true.</summary>
    public const string MarkInfoMissing = "PDFUA.MARK_INFO_MISSING";

    /// <summary>The catalog names no structure tree, or it does not resolve.</summary>
    public const string StructTreeMissing = "PDFUA.STRUCT_TREE_MISSING";

    /// <summary>The structure tree's parent links, parent tree, or page associations disagree.</summary>
    public const string StructTreeInconsistent = "PDFUA.STRUCT_TREE_INCONSISTENT";

    /// <summary>The catalog declares no natural language.</summary>
    public const string LanguageMissing = "PDFUA.LANG_MISSING";

    /// <summary>Neither the document information nor the XMP carries a title.</summary>
    public const string TitleMissing = "PDFUA.TITLE_MISSING";

    /// <summary>A <c>/Figure</c> structure element has no <c>/Alt</c> and no <c>/ActualText</c>.</summary>
    public const string FigureAlternateTextMissing = "PDFUA.FIGURE_ALT_MISSING";

    /// <summary>
    /// The pages draw images but the structure tree has no figure element at all, so the images are
    /// either decorative or unexplained. Only the bytes are known; a reader cannot tell.
    /// </summary>
    public const string ImageNotStructured = "PDFUA.IMAGE_NOT_STRUCTURED";

    /// <summary>
    /// A table's rows hold data cells (<c>/TD</c>) but no header cell (<c>/TH</c>), so a reader
    /// cannot announce which column a cell belongs to. Reported by
    /// <see cref="PdfConformance.CheckAccessibility"/> only.
    /// </summary>
    public const string TableHeaderMissing = "PDFUA.TABLE_HEADER_MISSING";

    /// <summary>
    /// A <c>/TH</c> header cell carries no <c>/Scope</c> (<c>Row</c>, <c>Column</c> or <c>Both</c>),
    /// so a reader cannot tell whether the header describes its row or its column. Reported by
    /// <see cref="PdfConformance.CheckAccessibility"/> only.
    /// </summary>
    public const string TableHeaderScopeMissing = "PDFUA.TABLE_HEADER_SCOPE_MISSING";

    /// <summary>
    /// A heading follows one more than one level above it (<c>/H1</c> then <c>/H3</c>), so a reader
    /// announces an outline with a missing level. Reported by
    /// <see cref="PdfConformance.CheckAccessibility"/> only.
    /// </summary>
    public const string HeadingLevelSkipped = "PDFUA.HEADING_LEVEL_SKIPPED";

    /// <summary>
    /// A tagged page does not declare <c>/Tabs /S</c>, so a reader may follow the drawing order
    /// rather than the structure tree's reading order. Reported by
    /// <see cref="PdfConformance.CheckAccessibility"/> only.
    /// </summary>
    public const string TabOrderNotStructure = "PDFUA.TABS_NOT_STRUCTURE";

    /// <summary>
    /// The catalog does not set <c>/ViewerPreferences /DisplayDocTitle true</c>, so a reader shows
    /// the file name in its title bar instead of the document title. Reported by
    /// <see cref="PdfConformance.CheckAccessibility"/> only.
    /// </summary>
    public const string DisplayDocTitleMissing = "PDFUA.DISPLAY_DOC_TITLE_MISSING";

    /// <summary>
    /// A structure element uses a role that is neither a standard structure type nor mapped by the
    /// root's <c>/RoleMap</c>, so a reader has no standard role to announce. Reported by
    /// <see cref="PdfConformance.CheckAccessibility"/> only.
    /// </summary>
    public const string RoleNotMapped = "PDFUA.ROLE_NOT_MAPPED";

    /// <summary>
    /// The catalog names no <c>/AF</c> array, so the document states no associated file at all.
    /// Reported for PDF/A-4f only.
    /// </summary>
    public const string AssociatedFilesMissing = "PDFA4F.AF_MISSING";

    /// <summary>
    /// The <c>/Names /EmbeddedFiles</c> tree is missing or holds no entry, and PDF/A-4f requires at
    /// least one embedded file. Reported for PDF/A-4f only.
    /// </summary>
    public const string EmbeddedFilesMissing = "PDFA4F.EMBEDDED_FILES_MISSING";

    /// <summary>
    /// An embedded file states no <c>/AFRelationship</c>, which PDF/A-4f requires of every one.
    /// Reported for PDF/A-4f only.
    /// </summary>
    public const string EmbeddedFileRelationshipMissing = "PDFA4F.RELATIONSHIP_MISSING";

    /// <summary>
    /// An embedded file states an <c>/AFRelationship</c> that is neither one of the values ISO 32000-2
    /// defines, nor <c>/Schema</c>, nor a second-class name. Reported for PDF/A-4f only.
    /// </summary>
    public const string EmbeddedFileRelationshipUnknown = "PDFA4F.RELATIONSHIP_UNKNOWN";

    /// <summary>
    /// The catalog's <c>/AF</c> and the <c>/EmbeddedFiles</c> name tree disagree: a named embedded
    /// file is not listed as an associated file, or an <c>/AF</c> entry is not a usable file
    /// specification. Reported for PDF/A-4f only.
    /// </summary>
    public const string AssociatedFilesInconsistent = "PDFA4F.AF_INCONSISTENT";
}
