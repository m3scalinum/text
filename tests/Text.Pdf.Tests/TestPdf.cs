using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// PDFs for the tests to sign. The cross-reference offsets are real, so the files can be handed to
/// an external validator as well as to this library.
/// </summary>
internal static class TestPdf
{
    /// <summary>The smallest PDF the signer accepts: a catalog, an empty page tree, a trailer.</summary>
    public const string MinimalText = """
        %PDF-1.7
        1 0 obj
        <</Type/Catalog/Pages 2 0 R>>
        endobj
        2 0 obj
        <</Type/Pages/Count 0/Kids[]>>
        endobj
        trailer
        <</Size 3/Root 1 0 R>>
        startxref
        0
        %%EOF
        """;

    public static byte[] Minimal() => Encoding.Latin1.GetBytes(MinimalText);

    /// <summary>A one-page A4 document with a text line and a valid cross-reference table.</summary>
    public static byte[] OnePage() => WithPages(1);

    /// <summary>An <paramref name="pages"/>-page A4 document with a text line per page.</summary>
    public static byte[] WithPages(int pages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);

        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();

        void Add(int number, string body)
        {
            offsets.Add(text.Length);
            text.Append(number.ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n")
                .Append(body)
                .Append("\nendobj\n");
        }

        var kids = new List<string>();
        for (var page = 0; page < pages; page++)
        {
            // Object 1 is the catalog, 2 the page tree, 3 the font; then two objects per page.
            kids.Add((4 + (page * 2)).ToString(CultureInfo.InvariantCulture) + " 0 R");
        }

        Add(1, "<</Type/Catalog/Pages 2 0 R>>");
        Add(2, "<</Type/Pages/Count " + pages.ToString(CultureInfo.InvariantCulture)
            + "/Kids[" + string.Join(" ", kids) + "]>>");
        Add(3, "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>");

        for (var page = 0; page < pages; page++)
        {
            var pageNumber = 4 + (page * 2);
            var contentNumber = pageNumber + 1;
            var content = "BT /F1 24 Tf 72 720 Td (Page "
                + (page + 1).ToString(CultureInfo.InvariantCulture)
                + ") Tj ET";

            Add(pageNumber, "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]"
                + "/Resources<</Font<</F1 3 0 R>>>>"
                + "/Contents " + contentNumber.ToString(CultureInfo.InvariantCulture) + " 0 R>>");

            Add(contentNumber, "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"
                + content + "\nendstream");
        }

        var size = offsets.Count + 1;
        var xref = text.Length;

        text.Append("xref\n0 ").Append(size.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        text.Append("trailer\n<</Size ").Append(size.ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R>>\nstartxref\n")
            .Append(xref.ToString(CultureInfo.InvariantCulture))
            .Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(text.ToString());
    }
}
