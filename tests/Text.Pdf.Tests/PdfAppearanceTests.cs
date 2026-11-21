using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The visible signature: a widget with a rectangle, a form XObject, and either text or an image.
/// The tests read the objects the writer produced, and decode the image bytes back to samples.
/// </summary>
public sealed class PdfAppearanceTests
{
    private static byte[] Sign(PdfSignatureAppearance appearance, byte[]? pdf = null)
        => PdfSigner.Sign(pdf ?? TestPdf.WithPages(2), TestCertificates.Signer(), new PdfSignatureOptions
        {
            SignedAt = new DateTimeOffset(2026, 2, 1, 10, 30, 0, TimeSpan.Zero),
            Appearance = appearance,
        });

    [Fact]
    public void A_text_appearance_is_drawn_on_the_requested_page()
    {
        var signed = Sign(new PdfSignatureAppearance
        {
            PageIndex = 1,
            X = 100,
            Y = 40,
            Width = 200,
            Height = 50,
            TextLines = ["Signé le 1er février", "M3scalinum"],
        });

        var text = SignedPdf.Text(signed);
        var widget = ObjectBody(text, 8);

        Assert.Contains("/Type/Annot/Subtype/Widget/FT/Sig/T(Signature1)", widget, StringComparison.Ordinal);
        Assert.Contains("/Rect[100 40 300 90]", widget, StringComparison.Ordinal);
        Assert.Contains("/P 6 0 R", widget, StringComparison.Ordinal);
        Assert.Contains("/AP<</N 10 0 R>>", widget, StringComparison.Ordinal);
        Assert.Contains("/F 132", widget, StringComparison.Ordinal);

        // The second page's object, rewritten, lists the widget among its annotations.
        Assert.Contains("/Annots[8 0 R]", ObjectBody(text, 6), StringComparison.Ordinal);

        // The form XObject carries the rectangle and the text, in Latin-1.
        var form = ObjectBody(text, 10);
        Assert.Contains("/Type/XObject/Subtype/Form/FormType 1", form, StringComparison.Ordinal);
        Assert.Contains("/BBox[0 0 200 50]", form, StringComparison.Ordinal);
        Assert.Contains("/BaseFont/Helvetica", form, StringComparison.Ordinal);
        Assert.Contains("(Sign\u00e9 le 1er f\u00e9vrier) Tj", form, StringComparison.Ordinal);
        Assert.Contains("(M3scalinum) Tj", form, StringComparison.Ordinal);
        Assert.Contains("T*", form, StringComparison.Ordinal);
    }

    [Fact]
    public void The_geometry_is_written_in_points_without_trailing_zeros()
    {
        var signed = Sign(new PdfSignatureAppearance
        {
            X = 10.5,
            Y = 20.25,
            Width = 100,
            Height = 30.5,
            TextLines = ["x"],
        });

        var text = SignedPdf.Text(signed);

        Assert.Contains("/Rect[10.5 20.25 110.5 50.75]", text, StringComparison.Ordinal);
        Assert.Contains("/BBox[0 0 100 30.5]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_text_follows_the_font_size_and_the_padding()
    {
        var signed = Sign(new PdfSignatureAppearance
        {
            X = 0,
            Y = 0,
            Width = 100,
            Height = 60,
            FontSize = 8,
            Padding = 6,
            TextLines = ["a"],
        });

        var form = ObjectBody(SignedPdf.Text(signed), 10);

        Assert.Contains("/F1 8 Tf", form, StringComparison.Ordinal);
        Assert.Contains("6 46 Td", form, StringComparison.Ordinal);
    }

    [Fact]
    public void A_character_outside_winansi_is_replaced_rather_than_written_raw()
    {
        var signed = Sign(new PdfSignatureAppearance
        {
            Width = 100,
            Height = 40,
            TextLines = ["Signé — 日本"],
        });

        var form = ObjectBody(SignedPdf.Text(signed), 10);

        Assert.Contains("(Sign\u00e9 ? ??) Tj", form, StringComparison.Ordinal);
    }

    [Fact]
    public void Parentheses_and_backslashes_are_escaped()
    {
        var signed = Sign(new PdfSignatureAppearance
        {
            Width = 100,
            Height = 40,
            TextLines = ["a (b) c\\d"],
        });

        var form = ObjectBody(SignedPdf.Text(signed), 10);

        Assert.Contains(@"(a \(b\) c\\d) Tj", form, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invisible_signature_keeps_the_zero_rectangle()
    {
        var signed = PdfSigner.Sign(TestPdf.OnePage(), TestCertificates.Signer());

        var widget = ObjectBody(SignedPdf.Text(signed), 6);

        Assert.Contains("/Rect[0 0 0 0]", widget, StringComparison.Ordinal);
        Assert.DoesNotContain("/AP", widget, StringComparison.Ordinal);
        Assert.DoesNotContain("/P ", widget, StringComparison.Ordinal);
    }

    [Fact]
    public void A_picture_appearance_embeds_a_png()
    {
        var png = TestImages.Rgb(2, 2, 10, 20, 30);
        var signed = Sign(new PdfSignatureAppearance { Width = 20, Height = 20, Image = png });

        var text = SignedPdf.Text(signed);
        var form = ObjectBody(text, 10);
        var samples = Inflate(StreamBytes(ObjectBody(text, 11)));

        var image = ObjectBody(text, 11);
        Assert.Contains("/Subtype/Image/Width 2/Height 2", image, StringComparison.Ordinal);
        Assert.Contains("/ColorSpace/DeviceRGB", image, StringComparison.Ordinal);
        Assert.Contains("/BitsPerComponent 8", image, StringComparison.Ordinal);
        Assert.Contains("/Filter/FlateDecode", image, StringComparison.Ordinal);
        Assert.Contains("/XObject<</Im0 11 0 R>>", form, StringComparison.Ordinal);
        Assert.Contains("/Im0 Do", form, StringComparison.Ordinal);

        // The picture is drawn over the whole rectangle.
        Assert.Contains("20 0 0 20 0 0 cm", form, StringComparison.Ordinal);

        Assert.Equal([10, 20, 30, 10, 20, 30, 10, 20, 30, 10, 20, 30], samples);
    }

    [Fact]
    public void The_decoder_undoes_every_png_row_filter()
    {
        var png = TestImages.Filtered(3, 9);
        var signed = Sign(new PdfSignatureAppearance { Width = 30, Height = 45, Image = png });

        var image = ObjectBody(SignedPdf.Text(signed), 11);
        var samples = Inflate(StreamBytes(image));

        Assert.Equal(TestImages.Gradient(3, 9), samples);
    }

    [Fact]
    public void An_rgba_png_becomes_a_soft_mask()
    {
        var png = TestImages.Rgba(2, 2, 128);
        var signed = Sign(new PdfSignatureAppearance { Width = 20, Height = 20, Image = png });

        var text = SignedPdf.Text(signed);
        var image = ObjectBody(text, 11);
        var mask = ObjectBody(text, 12);

        Assert.Contains("/SMask 12 0 R", image, StringComparison.Ordinal);
        Assert.Contains("/ColorSpace/DeviceRGB", image, StringComparison.Ordinal);
        Assert.Contains("/ColorSpace/DeviceGray", mask, StringComparison.Ordinal);
        Assert.Equal([128, 128, 128, 128], Inflate(StreamBytes(mask)));
        Assert.Equal([10, 20, 30, 10, 20, 30, 10, 20, 30, 10, 20, 30], Inflate(StreamBytes(image)));
    }

    [Fact]
    public void An_indexed_png_is_expanded_to_rgb()
    {
        var png = TestImages.Indexed(2, 2);
        var signed = Sign(new PdfSignatureAppearance { Width = 20, Height = 20, Image = png });

        var image = ObjectBody(SignedPdf.Text(signed), 11);

        Assert.Contains("/ColorSpace/DeviceRGB", image, StringComparison.Ordinal);
        Assert.Equal(TestImages.IndexedExpanded(2, 2), Inflate(StreamBytes(image)));
    }

    [Fact]
    public void A_grey_png_uses_the_grey_colour_space()
    {
        var signed = Sign(new PdfSignatureAppearance { Width = 20, Height = 20, Image = TestImages.Gray(2, 2, 200) });

        var image = ObjectBody(SignedPdf.Text(signed), 11);

        Assert.Contains("/ColorSpace/DeviceGray", image, StringComparison.Ordinal);
        Assert.Equal([200, 200, 200, 200], Inflate(StreamBytes(image)));
    }

    [Fact]
    public void A_jpeg_is_embedded_as_it_is()
    {
        var jpeg = TestImages.Jpeg(4, 8);
        var signed = Sign(new PdfSignatureAppearance { Width = 20, Height = 40, Image = jpeg });

        var text = SignedPdf.Text(signed);
        var image = ObjectBody(text, 11);

        Assert.Contains("/Subtype/Image/Width 4/Height 8", image, StringComparison.Ordinal);
        Assert.Contains("/ColorSpace/DeviceRGB", image, StringComparison.Ordinal);
        Assert.Contains("/Filter/DCTDecode", image, StringComparison.Ordinal);

        // Not one byte of the file is touched: no re-encoding, no inflate.
        Assert.Equal(jpeg, StreamBytes(image));
    }

    [Fact]
    public void A_jpeg_declares_the_colour_space_its_components_name()
    {
        var grey = Sign(new PdfSignatureAppearance { Width = 10, Height = 10, Image = TestImages.Jpeg(2, 2, components: 1) });
        var cmyk = Sign(new PdfSignatureAppearance { Width = 10, Height = 10, Image = TestImages.Jpeg(2, 2, components: 4) });

        Assert.Contains("/ColorSpace/DeviceGray", ObjectBody(SignedPdf.Text(grey), 11), StringComparison.Ordinal);
        Assert.Contains("/ColorSpace/DeviceCMYK", ObjectBody(SignedPdf.Text(cmyk), 11), StringComparison.Ordinal);
    }

    [Fact]
    public void A_png_of_sixteen_bits_is_refused()
    {
        var png = TestImages.Png(1, 1, colorType: 0, samples: [0, 0], bitDepth: 16);

        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(png));

        Assert.Contains("bit depth", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_interlaced_png_is_refused()
    {
        var png = TestImages.Png(1, 1, colorType: 0, samples: [7], interlace: 1);

        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(png));

        Assert.Contains("interlaced", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_png_colour_type_that_is_not_supported_is_refused()
    {
        var png = TestImages.Png(1, 1, colorType: 4, samples: [7, 8]);

        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(png));

        Assert.Contains("colour type 4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_indexed_png_without_a_palette_is_refused()
    {
        var png = TestImages.Png(1, 1, colorType: 3, samples: [0]);

        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(png));

        Assert.Contains("palette", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_png_whose_chunks_run_past_the_end_is_refused()
    {
        var png = TestImages.Rgb(2, 2, 1, 2, 3);

        // Cut into the image data chunk, so its declared length runs past the file.
        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(png.AsSpan(0, png.Length - 20).ToArray()));

        Assert.Contains("truncated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_png_with_an_unknown_row_filter_is_refused()
    {
        var png = TestImages.Png(1, 1, colorType: 0, samples: [7], filters: [5]);

        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(png));

        Assert.Contains("row filter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_png_whose_image_data_is_too_short_is_refused()
    {
        // One row of samples, but a header that announces four.
        var png = TestImages.Png(4, 1, colorType: 2, samples: TestImages.Gradient(4, 1), declaredHeight: 4);

        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(png));

        Assert.Contains("were expected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_that_are_neither_a_png_nor_a_jpeg_are_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(new byte[] { 1, 2, 3, 4, 5, 6 }));

        Assert.Contains("neither a PNG nor a JPEG", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_jpeg_without_a_frame_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));

        Assert.Contains("frame header", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_jpeg_with_two_components_is_refused()
    {
        var jpeg = TestImages.Jpeg(2, 2, components: 2);

        var exception = Assert.Throws<PdfSignatureException>(() => PdfImage.Parse(jpeg));

        Assert.Contains("2 colour components", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_appearance_on_a_document_without_pages_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(
            TestPdf.Minimal(),
            TestCertificates.Signer(),
            new PdfSignatureOptions { Appearance = new PdfSignatureAppearance { Width = 10, Height = 10, TextLines = ["x"] } }));

        Assert.Contains("0 page(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_index_past_the_last_page_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => Sign(
            new PdfSignatureAppearance { PageIndex = 2, Width = 10, Height = 10, TextLines = ["x"] }));

        Assert.Contains("asks for page 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_page_index_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => Sign(
            new PdfSignatureAppearance { PageIndex = -1, Width = 10, Height = 10, TextLines = ["x"] }));

        Assert.Contains("negative", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    public void A_rectangle_without_area_is_refused(double width, double height)
    {
        var exception = Assert.Throws<PdfSignatureException>(() => Sign(new PdfSignatureAppearance
        {
            Width = width,
            Height = height,
            TextLines = ["x"],
        }));

        Assert.Contains("must be positive", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_coordinate_that_is_not_a_number_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => Sign(new PdfSignatureAppearance
        {
            X = double.NaN,
            Width = 10,
            Height = 10,
            TextLines = ["x"],
        }));

        Assert.Contains("not a number", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_font_size_that_is_not_positive_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => Sign(new PdfSignatureAppearance
        {
            Width = 10,
            Height = 10,
            FontSize = 0,
            TextLines = ["x"],
        }));

        Assert.Contains("font size", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_padding_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => Sign(new PdfSignatureAppearance
        {
            Width = 10,
            Height = 10,
            Padding = -1,
            TextLines = ["x"],
        }));

        Assert.Contains("padding", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_appearance_with_both_text_and_an_image_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => Sign(new PdfSignatureAppearance
        {
            Width = 10,
            Height = 10,
            TextLines = ["x"],
            Image = new byte[] { 1, 2, 3 },
        }));

        Assert.Contains("both text lines and an image", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_appearance_with_neither_text_nor_an_image_is_refused()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => Sign(new PdfSignatureAppearance
        {
            Width = 10,
            Height = 10,
        }));

        Assert.Contains("neither text lines nor an image", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_text_list_is_treated_as_no_text()
    {
        var exception = Assert.Throws<PdfSignatureException>(() => Sign(new PdfSignatureAppearance
        {
            Width = 10,
            Height = 10,
            TextLines = [],
        }));

        Assert.Contains("neither", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_keeps_the_annotations_it_already_has()
    {
        // A one-page document whose page already carries an annotation.
        var pdf = Encoding.Latin1.GetBytes(SignedPdf.Text(TestPdf.OnePage()).Replace(
            "/Contents 5 0 R",
            "/Annots[3 0 R]/Contents 5 0 R",
            StringComparison.Ordinal));

        var signed = PdfSigner.Sign(pdf, TestCertificates.Signer(), new PdfSignatureOptions
        {
            Appearance = new PdfSignatureAppearance { Width = 50, Height = 20, TextLines = ["x"] },
        });

        var text = SignedPdf.Text(signed);

        // Object 4 is the page in a one-page document: both the old annotation and the widget.
        var page = ObjectBody(text, 4);
        Assert.Contains("/Annots[3 0 R 6 0 R]", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_visible_signature_still_covers_the_whole_file_and_verifies()
    {
        var signed = Sign(new PdfSignatureAppearance
        {
            Width = 200,
            Height = 50,
            TextLines = ["Signed"],
        });

        var signature = Assert.Single(PdfSignatureVerifier.Verify(signed).Signatures);

        Assert.True(signature.IsIntact, signature.Error);
        Assert.True(signature.CoversWholeFile);
        Assert.Equal(PdfSignatureLevel.BaselineB, signature.Level);
    }

    [Fact]
    public void The_appearance_does_not_move_the_earlier_revision()
    {
        var pdf = TestPdf.OnePage();
        var signed = Sign(
            new PdfSignatureAppearance { Width = 100, Height = 40, TextLines = ["x"] },
            pdf);

        Assert.Equal(pdf, signed.AsSpan(0, pdf.Length).ToArray());
    }

    [Fact]
    public void Two_visible_signatures_each_get_their_annotation()
    {
        var first = Sign(new PdfSignatureAppearance { Width = 100, Height = 40, TextLines = ["one"] }, TestPdf.OnePage());

        var second = PdfSigner.Sign(first, TestCertificates.Signer(), new PdfSignatureOptions
        {
            FieldName = "Second",
            Appearance = new PdfSignatureAppearance { Y = 100, Width = 100, Height = 40, TextLines = ["two"] },
        });

        var page = ObjectBody(SignedPdf.Text(second), 4);

        Assert.Contains("/T(Second)", SignedPdf.Text(second), StringComparison.Ordinal);
        Assert.Contains("/Annots[6 0 R 9 0 R]", page, StringComparison.Ordinal);
    }

    /// <summary>The body of the last definition of an object, as a reader would resolve it.</summary>
    private static string ObjectBody(string text, int number)
    {
        var marker = number.ToString(CultureInfo.InvariantCulture) + " 0 obj";
        var start = text.LastIndexOf(marker, StringComparison.Ordinal);

        Assert.True(start >= 0, $"object {number} is missing");

        var end = text.IndexOf("endobj", start, StringComparison.Ordinal);

        return text.Substring(start, end - start);
    }

    /// <summary>The bytes of a stream, using the <c>/Length</c> its dictionary declares.</summary>
    private static byte[] StreamBytes(string body)
    {
        var lengthAt = body.LastIndexOf("/Length ", StringComparison.Ordinal) + "/Length ".Length;
        var length = int.Parse(
            body.AsSpan(lengthAt, body.IndexOf('>', lengthAt) - lengthAt).Trim(),
            CultureInfo.InvariantCulture);

        var start = body.IndexOf("stream\n", StringComparison.Ordinal) + "stream\n".Length;

        return Encoding.Latin1.GetBytes(body.Substring(start, length));
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var inflate = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);

        return output.ToArray();
    }
}
