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

    TypstWorldHandle CloneWorld(TypstWorldHandle world);

    string GetDiagnostics(TypstDocumentHandle document);
}
