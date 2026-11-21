using System;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The part of signing every signed object shares: a fixed-size <c>/Contents</c> placeholder, a
/// <c>/ByteRange</c> computed on the finished file, and the hex string written back in place.
/// </summary>
/// <remarks>
/// The reserved size never changes, so filling the placeholder in does not move a byte: the range
/// the signature covers is the same before and after the signature exists. That is what lets a
/// signature be computed over a file that already contains its own signature dictionary.
/// </remarks>
internal static class PdfContents
{
    /// <summary>Room reserved for the DER structure: 8 KiB, far more than a chain or a token needs.</summary>
    public const int ReservedBytes = 8192;

    /// <summary>The <c>/ByteRange</c> placeholder, written with a fixed width.</summary>
    public const string ByteRangePlaceholder = "/ByteRange[0 0000000000 0000000000 0000000000]";

    /// <summary>What an object holding a placeholder carries, before the signature exists.</summary>
    public static string EmptyContents()
        => "/Contents<" + new string('0', ReservedBytes * 2) + ">";

    /// <summary>
    /// Computes the byte range, hands the covered bytes to <paramref name="produce"/>, and writes
    /// what it returns into the placeholder as hex.
    /// </summary>
    /// <param name="built">The finished file, with zeros in the placeholder.</param>
    /// <param name="objectOffset">Where the signed object starts, so its own <c>/Contents</c> is the one found.</param>
    /// <param name="produce">Builds the DER structure over the covered bytes.</param>
    /// <exception cref="PdfSignatureException">The structure does not fit the reserved space.</exception>
    public static byte[] Seal(string built, int objectOffset, Func<byte[], byte[]> produce)
    {
        // The byte range excludes the hex string and its two delimiters.
        var contentsStart = built.IndexOf("/Contents<", objectOffset, StringComparison.Ordinal) + "/Contents".Length;
        if (contentsStart < "/Contents".Length || contentsStart >= built.Length)
        {
            throw new PdfSignatureException("the signed object carries no /Contents to fill");
        }

        var contentsEnd = contentsStart + 1 + (ReservedBytes * 2) + 1;
        var total = built.Length;

        built = built.Replace(
            ByteRangePlaceholder,
            string.Create(
                CultureInfo.InvariantCulture,
                $"/ByteRange[0 {contentsStart:D10} {contentsEnd:D10} {total - contentsEnd:D10}]"),
            StringComparison.Ordinal);

        var bytes = Encoding.Latin1.GetBytes(built);

        var signed = new byte[contentsStart + (total - contentsEnd)];
        Buffer.BlockCopy(bytes, 0, signed, 0, contentsStart);
        Buffer.BlockCopy(bytes, contentsEnd, signed, contentsStart, total - contentsEnd);

        var der = produce(signed);
        var hex = Convert.ToHexString(der);

        if (hex.Length > ReservedBytes * 2)
        {
            throw new PdfSignatureException(
                $"the signature is {hex.Length / 2} bytes and the reserved space is {ReservedBytes}; a longer certificate chain needs a larger placeholder");
        }

        var encoded = Encoding.ASCII.GetBytes(hex.PadRight(ReservedBytes * 2, '0'));
        Buffer.BlockCopy(encoded, 0, bytes, contentsStart + 1, encoded.Length);

        return bytes;
    }
}
