using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Typst.Diagnostics;
using M3scalinum.Text.Typst.Export;
using M3scalinum.Text.Typst.Internal;
using M3scalinum.Text.Typst.Native;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// A laid-out document. Exporting is repeatable and side-effect free; the document is
/// already laid out, so a second export costs only the encoding.
/// </summary>
public sealed class CompilationResult : IDisposable
{
    private readonly ITypstNative _native;
    private readonly TypstDocumentHandle _handle;
    private bool _disposed;

    internal CompilationResult(
        ITypstNative native,
        TypstDocumentHandle handle,
        IReadOnlyList<TypstDiagnostic> diagnostics,
        TypstDocumentKind kind,
        CompilationMetrics metrics)
    {
        _native = native;
        _handle = handle;
        Diagnostics = diagnostics;
        Kind = kind;
        Metrics = metrics;
    }

    /// <summary>Which layout pass produced this document.</summary>
    public TypstDocumentKind Kind { get; }

    /// <summary>
    /// What this compilation cost: time, pages, fonts and diagnostics, measured when it ran.
    /// </summary>
    /// <remarks>
    /// Fixed at compile time — the result carries it rather than a process-wide collector, so
    /// parallel compilations never see each other's numbers. The size of a produced file is not
    /// here: it does not exist until an export runs, and it is reported by <see cref="LastExport"/>.
    /// </remarks>
    public CompilationMetrics Metrics { get; }

    /// <summary>
    /// The most recent export of this document: format, bytes and duration. <see langword="null"/>
    /// until the first export succeeds.
    /// </summary>
    /// <remarks>
    /// Set only by a successful export, and by the last one when a caller exports twice: it is a
    /// reading of what this result produced, not an audit log of every call. That is enough for the
    /// question it answers — how large is the document, and what did encoding it cost — without
    /// keeping a list per result.
    /// </remarks>
    public ExportMetrics? LastExport { get; private set; }

    /// <summary>Number of pages. Zero for an HTML document, which has no pages.</summary>
    public int PageCount => _handle.PageCount;

    /// <summary>Warnings and hints produced by a successful compilation.</summary>
    public IReadOnlyList<TypstDiagnostic> Diagnostics { get; }

    /// <summary>Exports the document with default options for the format.</summary>
    public byte[] Export(ExportFormat format)
        => ExportCore(format, page: null, ppi: null, pretty: null);

    /// <summary>Exports one page, or all pages merged, as SVG.</summary>
    public byte[] Export(ExportFormat format, SvgExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ExportCore(format, options.Page, ppi: null, options.Pretty);
    }

    /// <summary>Exports one page, or all pages merged, as PNG.</summary>
    public byte[] Export(ExportFormat format, PngExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ExportCore(format, options.Page, options.Ppi, pretty: null);
    }

    /// <summary>Exports as HTML.</summary>
    public byte[] Export(ExportFormat format, HtmlExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ExportCore(format, page: null, ppi: null, options.Pretty);
    }

    /// <summary>Exports to PDF.</summary>
    public byte[] ExportPdf() => Export(ExportFormat.Pdf);

    /// <summary>Exports to PDF with explicit options: standards, page ranges, metadata.</summary>
    public byte[] ExportPdf(PdfExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ExportCore(ExportFormat.Pdf, page: null, ppi: null, pretty: null, pdf: options);
    }

    /// <summary>Writes the PDF into <paramref name="destination"/> without a full managed copy.</summary>
    public void ExportPdf(Stream destination) => Export(ExportFormat.Pdf, destination);

    /// <summary>
    /// Writes the PDF into <paramref name="destination"/> with explicit options, without a full
    /// managed copy.
    /// </summary>
    public void ExportPdf(Stream destination, PdfExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ExportToCore(destination, ExportFormat.Pdf, options);
    }

    /// <summary>
    /// Which fonts a PDF export of this document embeds, and what each one costs in the file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers come from the encoded PDF, so this call encodes the document — a second export
    /// when the caller already holds the bytes. It is a measurement path, not something to run per
    /// request.
    /// </para>
    /// <para>
    /// Typst's PDF writer subsets every face and offers no switch to embed whole fonts, so an
    /// inventory is the actionable half of "control font subsetting": it says how many bytes are
    /// type, which face dominates, and whether the export did what the caller expected. It does not
    /// change what <see cref="ExportPdf()"/> produces, and it does not update
    /// <see cref="LastExport"/> — nothing was exported to the caller.
    /// </para>
    /// </remarks>
    /// <param name="options">
    /// The PDF options to measure under. Pass the same options as the real export: page ranges and
    /// standards change which pages, and therefore which fonts, the file needs.
    /// </param>
    /// <exception cref="TypstNativeException">
    /// The document is not paged, or the options are rejected by the compiler.
    /// </exception>
    public PdfFontInventory MeasurePdfFonts(PdfExportOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var json = BuildExportOptionsJson(page: null, ppi: null, pretty: null, options);
        string report;

        try
        {
            report = Encoding.UTF8.GetString(_native.Export(_handle, "pdf-fonts", json));
        }
        catch (NativeCallException exception)
        {
            throw NativeErrorTranslator.Translate(exception);
        }

        var dto = JsonSerializer.Deserialize(report, TypstJsonContext.Default.PdfFontInventoryDto)
            ?? new PdfFontInventoryDto();

        return new PdfFontInventory(dto);
    }

    /// <summary>
    /// Exports into <paramref name="destination"/> as the bytes are produced, without
    /// materialising the output in a managed array.
    /// </summary>
    /// <remarks>
    /// The encoded document still exists once, contiguously, in native memory — Typst's writers
    /// return a whole file. What this overload avoids is the second, managed copy of the same
    /// size, so the peak allocation of a large export is halved. Use it to write an HTTP response
    /// body or a file.
    /// </remarks>
    public void Export(ExportFormat format, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ExportToCore(destination, format, pdf: null);
    }

    /// <summary>Exports one page as SVG.</summary>
    public byte[] ExportSvg(int page = 0)
        => Export(ExportFormat.Svg, new SvgExportOptions { Page = page });

    /// <summary>Exports every page into a single SVG, stacked vertically.</summary>
    public byte[] ExportSvgMerged()
        => ExportCore(ExportFormat.Svg, page: null, ppi: null, pretty: null, merged: true);

    /// <summary>Exports one page as PNG.</summary>
    public byte[] ExportPng(int page = 0, float ppi = 144f)
        => Export(ExportFormat.Png, new PngExportOptions { Page = page, Ppi = ppi });

    /// <summary>Exports every page into a single PNG, stacked vertically.</summary>
    public byte[] ExportPngMerged(float ppi = 144f)
        => ExportCore(ExportFormat.Png, page: null, ppi: ppi, pretty: null, merged: true);

    /// <summary>Exports as an HTML string.</summary>
    public string ExportHtmlString()
        => Encoding.UTF8.GetString(Export(ExportFormat.Html, new HtmlExportOptions()));

    /// <summary>Exports as Markdown, derived from the HTML document.</summary>
    public string ExportMarkdown() => Encoding.UTF8.GetString(Export(ExportFormat.Markdown));

    /// <summary>Exports as plain text, derived from the HTML document.</summary>
    public string ExportText() => Encoding.UTF8.GetString(Export(ExportFormat.Text));

    /// <summary>
    /// Reads the text of one page of a paged document: the characters of the layout's frames, in
    /// the order the frame tree yields them.
    /// </summary>
    /// <remarks>
    /// This is the layout's text, not a semantic reconstruction: it carries the page boundary the
    /// layout decided, but a heading is just text on a line and a list item is just a line that
    /// starts with a bullet. Ligatures and hyphenation come out as the font mapped them. Use
    /// <see cref="ExportText"/> for a document compiled as HTML when structure matters.
    /// </remarks>
    /// <param name="page">Zero-based page index.</param>
    /// <exception cref="TypstNativeException">
    /// The document is not paged, or <paramref name="page"/> is out of range.
    /// </exception>
    public string ExtractText(int page)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            return Encoding.UTF8.GetString(_native.PageText(_handle, page));
        }
        catch (NativeCallException exception)
        {
            throw NativeErrorTranslator.Translate(exception);
        }
    }

    /// <summary>
    /// Reads the text of every page of a paged document, one entry per page, in page order.
    /// </summary>
    /// <exception cref="TypstNativeException">The document is not paged.</exception>
    public IReadOnlyList<string> ExtractTextPages()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A paged document always has at least one page, so a document with no pages is one that
        // was not laid out as pages at all. Saying so is more useful than returning an empty list
        // and letting the caller believe the document has no text.
        if (Kind != TypstDocumentKind.Paged)
        {
            throw new TypstNativeException(
                "page text requires a paged document; compile with document = \"paged\"",
                TypstErrorCode.InvalidArgument);
        }

        var count = PageCount;
        var pages = new List<string>(count);

        for (var page = 0; page < count; page++)
        {
            pages.Add(ExtractText(page));
        }

        return pages;
    }

    /// <summary>
    /// Exports the files of a bundle. Requires a document compiled with
    /// <see cref="TypstDocumentKind.Bundle"/>.
    /// </summary>
    public IReadOnlyList<BundleFile> ExportBundleFiles()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string manifestJson;

        try
        {
            manifestJson = Encoding.UTF8.GetString(_native.Export(_handle, "bundle", optionsJson: null));
        }
        catch (NativeCallException exception)
        {
            throw NativeErrorTranslator.Translate(exception);
        }

        var manifest = JsonSerializer.Deserialize(manifestJson, TypstJsonContext.Default.BundleManifestDto)
            ?? new BundleManifestDto();

        var count = manifest.Assets?.Count ?? 0;
        var files = new List<BundleFile>(count);

        for (var index = 0; index < count; index++)
        {
            var entry = manifest.Assets![index];

            try
            {
                files.Add(new BundleFile(entry.Path ?? string.Empty, _native.BundleAsset(_handle, index)));
            }
            catch (NativeCallException exception)
            {
                throw NativeErrorTranslator.Translate(exception);
            }
        }

        return files;
    }

    /// <summary>
    /// Reads the document's outline, labels and metadata. Requires a document compiled with
    /// <see cref="TypstDocumentKind.Paged"/>.
    /// </summary>
    public TypstEvaluation Evaluate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string json;

        try
        {
            json = Encoding.UTF8.GetString(_native.Evaluate(_handle));
        }
        catch (NativeCallException exception)
        {
            throw NativeErrorTranslator.Translate(exception);
        }

        var dto = JsonSerializer.Deserialize(json, TypstJsonContext.Default.EvaluationDto)
            ?? new EvaluationDto();

        return new TypstEvaluation(dto);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }

    private byte[] ExportCore(
        ExportFormat format,
        int? page,
        float? ppi,
        bool? pretty,
        bool merged = false,
        PdfExportOptions? pdf = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var nativeFormat = ToNativeFormat(format, merged);
        var json = BuildExportOptionsJson(page, ppi, pretty, pdf);
        var started = Stopwatch.GetTimestamp();
        using var activity = TypstInstrumentation.StartExport(nativeFormat);
        byte[]? bytes = null;
        string? failure = null;

        try
        {
            bytes = _native.Export(_handle, nativeFormat, json);
            LastExport = new ExportMetrics(format, bytes.LongLength, Stopwatch.GetElapsedTime(started));
            return bytes;
        }
        catch (NativeCallException exception)
        {
            failure = exception.Message;
            throw NativeErrorTranslator.Translate(exception);
        }
        finally
        {
            TypstInstrumentation.RecordExport(activity, started, nativeFormat, bytes?.LongLength, failure);
        }
    }

    private void ExportToCore(Stream destination, ExportFormat format, PdfExportOptions? pdf)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var nativeFormat = ToNativeFormat(format, merged: false);
        var json = BuildExportOptionsJson(page: null, ppi: null, pretty: null, pdf);
        var position = destination.CanSeek ? destination.Position : 0L;
        var started = Stopwatch.GetTimestamp();
        using var activity = TypstInstrumentation.StartExport(nativeFormat);
        long? written = null;
        string? failure = null;

        try
        {
            _native.ExportTo(_handle, nativeFormat, json, destination);
            written = destination.CanSeek ? destination.Position - position : null;
            LastExport = new ExportMetrics(
                format,
                written ?? 0L,
                Stopwatch.GetElapsedTime(started));
        }
        catch (NativeCallException exception)
        {
            failure = exception.Message;
            throw NativeErrorTranslator.Translate(exception);
        }
        finally
        {
            TypstInstrumentation.RecordExport(activity, started, nativeFormat, written, failure);
        }
    }

    private static string BuildExportOptionsJson(
        int? page,
        float? ppi,
        bool? pretty,
        PdfExportOptions? pdf)
    {
        var dto = new ExportOptionsDto
        {
            Page = page,
            Ppi = ppi,
            Pretty = pretty,
            Pdf = pdf is null ? null : ToPdfDto(pdf),
        };

        return JsonSerializer.Serialize(dto, TypstJsonContext.Default.ExportOptionsDto);
    }

    private static PdfOptionsDto ToPdfDto(PdfExportOptions options) => new()
    {
        Standards = options.Standards.Count == 0
            ? null
            : [.. options.Standards.Select(ToNativeStandard)],
        PageRanges = options.PageRanges,
        Tagged = options.Tagged,
        Ident = options.Ident,
        Creator = options.Creator,
        Pretty = options.Pretty,
        Timestamp = options.Timestamp is { } timestamp
            ? new TimestampDto
            {
                Year = timestamp.Year,
                Month = timestamp.Month,
                Day = timestamp.Day,
                Hour = timestamp.Hour,
                Minute = timestamp.Minute,
                Second = timestamp.Second,
            }
            : null,
    };

    private static string ToNativeStandard(PdfStandard standard) => standard switch
    {
        PdfStandard.Pdf14 => "1.4",
        PdfStandard.Pdf15 => "1.5",
        PdfStandard.Pdf16 => "1.6",
        PdfStandard.Pdf17 => "1.7",
        PdfStandard.Pdf20 => "2.0",
        PdfStandard.PdfA1b => "a-1b",
        PdfStandard.PdfA1a => "a-1a",
        PdfStandard.PdfA2b => "a-2b",
        PdfStandard.PdfA2u => "a-2u",
        PdfStandard.PdfA2a => "a-2a",
        PdfStandard.PdfA3b => "a-3b",
        PdfStandard.PdfA3u => "a-3u",
        PdfStandard.PdfA3a => "a-3a",
        PdfStandard.PdfA4 => "a-4",
        PdfStandard.PdfA4f => "a-4f",
        PdfStandard.PdfA4e => "a-4e",
        PdfStandard.PdfUa1 => "ua-1",
        _ => throw new ArgumentOutOfRangeException(nameof(standard), standard, "unknown PDF standard"),
    };

    private static string ToNativeFormat(ExportFormat format, bool merged) => (format, merged) switch
    {
        (ExportFormat.Svg, true) => "svg-merged",
        (ExportFormat.Png, true) => "png-merged",
        (ExportFormat.Pdf, _) => "pdf",
        (ExportFormat.Svg, _) => "svg",
        (ExportFormat.Png, _) => "png",
        (ExportFormat.Html, _) => "html",
        (ExportFormat.Markdown, _) => "markdown",
        (ExportFormat.Text, _) => "text",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "unknown export format"),
    };
}
