using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Rendering a page as a JPEG, and what that buys over the PNG.
/// </summary>
/// <remarks>
/// <para>
/// The tests that render need the native library and skip with a message naming
/// <c>SHARPTEXTPDF_NATIVE_PATH</c> where it is not present; the argument checks need no library at
/// all. The figures the specification quotes are printed by
/// <see cref="Jpeg_is_the_smaller_encoding_of_a_scanned_page_at_the_default_quality"/> and by the
/// crate's own <c>cargo test</c>, which encodes the same pixels through the same encoder.
/// </para>
/// <para>
/// The fixture is a scanned page — a full-page image at the page's own resolution, with grain —
/// rather than the one-line page the other raster tests use. That is the content the format is for:
/// a page of flat vector art is the one case where the PNG is already the smaller file, and the
/// specification says so instead of pretending otherwise.
/// </para>
/// </remarks>
public sealed class PdfRasterJpegTests
{
    private const int A4WidthPoints = 595;
    private const int A4HeightPoints = 842;

    /// <summary>The resolution the measured figures use: A4 is 1239×1754 pixels there.</summary>
    private const int MeasuredDpi = 150;

    private readonly ITestOutputHelper _output;

    public PdfRasterJpegTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Jpeg_is_the_smaller_encoding_of_a_scanned_page_at_the_default_quality()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var pdf = ScannedPage();

        var png = PdfRaster.Render(pdf, 1, new PdfRasterOptions { Dpi = MeasuredDpi });
        var jpeg = PdfRaster.Render(
            pdf,
            1,
            new PdfRasterOptions { Dpi = MeasuredDpi, Format = PdfPageImageFormat.Jpeg });

        _output.WriteLine($"page {png.Width}x{png.Height}: png={png.Bytes.Length} jpeg={jpeg.Bytes.Length}");

        Assert.Equal(PdfPageImageFormat.Png, png.Format);
        Assert.Equal(PdfPageImageFormat.Jpeg, jpeg.Format);
        Assert.Equal((png.Width, png.Height), (jpeg.Width, jpeg.Height));
        Assert.True(
            jpeg.Bytes.Length * 4 < png.Bytes.Length,
            $"the JPEG is {jpeg.Bytes.Length} bytes and the PNG {png.Bytes.Length}: not a quarter");
    }

    [Fact]
    public void A_higher_quality_is_a_larger_file()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var pdf = ScannedPage();

        var lowest = PdfRaster.Render(
            pdf,
            1,
            new PdfRasterOptions
            {
                Dpi = MeasuredDpi,
                Format = PdfPageImageFormat.Jpeg,
                Quality = PdfRasterOptions.MinimumQuality,
            });
        var highest = PdfRaster.Render(
            pdf,
            1,
            new PdfRasterOptions
            {
                Dpi = MeasuredDpi,
                Format = PdfPageImageFormat.Jpeg,
                Quality = PdfRasterOptions.MaximumQuality,
            });

        _output.WriteLine($"jpeg1={lowest.Bytes.Length} jpeg100={highest.Bytes.Length}");

        Assert.True(
            highest.Bytes.Length > lowest.Bytes.Length,
            $"quality 100 gave {highest.Bytes.Length} bytes, quality 1 gave {lowest.Bytes.Length}");
    }

    [Fact]
    public void The_format_asked_for_is_the_format_of_the_bytes()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var image = PdfRaster.Render(
            ScannedPage(),
            1,
            new PdfRasterOptions { Format = PdfPageImageFormat.Jpeg });

        // SOI, then an APP0 segment and a JFIF identifier: a JPEG, not a PNG with another name.
        Assert.Equal(PdfPageImageFormat.Jpeg, image.Format);
        Assert.Equal([0xFF, 0xD8], image.Bytes[..2]);
        Assert.Equal([0xFF, 0xE0], image.Bytes[2..4]);
        Assert.Equal("JFIF", Encoding.ASCII.GetString(image.Bytes, 6, 4));
        Assert.Equal([0xFF, 0xD9], image.Bytes[^2..]);
        Assert.NotEqual(0x89, image.Bytes[0]);
    }

    [Fact]
    public void The_jpeg_decodes_to_the_declared_pixel_size_in_rgb()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var image = PdfRaster.Render(
            ScannedPage(),
            1,
            new PdfRasterOptions { Dpi = 144, Format = PdfPageImageFormat.Jpeg });

        var (width, height, components) = JpegSize(image.Bytes);

        Assert.Equal(image.Width, width);
        Assert.Equal(image.Height, height);
        // Three components: Y, Cb, Cr. A JPEG with one would be a greyscale encoding, which is not
        // what was asked for and not what the raster holds.
        Assert.Equal(3, components);
    }

    [Fact]
    public void The_jpeg_of_the_dense_text_page_is_still_a_page()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var image = PdfRaster.Render(
            DenseTextPage(),
            1,
            new PdfRasterOptions { Format = PdfPageImageFormat.Jpeg, Quality = 85 });

        var (width, height, components) = JpegSize(image.Bytes);

        Assert.Equal(PdfPageImageFormat.Jpeg, image.Format);
        Assert.Equal(image.Width, width);
        Assert.Equal(image.Height, height);
        Assert.Equal(3, components);
        Assert.Equal(
            (A4WidthPoints * PdfRasterOptions.DefaultDpi / 72),
            image.Width);
    }

    [Fact]
    public void A_quality_outside_the_range_is_refused_before_the_library_is_loaded()
    {
        foreach (var quality in new[] { 0, PdfRasterOptions.MaximumQuality + 1 })
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => PdfRaster.Render(
                    TestPdf.WithPages(1),
                    1,
                    new PdfRasterOptions { Format = PdfPageImageFormat.Jpeg, Quality = quality }));

            Assert.Equal("Quality", exception.ParamName);
        }

        // The default is inside the range, and is the documented one.
        Assert.Equal(85, new PdfRasterOptions().Quality);
    }

    [Fact]
    public void An_unknown_format_is_refused_before_the_library_is_loaded()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfRaster.Render(
                TestPdf.WithPages(1),
                1,
                new PdfRasterOptions { Format = (PdfPageImageFormat)99 }));

        Assert.Equal("Format", exception.ParamName);
    }

    [Fact]
    public void A_png_is_still_what_a_call_with_no_options_produces()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var image = PdfRaster.Render(TestPdf.WithPages(1), 1);

        Assert.Equal(PdfPageImageFormat.Png, image.Format);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], image.Bytes[..4]);
    }

    [Fact]
    public void An_encrypted_document_is_refused_whatever_the_format()
    {
        if (!RasterProbe.IsAvailable)
        {
            Assert.Skip(RasterProbe.SkipReason);
        }

        var path = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "encryption", "qpdf-r6-user-password.pdf");
        var pdf = File.ReadAllBytes(path);

        var exception = Assert.Throws<PdfRasterException>(
            () => PdfRaster.Render(
                pdf, 1, new PdfRasterOptions { Format = PdfPageImageFormat.Jpeg }));

        Assert.Equal(PdfRasterError.Encrypted, exception.Error);
        Assert.Contains("PdfEncryption.Open", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the width, the height and the component count out of a JPEG's frame header.
    /// </summary>
    /// <remarks>
    /// No image library is referenced by this project, and one is not worth adding to check a file
    /// this package wrote: a baseline JPEG states its size in the `SOFn` segment, and walking the
    /// markers to it is twenty lines that also prove the segment chain is well formed.
    /// </remarks>
    private static (int Width, int Height, int Components) JpegSize(byte[] jpeg)
    {
        Assert.True(jpeg.Length > 4);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);

        var index = 2;

        while (index < jpeg.Length - 3)
        {
            Assert.Equal(0xFF, jpeg[index]);

            var marker = jpeg[index + 1];

            // Standalone markers carry no length: TEM, RST0-RST7, SOI, EOI.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD9)
            {
                index += 2;
                continue;
            }

            var length = (jpeg[index + 2] << 8) | jpeg[index + 3];
            Assert.True(length >= 8, $"segment {marker:X2} declares a length of {length}");

            // SOF0 to SOF15, except DHT (C4), JPG (C8) and DAC (CC), which share the range.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                var height = (jpeg[index + 5] << 8) | jpeg[index + 6];
                var width = (jpeg[index + 7] << 8) | jpeg[index + 8];
                var components = jpeg[index + 9];

                return (width, height, components);
            }

            index += 2 + length;
        }

        throw new InvalidOperationException("the bytes hold no JPEG frame header");
    }

    /// <summary>
    /// A one-page A4 PDF whose page is a full-page image at the page's own resolution, with grain: a
    /// scanned page.
    /// </summary>
    /// <remarks>
    /// The image is an uncompressed <c>DeviceRGB</c> XObject, which is legal PDF and keeps the
    /// fixture deterministic: the same pixels are handed to both encoders, so the two sizes are a
    /// measurement of the encoding and not of the fixture.
    /// </remarks>
    private static byte[] ScannedPage()
    {
        const int width = 1240;
        const int height = 1754;

        var samples = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Film and sensor grain: fine, low-amplitude noise a lossless encoder pays for and a
                // DCT attenuates to nothing.
                var grain = (int)(unchecked((uint)x * 2_654_435_761u) ^ (uint)(y * 40_503)) % 13;
                var offset = (y * width + x) * 3;

                samples[offset] = Channel(x, y, 41.0, 67.0, grain - 6);
                samples[offset + 1] = Channel(x, y, 53.0, 53.0, grain - 6);
                samples[offset + 2] = Channel(x, y, 71.0, 37.0, grain - 6);
            }
        }

        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} "
            + $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {samples.Length} >>\nstream\n");

        using var builder = new PdfBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [4 0 R] /Count 1 >>");
        builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        builder.Add(
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 6 0 R "
            + "/Resources << /Font << /F1 3 0 R >> /XObject << /Im0 5 0 R >> >> >>");
        builder.Add(Encoding.Latin1.GetBytes(header), samples);
        builder.Add(Stream("q 595 0 0 842 0 0 cm /Im0 Do Q\n"));

        return builder.Build();
    }

    /// <summary>One channel of the scanned fixture: two interfering waves, one of them diagonal.</summary>
    private static byte Channel(int x, int y, double periodX, double periodY, int grain)
    {
        var wave = 128.0 + (90.0 * Math.Sin(x / periodX) * Math.Cos(y / periodY));
        return (byte)Math.Clamp(Math.Round(wave + grain), 0.0, 255.0);
    }

    /// <summary>A one-page A4 PDF carrying 62 lines of 9 pt text, which is a page of vector art.</summary>
    private static byte[] DenseTextPage()
    {
        using var builder = new PdfBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [4 0 R] /Count 1 >>");
        builder.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        builder.Add(
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 5 0 R "
            + "/Resources << /Font << /F1 3 0 R >> >> >>");
        builder.Add(Stream(TextOps()));

        return builder.Build();
    }

    /// <summary>The text block: 62 lines of about a hundred characters, each one different.</summary>
    private static string TextOps()
    {
        string[] words =
        [
            "the", "rasteriser", "renders", "a", "page", "at",
            "the", "resolution", "the", "caller", "asks", "for",
        ];

        var content = new StringBuilder("BT /F1 9 Tf 11 TL 40 770 Td\n");

        for (var line = 0; line < 62; line++)
        {
            var text = new StringBuilder();
            var word = line * 5;

            while (text.Length < 108)
            {
                text.Append(words[word % words.Length]).Append(' ');
                word += 3;
            }

            content.Append('(').Append(text.ToString().TrimEnd()).Append(") Tj T*\n");
        }

        return content.Append("ET\n").ToString();
    }

    /// <summary>A content stream object body: a stream whose length is the byte count of its data.</summary>
    private static byte[] Stream(string content) =>
        Encoding.Latin1.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream"));

    /// <summary>
    /// Assembles objects into a PDF with a classic cross-reference table whose offsets are the real
    /// ones: a table that points one byte off is a document some readers accept and others refuse,
    /// which is the worst kind of fixture.
    /// </summary>
    private sealed class PdfBuilder : IDisposable
    {
        private readonly MemoryStream _stream = new();
        private readonly List<long> _offsets = [];

        public PdfBuilder() => _stream.Write("%PDF-1.7\n"u8);

        public void Dispose() => _stream.Dispose();

        /// <summary>Adds an object whose body is text.</summary>
        public void Add(string body) => Add(Encoding.Latin1.GetBytes(body));

        /// <summary>Adds an object whose body is bytes, which a binary stream needs.</summary>
        public void Add(byte[] body) => Add(body, []);

        /// <summary>Adds an object made of a text body followed by raw bytes, for an image stream.</summary>
        public void Add(byte[] body, ReadOnlySpan<byte> data)
        {
            _offsets.Add(_stream.Length);

            var number = _offsets.Count.ToString(CultureInfo.InvariantCulture);
            _stream.Write(Encoding.Latin1.GetBytes($"{number} 0 obj\n"));
            _stream.Write(body);
            _stream.Write(data);
            _stream.Write("\nendobj\n"u8);
        }

        public byte[] Build()
        {
            var xref = _stream.Length;
            var size = _offsets.Count + 1;

            _stream.Write(Encoding.Latin1.GetBytes($"xref\n0 {size}\n0000000000 65535 f \n"));

            foreach (var offset in _offsets)
            {
                _stream.Write(Encoding.Latin1.GetBytes(
                    offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
            }

            _stream.Write(Encoding.Latin1.GetBytes(
                $"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));

            return _stream.ToArray();
        }
    }
}
