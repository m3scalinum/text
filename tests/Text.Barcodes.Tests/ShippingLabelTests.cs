using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Barcodes;
using M3scalinum.Text.Pdf;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The logistics label: an SSCC in a GS1-128 symbol, the same number printed, an address and a
/// reference, on one page of a PDF.
/// </summary>
/// <remarks>
/// <para>
/// The proof the item asks for is the last one: the PDF the package produces is rendered back to
/// pixels and read by the package's own decoder, which must return the eighteen digits of the SSCC,
/// check digit included. That needs <c>M3scalinum.Text.Pdf</c>'s rasteriser — a native library, and
/// only this test project references it — so the test skips where it is not built, and every other
/// test here runs without it.
/// </para>
/// <para>
/// The sample under <c>samples/labels/sscc-label.json</c> is read by one of these tests on purpose:
/// it is the file a reader copies, and a sample that no longer builds is worse than none.
/// </para>
/// </remarks>
public sealed class ShippingLabelTests
{
    /// <summary>106141411234567897: GS1 extension digit 1, sixteen digits of body, check digit 7.</summary>
    private const string Sscc = "106141411234567897";

    private const string SsccWithABadCheckDigit = "106141411234567898";

    private const string SkipReason =
        "the PDF rasteriser's native library is not available; set SHARPTEXTPDF_NATIVE_PATH";

    private static readonly Lazy<bool> Renderer = new(Probe);

    private static bool RendererAvailable => Renderer.Value;

    [Fact]
    public void A_label_prints_the_sscc_as_a_gs1_128_symbol_and_in_its_printed_form()
    {
        var label = Create();

        Assert.Equal(Sscc, label.Sscc);
        Assert.Equal("(00)" + Sscc, label.HumanReadable);
        Assert.Equal(BarcodeSymbology.Gs1_128, label.Code.Symbology);
        Assert.Equal(ShippingLabelFormat.Millimetres100x150, label.Format);
        Assert.Equal(100, label.WidthMillimetres);
        Assert.Equal(150, label.HeightMillimetres);

        // The nominal GS1 module, 0.495 mm, on whole dots at 300 dpi: six dots, 0.508 mm.
        Assert.Equal(0.508, label.Code.ModuleMillimetres, 3);
        Assert.InRange(label.Code.WidthMillimetres, 60, 90);
        Assert.InRange(label.Code.HeightMillimetres, 30, 34);
    }

    [Fact]
    public void The_symbol_on_the_label_decodes_back_to_the_eighteen_digits()
    {
        var label = Create();

        var decoded = Barcode.Decode(label.Code.Png);

        Assert.True(decoded.Success, decoded.Error);
        var data = Gs1Data.Parse(decoded.Text);
        var element = Assert.Single(data.Elements);
        Assert.Equal("00", element.Ai);
        Assert.Equal(Sscc, element.Value);
        Assert.Equal(18, element.Value.Length);
    }

    [Fact]
    public void The_rendered_pdf_carries_a_symbol_that_decodes_to_the_sscc()
    {
        Assert.SkipUnless(RendererAvailable, SkipReason);

        var label = Create();

        var page = PdfRaster.Render(label.Pdf, 1, new PdfRasterOptions { Dpi = 300 });
        var decoded = Barcode.DecodeAll(page.Bytes);

        Assert.True(decoded.Success, decoded.Error);
        var symbol = Assert.Single(decoded.Symbols);

        // The reader names a GS1-128 from its `]C1` identifier: parse what came back rather than
        // assuming the prefix was stripped, and read the SSCC out of the element string.
        var data = Gs1Data.Parse(symbol.Text);
        var element = Assert.Single(data.Elements);
        Assert.Equal("00", element.Ai);
        Assert.Equal(Sscc, element.Value);
        Assert.Equal(18, element.Value.Length);
    }

    [Fact]
    public void The_page_is_the_format_it_declares()
    {
        var a6 = ShippingLabel.Create(Sscc, new ShippingLabelOptions
        {
            Format = ShippingLabelFormat.A6,
            AddressLines = ["1 Test Street"],
        });

        Assert.Equal(105, a6.WidthMillimetres);
        Assert.Equal(148, a6.HeightMillimetres);

        var mediaBox = Encoding.ASCII.GetString(a6.Pdf);
        Assert.Contains("/MediaBox [0 0 297.638 419.528]", mediaBox, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_label_renders_to_the_same_bytes()
    {
        Assert.Equal(Create().Pdf, Create().Pdf);
    }

    [Fact]
    public void An_sscc_with_the_wrong_check_digit_names_the_digit_it_ends_in_and_the_one_it_needs()
    {
        var error = Assert.Throws<BarcodeGs1Exception>(() => ShippingLabel.Create(
            SsccWithABadCheckDigit,
            new ShippingLabelOptions { AddressLines = ["1 Test Street"] }));

        Assert.Contains("ends in check digit 8", error.Message, StringComparison.Ordinal);
        Assert.Contains("needs 7", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10614141123456789")]   // seventeen digits
    [InlineData("1061414112345678977")] // nineteen
    [InlineData("10614141123456789A")]  // not a digit
    public void An_sscc_that_is_not_eighteen_digits_is_refused(string sscc)
    {
        var error = Assert.Throws<BarcodeGs1Exception>(
            () => ShippingLabel.Create(sscc, new ShippingLabelOptions { AddressLines = ["1 Test Street"] }));

        Assert.Contains("eighteen digits", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_without_an_address_is_refused()
    {
        var error = Assert.Throws<BarcodeException>(() => ShippingLabel.Create(Sscc));

        Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        Assert.Contains("address", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gs1_string_without_an_sscc_is_refused_by_identifier()
    {
        var data = Gs1Data.Parse("0109501101530003");

        var error = Assert.Throws<BarcodeGs1Exception>(() => ShippingLabel.Create(
            data, new ShippingLabelOptions { AddressLines = ["1 Test Street"] }));

        Assert.Contains("AI 00", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_address_is_written_in_windows_1252_so_an_accent_survives()
    {
        var label = ShippingLabel.Create(Sscc, new ShippingLabelOptions
        {
            AddressLines = ["12 rue de l'Église", "75002 Paris"],
        });

        // É is 0xC9 in Windows-1252; a literal string escapes high bytes as octal, so the address
        // in the content stream reads "l'\311glise". A reader with the declared encoding prints it.
        var pdf = Encoding.ASCII.GetString(label.Pdf);
        Assert.Contains(@"l'\311glise", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sample_file_still_builds_a_label()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Root(), "samples", "labels", "sscc-label.json")));
        var sample = document.RootElement;

        var addressLines = new List<string>();
        foreach (var line in sample.GetProperty("addressLines").EnumerateArray())
        {
            addressLines.Add(line.GetString()!);
        }

        var label = ShippingLabel.Create(
            sample.GetProperty("sscc").GetString()!,
            new ShippingLabelOptions
            {
                Format = Enum.Parse<ShippingLabelFormat>(sample.GetProperty("format").GetString()!),
                Recipient = sample.GetProperty("recipient").GetString(),
                AddressLines = addressLines,
                Reference = sample.GetProperty("reference").GetString(),
            });

        Assert.Equal(sample.GetProperty("sscc").GetString(), label.Sscc);
        Assert.StartsWith("(00)", label.HumanReadable, StringComparison.Ordinal);
        Assert.True(label.Pdf.Length > 1024, $"the sample produced a {label.Pdf.Length}-byte PDF");
    }

    [Fact]
    public void An_option_out_of_range_is_refused_before_anything_is_encoded()
    {
        var bad = new[]
        {
            new ShippingLabelOptions { AddressLines = ["1 Test Street"], Dpi = 10 },
            new ShippingLabelOptions { AddressLines = ["1 Test Street"], ModuleMillimetres = 0 },
            new ShippingLabelOptions { AddressLines = ["1 Test Street"], BarHeightModules = 0 },
            new ShippingLabelOptions { AddressLines = ["1 Test Street"], Format = (ShippingLabelFormat)99 },
            new ShippingLabelOptions { AddressLines = [""] },
            new ShippingLabelOptions { AddressLines = ["1", "2", "3", "4", "5", "6"] },
            new ShippingLabelOptions { AddressLines = [new string('A', 43)] },
            new ShippingLabelOptions { AddressLines = ["1 Test Street"], Recipient = new string('B', 43) },
            new ShippingLabelOptions { AddressLines = ["1 Test Street"], Reference = new string('C', 33) },
            new ShippingLabelOptions { AddressLines = ["1 Test Street"], Reference = "LS\u0001" },
        };

        foreach (var options in bad)
        {
            var error = Assert.Throws<BarcodeException>(() => ShippingLabel.Create(Sscc, options));
            Assert.Equal(BarcodeErrorCode.InvalidArgument, error.Code);
        }
    }

    [Fact]
    public void A_module_the_page_cannot_hold_is_refused_with_both_widths()
    {
        var error = Assert.Throws<BarcodeException>(() => ShippingLabel.Create(Sscc, new ShippingLabelOptions
        {
            AddressLines = ["1 Test Street"],
            ModuleMillimetres = 1.2,
        }));

        Assert.Contains("leaves 92 mm", error.Message, StringComparison.Ordinal);
        Assert.Contains("ModuleMillimetres", error.Message, StringComparison.Ordinal);
    }

    private static ShippingLabel Create() => ShippingLabel.Create(Sscc, new ShippingLabelOptions
    {
        Recipient = "Camille Dubois",
        AddressLines = ["12 rue de la Paix", "75002 Paris", "France"],
        Reference = "LS-2026-000481",
    });

    private static bool Probe()
    {
        try
        {
            _ = PdfRaster.PageCount(MinimalPdf.WithImage(new GreyImage(4, 4)));
            return true;
        }
        catch (Exception error) when (error is DllNotFoundException or TypeInitializationException)
        {
            return false;
        }
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "samples")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"could not find samples/ above {AppContext.BaseDirectory}");
    }
}
