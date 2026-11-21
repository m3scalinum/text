using System;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The vector half of the package: what a layout engine consumes, asserted as shape rather than as
/// pixels. The SVG is emitted at one unit per module so Typst can scale it without a raster step.
/// </summary>
public sealed class SvgShapeTests
{
    [Theory]
    [InlineData(BarcodeSymbology.QrCode, "SVG")]
    [InlineData(BarcodeSymbology.DataMatrix, "SVG")]
    [InlineData(BarcodeSymbology.Aztec, "SVG")]
    [InlineData(BarcodeSymbology.Pdf417, "SVG")]
    [InlineData(BarcodeSymbology.Code128, "SVG-128")]
    [InlineData(BarcodeSymbology.Code39, "SVG-39")]
    [InlineData(BarcodeSymbology.Ean13, "5901234123457")]
    [InlineData(BarcodeSymbology.Ean8, "96385074")]
    [InlineData(BarcodeSymbology.Itf, "1234567890")]
    [InlineData(BarcodeSymbology.Codabar, "A123456789B")]
    public void The_svg_is_one_path_in_a_viewbox_of_one_unit_per_module(
        BarcodeSymbology symbology, string payload)
    {
        var image = Barcode.Encode(symbology, payload);

        Assert.StartsWith("<svg", image.Svg, StringComparison.Ordinal);
        Assert.EndsWith("</svg>", image.Svg, StringComparison.Ordinal);
        Assert.Equal(1, Count(image.Svg, "<path"));
        Assert.Equal(1, Count(image.Svg, "</svg>"));
        Assert.Contains(
            $"viewBox=\"0 0 {image.ModuleWidth} {image.ModuleHeight}\"",
            image.Svg,
            StringComparison.Ordinal);
        Assert.Contains(
            $"width=\"{image.ModuleWidth}\" height=\"{image.ModuleHeight}\"",
            image.Svg,
            StringComparison.Ordinal);
        Assert.Contains("shape-rendering=\"crispEdges\"", image.Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_run_in_the_path_stays_inside_the_viewbox()
    {
        var image = Barcode.Encode(BarcodeSymbology.QrCode, "BOUNDS");

        var runs = 0;
        foreach (var run in Runs(image))
        {
            Assert.InRange(run.X, 0, image.ModuleWidth - 1);
            Assert.InRange(run.X + run.Length, 1, image.ModuleWidth);
            Assert.InRange(run.Y, 0, image.ModuleHeight - 1);
            runs++;
        }

        Assert.True(runs > 0, "the QR code drew nothing");
    }

    [Fact]
    public void A_qr_code_draws_many_runs_and_a_wide_bar_merges_them()
    {
        // Code 39 draws wide bars, so two dark modules are adjacent and one run covers both: the run
        // count must be lower than the dark-module count, or the renderer is emitting a rectangle per
        // module and the runs mean nothing.
        var image = Barcode.Encode(BarcodeSymbology.Code39, "WIDE-BARS");

        var runs = Count(image.Svg, "M");
        var dark = 0;
        foreach (var row in PathRows(image))
        {
            dark += row;
        }


        Assert.True(runs < dark, $"{runs} runs for {dark} dark modules");
        Assert.True(runs > 0);
    }

    [Fact]
    public void A_square_symbology_produces_a_square_viewbox()
    {
        var image = Barcode.Encode(BarcodeSymbology.QrCode, "SQUARE");

        Assert.Equal(image.ModuleWidth, image.ModuleHeight);
        Assert.Equal(1.0, image.AspectRatio, 3);
    }

    [Fact]
    public void A_one_dimensional_symbol_is_a_wide_strip()
    {
        var image = Barcode.Encode(BarcodeSymbology.Code128, "STRIP");

        Assert.True(image.AspectRatio > 2, $"aspect ratio {image.AspectRatio}");
        Assert.Equal(40, image.ModuleHeight);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    /// <summary>One <c>M{x} {y}h{length}v1h-{length}z</c> instruction of the path.</summary>
    private readonly record struct Run(int X, int Y, int Length);

    /// <summary>Reads every run the renderer wrote, in the order it wrote them.</summary>
    private static System.Collections.Generic.List<Run> Runs(BarcodeImage image)
    {
        var runs = new System.Collections.Generic.List<Run>();
        var path = image.Svg[(image.Svg.IndexOf(" d=\"", StringComparison.Ordinal) + 4)..];

        foreach (var instruction in path.Split('M', StringSplitOptions.RemoveEmptyEntries))
        {
            // "{x} {y}h{length}v1h-{length}z"
            var separator = instruction.IndexOf(' ');
            var x = int.Parse(
                instruction[..separator], System.Globalization.CultureInfo.InvariantCulture);

            var remainder = instruction[(separator + 1)..];
            var h = remainder.IndexOf('h', StringComparison.Ordinal);
            var y = int.Parse(
                remainder[..h], System.Globalization.CultureInfo.InvariantCulture);

            var afterH = remainder[(h + 1)..];
            var length = int.Parse(
                afterH[..afterH.IndexOf('v', StringComparison.Ordinal)],
                System.Globalization.CultureInfo.InvariantCulture);

            runs.Add(new Run(x, y, length));
        }

        return runs;
    }

    /// <summary>Replays the path and returns the dark-module count per row.</summary>
    private static int[] PathRows(BarcodeImage image)
    {
        var rows = new int[image.ModuleHeight];

        foreach (var run in Runs(image))
        {
            rows[run.Y] += run.Length;
        }

        return rows;
    }
}
