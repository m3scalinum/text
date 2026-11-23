using System;
using System.IO;

namespace M3scalinum.Text.Typst.Native;

/// <summary>
/// The native surface as the managed layer sees it. Internal on purpose: it exists so the
/// public API can be unit-tested without loading a native library.
/// </summary>
internal interface ITypstNative
{
    string TypstVersion { get; }

    TypstWorldHandle CreateWorld(string? configJson);

    void AddSource(TypstWorldHandle world, string path, ReadOnlySpan<byte> utf8);

    void AddAsset(TypstWorldHandle world, string path, ReadOnlySpan<byte> bytes);

    void AddFont(TypstWorldHandle world, ReadOnlySpan<byte> bytes, uint faceIndex);

    void ClearSources(TypstWorldHandle world);

    TypstDocumentHandle Compile(TypstWorldHandle world, string optionsJson, out string diagnosticsJson);

    byte[] Export(TypstDocumentHandle document, string format, string? optionsJson);

    /// <summary>
    /// Exports straight into <paramref name="destination"/>, in chunks, so the caller never holds
    /// a second full copy of the output.
    /// </summary>
    void ExportTo(TypstDocumentHandle document, string format, string? optionsJson, Stream destination);

    byte[] BundleAsset(TypstDocumentHandle document, int index);

    byte[] Evaluate(TypstDocumentHandle document);

    /// <summary>
    /// The text of one page of a paged document, read from the layout's frame tree.
    /// </summary>
    byte[] PageText(TypstDocumentHandle document, int page);

    byte[] FontFamilies(TypstWorldHandle world);

    /// <summary>
    /// Every face the world resolves, as the JSON array <c>tn_world_font_faces</c> returns:
    /// family, default weight and width, style, whether the face is variable, and its axes.
    /// </summary>
    byte[] FontFaces(TypstWorldHandle world);

    TypstWorldHandle CloneWorld(TypstWorldHandle world);

    string GetDiagnostics(TypstDocumentHandle document);

    /// <summary>
    /// What the compilation cost, as the JSON object <c>tn_document_metrics</c> returns: the time
    /// inside Typst, the pages, the fonts and their bytes.
    /// </summary>
    byte[] Metrics(TypstDocumentHandle document);

    /// <summary>
    /// The page fingerprints of a paged document, as the JSON object
    /// <c>tn_document_page_fingerprints</c> returns. Empty for a document without pages.
    /// </summary>
    byte[] PageFingerprints(TypstDocumentHandle document);

    /// <summary>
    /// Drops every memoized compilation result the process holds. The next compilation of any
    /// document is cold again.
    /// </summary>
    void ClearCache();
}
