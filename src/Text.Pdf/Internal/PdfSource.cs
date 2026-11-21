using System;

namespace M3scalinum.Text.Pdf;

/// <summary>The guards every page or form operation runs before it touches a document.</summary>
internal static class PdfSource
{
    /// <summary>
    /// Reads <paramref name="pdf"/>, refusing the documents these operations do not handle: bytes
    /// that are not a PDF, an encrypted file, a catalog that cannot be resolved, and a page tree
    /// that could not be read at all.
    /// </summary>
    /// <param name="pdf">The bytes.</param>
    /// <param name="operation">What the caller was doing, for the message.</param>
    /// <exception cref="PdfOperationException">The document cannot be operated on.</exception>
    public static PdfFile Open(byte[] pdf, string operation)
    {
        if (!PdfFile.TryOpen(pdf, out var file))
        {
            throw new PdfOperationException($"the bytes are not a PDF, so there is nothing to {operation}");
        }

        if (file.IsEncrypted)
        {
            throw new PdfOperationException(
                $"the document is encrypted, so there is nothing to {operation} until it is decrypted: open it with PdfEncryption.Open and use the bytes it returns");
        }

        if (file.Catalog is null)
        {
            throw new PdfOperationException(
                $"the document has no readable catalog, so there is nothing to {operation}");
        }

        return file;
    }
}
