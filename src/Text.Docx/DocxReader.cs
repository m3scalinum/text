using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using M3scalinum.Text.Docx.Internal;

namespace M3scalinum.Text.Docx;

/// <summary>Reads a Word `.docx` package into a <see cref="DocxDocument"/>.</summary>
/// <remarks>
/// Bytes in, a typed tree out: no compiler, no fonts, no network. Everything is decided from the
/// package itself, so a document read on one machine reads the same on another.
/// </remarks>
public static class DocxReader
{
    /// <summary>Reads a package with the default options.</summary>
    /// <param name="docx">The bytes of a `.docx` file.</param>
    /// <exception cref="DocxException">The bytes are not a package this reader can open.</exception>
    public static DocxDocument Read(byte[] docx) => Read(docx, new DocxOptions());

    /// <summary>Reads a package.</summary>
    /// <param name="docx">The bytes of a `.docx` file.</param>
    /// <param name="options">What to extract beyond the text; defaults are used when null.</param>
    /// <exception cref="DocxException">The bytes are not a package this reader can open.</exception>
    public static DocxDocument Read(byte[] docx, DocxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(docx);
        options ??= new DocxOptions();
        options.Validate();

        if (docx.Length == 0)
        {
            throw new DocxException(
                DocxErrorCode.InvalidArgument, "the document is empty");
        }

        using var document = DocxNative.Parse(
            docx, DocxNative.OptionsJson(options.ExtractImages, options.MaxDocumentBytes));

        var ast = ReadAst(document);

        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var image in ast.Images)
        {
            images[image.Name] = DocxNative.ReadImage(document, image.BytesHandle);
        }

        return DocxModel.Build(ast, images);
    }

    /// <summary>Reads a package from a stream, which is buffered whole.</summary>
    /// <param name="stream">A readable stream positioned at the start of the package.</param>
    /// <param name="options">What to extract beyond the text; defaults are used when null.</param>
    /// <remarks>
    /// A DOCX is a zip: its parts are stored at the end of the file, so a stream cannot be read
    /// incrementally. The stream is read to its end, up to
    /// <see cref="DocxOptions.MaxDocumentBytes"/>.
    /// </remarks>
    /// <exception cref="DocxException">The bytes are not a package this reader can open.</exception>
    public static DocxDocument Read(Stream stream, DocxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Read(ReadAll(stream, options ?? new DocxOptions()), options);
    }

    private static byte[] ReadAll(Stream stream, DocxOptions options)
    {
        options.Validate();

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > options.MaxDocumentBytes)
            {
                throw new DocxException(
                    DocxErrorCode.Limit,
                    $"the document is larger than the {options.MaxDocumentBytes}-byte limit");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static DocxAstDto ReadAst(DocxHandle document)
    {
        var json = DocxNative.ReadAst(document);

        try
        {
            return JsonSerializer.Deserialize(json, DocxJsonContext.Default.DocxAstDto)
                ?? throw new DocxException(
                    DocxErrorCode.Internal, "the native reader returned an empty document tree");
        }
        catch (JsonException exception)
        {
            throw new DocxException(
                DocxErrorCode.Internal,
                "the native reader returned a document tree this package cannot read",
                exception);
        }
    }
}
