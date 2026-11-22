using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Text extraction from a document this package did not produce. The compiler fixtures prove the
/// reader survives a real producer; the hand-written files state the cases a real producer makes
/// awkward to find — a CMap in every shape, a font with no CMap, a form XObject, an encrypted
/// trailer.
/// </summary>
public sealed class PdfTextTests
{
    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "text", name);

        Assert.True(File.Exists(path), $"the fixture {name} is missing from {path}");

        return File.ReadAllBytes(path);
    }

    private static string Stream(string content)
        => $"<</Length {content.Length}>>\nstream\n{content}\nendstream";

    /// <summary>The password the tests encrypt with and open with.</summary>
    private const string Password = "correct horse";

    [Fact]
    public void Every_page_of_a_compiler_document_is_read_and_its_marker_stays_on_its_page()
    {
        var result = PdfText.Extract(Fixture("extract.pdf"));

        Assert.Equal(2, result.PageCount);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(1, result.Pages[0].PageNumber);
        Assert.Equal(2, result.Pages[1].PageNumber);

        Assert.Contains("ZEBRAALPHA", result.Pages[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ZEBRABETA", result.Pages[0].Text, StringComparison.Ordinal);

        Assert.Contains("ZEBRABETA", result.Pages[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ZEBRAALPHA", result.Pages[1].Text, StringComparison.Ordinal);

        Assert.All(result.Pages, page => Assert.Empty(page.FallbackFonts));
        Assert.All(result.Pages, page => Assert.False(page.HasFallback));
        Assert.All(result.Pages, page => Assert.Equal(0, page.SkippedContentStreams));
    }

    [Fact]
    public void A_table_and_a_footnote_are_read_on_the_page_that_draws_them()
    {
        var pages = PdfText.Extract(Fixture("extract.pdf")).Pages;

        // Cells of one row are separate strings far apart horizontally: the gap is what turns them
        // into three words rather than one.
        Assert.Contains("Column Second Third", pages[0].Text, StringComparison.Ordinal);
        Assert.Contains("alpha beta gamma", pages[0].Text, StringComparison.Ordinal);
        Assert.Contains("ZEBRANOTE", pages[0].Text, StringComparison.Ordinal);

        Assert.Contains("Key Value", pages[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ZEBRANOTE", pages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_ends_with_exactly_one_newline_and_a_word_is_never_split()
    {
        foreach (var page in PdfText.Extract(Fixture("extract.pdf")).Pages)
        {
            Assert.EndsWith("\n", page.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("\n\n", page.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_document_printed_by_a_browser_is_read_like_a_compiler_document()
    {
        var result = PdfText.Extract(Fixture("foreign.pdf"));

        Assert.Equal(1, result.PageCount);

        var text = result.Pages[0].Text;
        Assert.Contains("QUOKKAFOREIGN", text, StringComparison.Ordinal);
        Assert.Contains("Browser printed document", text, StringComparison.Ordinal);
        Assert.Contains("Fruit Count", text, StringComparison.Ordinal);
        Assert.Contains("apple 3", text, StringComparison.Ordinal);
        Assert.Contains("1. A footnote the browser laid out at the bottom.", text, StringComparison.Ordinal);

        Assert.False(result.Pages[0].HasFallback);
    }

    [Fact]
    public void ExtractPage_reads_one_page_and_refuses_a_page_the_document_does_not_have()
    {
        var pdf = Fixture("extract.pdf");
        var pages = PdfText.Extract(pdf).Pages;

        var second = PdfText.ExtractPage(pdf, 1);
        Assert.Equal(2, second.PageNumber);
        Assert.Equal(pages[1].Text, second.Text, StringComparer.Ordinal);

        Assert.Throws<ArgumentOutOfRangeException>(() => PdfText.ExtractPage(pdf, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfText.ExtractPage(pdf, -1));
    }

    [Fact]
    public void A_Type0_font_is_read_through_every_shape_of_its_ToUnicode_CMap()
    {
        // bfchar for A and B; a bfrange with an array for C and D; a bfrange with one destination
        // for a, b and c. The TJ adjustment separates D from a, which is the only space on the line.
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 300]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            Stream("""
                BT
                /F1 12 Tf
                72 250 Td
                <00010002> Tj
                [<00030004> -400 <00050006>] TJ
                0 -20 Td
                <0007> Tj
                ET
                """),
            "<</Type/Font/Subtype/Type0/BaseFont/Test-Identity-H/Encoding/Identity-H/DescendantFonts[6 0 R]/ToUnicode 7 0 R>>",
            "<</Type/Font/Subtype/CIDFontType2/BaseFont/Test"
                + "/CIDSystemInfo<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>/DW 500/W[1 7 600]>>",
            Stream("""
                /CIDInit /ProcSet findresource begin
                12 dict begin
                begincmap
                /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def
                /CMapName /Adobe-Identity-UCS def
                /CMapType 2 def
                1 begincodespacerange
                <0000> <FFFF>
                endcodespacerange
                2 beginbfchar
                <0001> <0041>
                <0002> <0042>
                endbfchar
                1 beginbfrange
                <0003> <0004> [<0043> <0044>]
                endbfrange
                1 beginbfrange
                <0005> <0007> <0061>
                endbfrange
                endcmap
                CMapName currentdict /CMap defineresource pop
                end
                end
                """),
        ]);

        var page = PdfText.Extract(pdf).Pages[0];

        Assert.Contains("ABCD ab", page.Text, StringComparison.Ordinal);
        Assert.Contains("\nc\n", page.Text, StringComparison.Ordinal);
        Assert.False(page.HasFallback);
    }

    [Fact]
    public void A_font_with_no_ToUnicode_is_read_through_its_differences_and_the_page_says_so()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 300]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            Stream("BT\n/F1 12 Tf\n72 250 Td\n(ABC) Tj\n0 -20 Td\n(XYZ) Tj\nET"),
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica"
                + "/Encoding<</Type/Encoding/BaseEncoding/WinAnsiEncoding/Differences[65/Eacute]>>>>",
        ]);

        var page = PdfText.Extract(pdf).Pages[0];

        // Code 65 is the glyph the differences name; 66 and 67 are not listed, so the base encoding
        // decodes them.
        Assert.Contains("ÉBC", page.Text, StringComparison.Ordinal);
        Assert.Contains("XYZ", page.Text, StringComparison.Ordinal);
        Assert.True(page.HasFallback);
        Assert.Equal(["F1"], page.FallbackFonts);
    }

    [Fact]
    public void A_Type0_font_with_no_ToUnicode_reads_raw_codes_and_the_page_says_so()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 300]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            Stream("BT\n/F1 12 Tf\n72 250 Td\n<00410042> Tj\nET"),
            "<</Type/Font/Subtype/Type0/BaseFont/Test-Identity-H/Encoding/Identity-H/DescendantFonts[6 0 R]>>",
            "<</Type/Font/Subtype/CIDFontType2/BaseFont/Test/DW 500/W[65 66 600]>>",
        ]);

        var page = PdfText.Extract(pdf).Pages[0];

        Assert.Contains("AB", page.Text, StringComparison.Ordinal);
        Assert.True(page.HasFallback);
        Assert.Equal(["F1"], page.FallbackFonts);
    }

    [Fact]
    public void A_form_XObject_is_walked_with_its_own_resources()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 300]"
                + "/Resources<</Font<</F1 6 0 R>>/XObject<</Fm1 5 0 R>>>>/Contents 4 0 R>>",
            Stream("BT\n/F1 12 Tf\n72 250 Td\n(PageWord) Tj\nET\nq\n/Fm1 Do\nQ"),
            "<</Type/XObject/Subtype/Form/BBox[0 0 200 200]/Resources<</Font<</F2 6 0 R>>>>/Length 52>>\n"
                + "stream\nBT /F2 12 Tf 10 10 Td (FORMWORD) Tj ET\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>",
        ]);

        var page = PdfText.Extract(pdf).Pages[0];

        // The form's coordinates are its own, so its text is a run of its own rather than a
        // continuation of the page line.
        Assert.Contains("PageWord", page.Text, StringComparison.Ordinal);
        Assert.Contains("PageWord\nFORMWORD", page.Text, StringComparison.Ordinal);

        // Both names point at the same font dictionary, so the page reports it once.
        Assert.True(page.HasFallback);
        Assert.Equal(["F1"], page.FallbackFonts);
    }

    [Fact]
    public void An_encrypted_document_is_refused_and_the_error_names_the_handler()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            "<</Filter/Standard/V 2/R 3/Length 128/P -4/O<00000000000000000000000000000000>"
                + "/U<00000000000000000000000000000000>>>",
        ],
        " /Encrypt 3 0 R");

        var exception = Assert.Throws<PdfTextException>(() => PdfText.Extract(pdf));

        Assert.Equal(PdfTextError.Encrypted, exception.Error);
        Assert.Contains("encrypted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Standard", exception.Message, StringComparison.Ordinal);

        Assert.Throws<PdfTextException>(() => PdfText.ExtractPage(pdf, 0));
    }

    [Fact]
    public void An_AES_256_document_is_decrypted_with_its_password_then_read()
    {
        var encrypted = PdfEncryption.Encrypt(TestPdf.OnePage(), Password, PdfPermissions.All);

        var result = PdfText.Extract(encrypted, Password);

        Assert.Equal(1, result.PageCount);
        Assert.Contains("Page 1", result.Pages[0].Text, StringComparison.Ordinal);

        var page = PdfText.ExtractPage(encrypted, 0, Password);
        Assert.Equal(result.Pages[0].Text, page.Text, StringComparer.Ordinal);
    }

    [Fact]
    public void An_AES_128_document_is_decrypted_with_its_user_password_then_read()
    {
        // A second implementation built from the specification, not this library's writer.
        var encrypted = TestEncryption.Aes128();

        var result = PdfText.Extract(encrypted, TestEncryption.UserPassword);

        Assert.Contains(TestEncryption.ContentText, result.Pages[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_encrypted_document_without_a_password_is_still_refused()
    {
        var encrypted = PdfEncryption.Encrypt(TestPdf.OnePage(), Password, PdfPermissions.All);

        var exception = Assert.Throws<PdfTextException>(() => PdfText.Extract(encrypted));

        Assert.Equal(PdfTextError.Encrypted, exception.Error);
        Assert.Contains("encrypted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not implemented", exception.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<PdfTextException>(() => PdfText.ExtractPage(encrypted, 0));
    }

    [Fact]
    public void A_wrong_password_is_a_typed_error_and_not_an_uncontrolled_exception()
    {
        var encrypted = PdfEncryption.Encrypt(TestPdf.OnePage(), Password, PdfPermissions.All);

        var exception = Assert.Throws<PdfTextException>(() => PdfText.Extract(encrypted, "wrong horse"));

        Assert.Equal(PdfTextError.WrongPassword, exception.Error);
        Assert.Contains("password", exception.Message, StringComparison.OrdinalIgnoreCase);

        var page = Assert.Throws<PdfTextException>(() => PdfText.ExtractPage(encrypted, 0, "wrong horse"));
        Assert.Equal(PdfTextError.WrongPassword, page.Error);
    }

    [Fact]
    public void An_encrypted_document_with_an_unsupported_handler_is_refused_even_with_a_password()
    {
        // V2/R3 is RC4, which this package refuses by design: the password cannot be the reason.
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            "<</Filter/Standard/V 2/R 3/Length 128/P -4/O<00000000000000000000000000000000>"
                + "/U<00000000000000000000000000000000>>>",
        ],
        " /Encrypt 3 0 R");

        var exception = Assert.Throws<PdfTextException>(() => PdfText.Extract(pdf, Password));

        Assert.Equal(PdfTextError.Encrypted, exception.Error);
        Assert.Contains("/Standard", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_password_is_a_password_while_no_password_is_a_refusal()
    {
        var encrypted = PdfEncryption.Encrypt(TestPdf.OnePage(), string.Empty, PdfPermissions.All);

        Assert.Throws<PdfTextException>(() => PdfText.Extract(encrypted));

        var opened = PdfText.Extract(encrypted, string.Empty);
        Assert.Contains("Page 1", opened.Pages[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unencrypted_document_reads_the_same_whatever_password_is_passed()
    {
        var pdf = TestPdf.OnePage();

        var without = PdfText.Extract(pdf);
        var with = PdfText.Extract(pdf, Password);

        Assert.Equal(without.Pages[0].Text, with.Pages[0].Text, StringComparer.Ordinal);
        Assert.Equal(without.PageCount, with.PageCount);
    }

    [Fact]
    public void Bytes_that_are_not_a_PDF_are_refused_with_the_reason()
    {
        var exception = Assert.Throws<PdfTextException>(
            () => PdfText.Extract(Encoding.ASCII.GetBytes("this is not a document")));

        Assert.Equal(PdfTextError.NotAPdf, exception.Error);
    }

    [Fact]
    public void A_page_that_draws_no_text_comes_back_empty()
    {
        var result = PdfText.Extract(PdfTestDocument.Minimal());

        Assert.Equal(1, result.PageCount);
        Assert.Equal(string.Empty, result.Pages[0].Text);
        Assert.Empty(result.Pages[0].FallbackFonts);
        Assert.Equal(0, result.Pages[0].SkippedContentStreams);
    }

    [Fact]
    public void The_document_text_is_the_pages_in_order()
    {
        var result = PdfText.Extract(Fixture("extract.pdf"));

        Assert.Equal(result.Pages[0].Text + result.Pages[1].Text, result.Text, StringComparer.Ordinal);
        Assert.Equal(result.PageCount, result.Pages.Select(page => page.PageNumber).Distinct().Count());
    }
}
