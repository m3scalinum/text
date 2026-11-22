using System;
using System.IO;
using System.Text.Json;
using M3scalinum.Text.Xlsx.Internal;

namespace M3scalinum.Text.Xlsx;

/// <summary>Reads an Excel <c>.xlsx</c> workbook into an <see cref="XlsxWorkbook"/>.</summary>
/// <remarks>
/// Bytes in, typed sheets out: no Excel, no Open XML SDK, no network. Everything is decided from the
/// package itself, so a workbook read on one machine reads the same on another.
/// </remarks>
public static class XlsxReader
{
    /// <summary>Reads a workbook with the default options.</summary>
    /// <param name="xlsx">The bytes of an <c>.xlsx</c> file.</param>
    /// <returns>The workbook.</returns>
    /// <exception cref="XlsxException">The bytes are not a package this reader can open.</exception>
    public static XlsxWorkbook Read(byte[] xlsx) => Read(xlsx, new XlsxOptions());

    /// <summary>Reads a workbook.</summary>
    /// <param name="xlsx">The bytes of an <c>.xlsx</c> file.</param>
    /// <param name="options">The ceilings to apply; defaults are used when null.</param>
    /// <returns>The workbook.</returns>
    /// <exception cref="XlsxException">The bytes are not a package this reader can open.</exception>
    public static XlsxWorkbook Read(byte[] xlsx, XlsxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(xlsx);
        options ??= new XlsxOptions();
        options.Validate();

        if (xlsx.Length == 0)
        {
            throw new XlsxException(XlsxErrorCode.InvalidArgument, "the workbook is empty");
        }

        using var workbook = XlsxNative.Parse(
            xlsx, XlsxNative.OptionsJson(options.MaxWorkbookBytes, options.MaxCells));

        return XlsxModel.Build(ReadAst(workbook));
    }

    /// <summary>Reads a workbook from a stream, which is buffered whole.</summary>
    /// <param name="stream">A readable stream positioned at the start of the package.</param>
    /// <param name="options">The ceilings to apply; defaults are used when null.</param>
    /// <returns>The workbook.</returns>
    /// <remarks>
    /// An XLSX is a zip: its parts are stored at the end of the file, so a stream cannot be read
    /// incrementally. The stream is read to its end, up to <see cref="XlsxOptions.MaxWorkbookBytes"/>.
    /// </remarks>
    /// <exception cref="XlsxException">The bytes are not a package this reader can open.</exception>
    public static XlsxWorkbook Read(Stream stream, XlsxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Read(ReadAll(stream, options ?? new XlsxOptions()), options);
    }

    private static byte[] ReadAll(Stream stream, XlsxOptions options)
    {
        options.Validate();

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > options.MaxWorkbookBytes)
            {
                throw new XlsxException(
                    XlsxErrorCode.Limit,
                    $"the workbook is larger than the {options.MaxWorkbookBytes}-byte limit");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static XlsxWorkbookDto ReadAst(XlsxHandle workbook)
    {
        var json = XlsxNative.ReadAst(workbook);

        try
        {
            return JsonSerializer.Deserialize(json, XlsxJsonContext.Default.XlsxWorkbookDto)
                ?? throw new XlsxException(
                    XlsxErrorCode.Internal, "the native reader returned an empty workbook");
        }
        catch (JsonException exception)
        {
            throw new XlsxException(
                XlsxErrorCode.Internal,
                "the native reader returned a workbook this package cannot read",
                exception);
        }
    }
}
