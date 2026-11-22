using System;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Xlsx;
using Xunit;

namespace M3scalinum.Text.Xlsx.Tests;

/// <summary>From a workbook to Typst source, and from Typst source to a PDF.</summary>
/// <remarks>
/// The last test drives the real compiler: it reads the fixture, converts it, compiles the pages and
/// checks the bytes are a PDF. That is the whole claim of the package in one test — an invoice typed
/// into a spreadsheet renders without Excel and without the Open XML SDK. It skips itself when the
/// compiler is not on the host, which is what the `extras` job of `ci-text.yml` provides.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class XlsxTypstTests
{
    private const string CompilerSkipReason =
        "set SHARPTYPST_NATIVE_PATH to native/sharp-typst/target/release/sharp_typst.dll " +
        "(or libsharp_typst.so) to run the render test";

    [Fact]
    public void A_sheet_becomes_a_typst_table()
    {
        var markup = XlsxReaderTests.Invoice().ToTypstDocument().ToMarkup();

        Assert.Contains("table(", markup, StringComparison.Ordinal);
        Assert.Contains(XlsxReaderTests.Title, markup, StringComparison.Ordinal);
        Assert.Contains(XlsxReaderTests.ClientName, markup, StringComparison.Ordinal);
        Assert.Contains("1200", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sheet_alone_carries_no_heading()
    {
        var markup = XlsxReaderTests.Invoice().ToTypstDocument().ToMarkup();

        Assert.DoesNotContain("= Facture", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_workbook_names_each_sheet_above_its_table()
    {
        var markup = XlsxReaderTests.Read(XlsxReaderTests.Annexed()).ToTypstDocument().ToMarkup();

        Assert.Contains($"= {XlsxReaderTests.InvoiceName}", markup, StringComparison.Ordinal);
        Assert.Contains($"= {XlsxReaderTests.AnnexName}", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_sheet_renders_no_table()
    {
        var workbook = XlsxReaderTests.Read(new XlsxFixture().EmptySheet("Vide").Build());

        var markup = workbook.ToTypstDocument().ToMarkup();

        Assert.DoesNotContain("table(", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_formula_cell_shows_the_value_the_workbook_cached()
    {
        var markup = XlsxReaderTests.Invoice().ToTypstDocument().ToMarkup();

        Assert.DoesNotContain("=B3+B4", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invoice_read_from_a_spreadsheet_renders_to_a_real_pdf()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);
        Assert.SkipUnless(XlsxNativeProbe.CompilerAvailable, CompilerSkipReason);

        var workbook = XlsxReaderTests.Read(XlsxReaderTests.Annexed());
        var markup = workbook.ToTypstDocument().ToMarkup();

        var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();

        try
        {
            using var result = new TypstCompiler().Compile(
                world, new TypstCompilationOptions { DocumentKind = TypstDocumentKind.Paged });

            var pdf = result.ExportPdf();

            Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
            Assert.True(pdf.Length > 1_000, "a one-page invoice is larger than a kilobyte");
            Assert.Equal(1, result.PageCount);
        }
        finally
        {
            world.Dispose();
        }
    }
}
