using System;
using System.Linq;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Pdf;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Diagnostics;
using M3scalinum.Text.Typst.Export;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// The accessibility arguments the model emits have to survive the whole pipeline: markup, layout
/// and PDF tagging. <see cref="PdfConformance.CheckAccessibility"/> reads the tagged structure of
/// the exported bytes, which is the part a markup assertion cannot see.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ModelAccessibilityPdfTests
{
    /// <summary>A 1×1 transparent PNG, so the figure resolves without a fixture on disk.</summary>
    private static readonly byte[] Pixel =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    [Fact]
    public void A_model_built_document_passes_the_accessibility_checks_the_model_can_state()
    {
        var document = TypstDocument.Create()
            .Metadata(m => m.Title("Accessible report").Language("en"))
            .Heading(1, "Revenue")
            .Figure(f => f
                .Image("pixel.png")
                .Alt("A single pixel standing in for the revenue chart.")
                .WithCaption("Revenue by quarter"))
            .Table(t => t
                .Columns(TypstLength.Fr(1), TypstLength.Fr(2))
                .HeaderRow(
                    new TypstTableCell("Quarter") { HeaderScope = TypstHeaderScope.Both },
                    new TypstTableCell("Revenue"))
                .Row(
                    new TypstTableCell("Q1") { HeaderScope = TypstHeaderScope.Row },
                    new TypstTableCell("1 200")));

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .AddAsset("pixel.png", Pixel)
            .Build();

        using var result = new TypstCompiler().Compile(world);

        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == TypstSeverity.Error);

        var pdf = result.ExportPdf(new PdfExportOptions { Standards = [PdfStandard.PdfUa1] });
        var codes = PdfConformance
            .CheckAccessibility(pdf)
            .Issues
            .Select(issue => issue.Code)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(PdfConformanceCodes.FigureAlternateTextMissing, codes);
        Assert.DoesNotContain(PdfConformanceCodes.TableHeaderMissing, codes);
        Assert.DoesNotContain(PdfConformanceCodes.TableHeaderScopeMissing, codes);
        Assert.DoesNotContain(PdfConformanceCodes.LanguageMissing, codes);
    }
}
