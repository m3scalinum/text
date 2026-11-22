using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The PDF 2.0 / PDF/UA-2 boundary, stated as a test: which levels the embedded compiler can
/// actually produce, and what happens when a document claims one it cannot.
/// </summary>
/// <remarks>
/// <para>
/// The list below is the same table as §8 of <c>docs/specifications/15-pdf-conformance.md</c>, one row
/// per member of <see cref="PdfConformanceStandard"/>, each with the option string
/// <c>native/sharp-typst/src/export.rs</c> maps for it. A member added to the enumeration without a
/// row fails this test — that is the point: the enumeration must not promise a level the producer
/// cannot write, and the specification must not promise a level the enumeration lacks.
/// </para>
/// <para>
/// The evidence lives in the vendored crates, not in this test: <c>typst-pdf 0.15.1</c> declares
/// <c>PdfStandard</c> with one accessibility variant, <c>Ua_1</c>, and
/// <c>krilla 0.8.2</c>'s <c>Accessibility</c> enum has a single value whose maximum PDF version is
/// 1.7. PDF/UA-2 therefore has neither a producer nor a checker here.
/// </para>
/// </remarks>
public sealed class PdfUa2Tests
{
    /// <summary>
    /// Every level <c>typst-pdf 0.15.1</c> can enforce, in the order the crate declares it, with the
    /// name the native bridge parses. Copied from <c>PdfStandard</c> (lib.rs:279-336) and
    /// <c>parse_standard</c> (export.rs:215-233), not invented.
    /// </summary>
    private static readonly (PdfConformanceStandard Level, string Option)[] ProducibleLevels =
    [
        (PdfConformanceStandard.Pdf14, "1.4"),
        (PdfConformanceStandard.Pdf15, "1.5"),
        (PdfConformanceStandard.Pdf16, "1.6"),
        (PdfConformanceStandard.Pdf17, "1.7"),
        (PdfConformanceStandard.Pdf20, "2.0"),
        (PdfConformanceStandard.PdfA1b, "a-1b"),
        (PdfConformanceStandard.PdfA1a, "a-1a"),
        (PdfConformanceStandard.PdfA2b, "a-2b"),
        (PdfConformanceStandard.PdfA2u, "a-2u"),
        (PdfConformanceStandard.PdfA2a, "a-2a"),
        (PdfConformanceStandard.PdfA3b, "a-3b"),
        (PdfConformanceStandard.PdfA3u, "a-3u"),
        (PdfConformanceStandard.PdfA3a, "a-3a"),
        (PdfConformanceStandard.PdfA4, "a-4"),
        (PdfConformanceStandard.PdfA4f, "a-4f"),
        (PdfConformanceStandard.PdfA4e, "a-4e"),
        (PdfConformanceStandard.PdfUa1, "ua-1"),
    ];

    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", name);

        Assert.True(File.Exists(path), $"the fixture {name} is missing from {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>The header bytes of a PDF, as a reader sees them: <c>%PDF-2.0</c> and a line break.</summary>
    private static string Header(byte[] pdf) => Encoding.ASCII.GetString(pdf, 0, 8);

    [Fact]
    public void Every_level_the_enumeration_states_is_a_level_the_embedded_compiler_produces()
    {
        // Both directions: no member without a producer, and no producer the enumeration hides. The
        // compiler exposes 17 standards in typst-pdf 0.15.1; the enumeration must be the same 17.
        var stated = Enum.GetValues<PdfConformanceStandard>().Select(level => level.ToString()).ToList();
        var documented = ProducibleLevels.Select(row => row.Level.ToString()).ToList();

        Assert.Equal(documented.OrderBy(name => name, StringComparer.Ordinal), stated.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(17, documented.Count);
        Assert.Equal(documented.Count, documented.Distinct(StringComparer.Ordinal).Count());

        foreach (var (level, option) in ProducibleLevels)
        {
            Assert.False(string.IsNullOrWhiteSpace(option), $"{level} has no option string");
            Assert.True(Enum.IsDefined(level), $"{level} is not a member of the enumeration");
        }
    }

    [Fact]
    public void The_enumeration_has_no_PDF_UA_2_member_because_the_compiler_has_no_UA_2_flavour()
    {
        // typst-pdf 0.15.1's PdfStandard enum ends at Ua_1 (lib.rs:334-336) and krilla 0.8.2's
        // Accessibility enum has only UA1 (validate.rs:1090). A member here would be a value the
        // export rejects, so none exists — and this test is what keeps it that way.
        Assert.DoesNotContain(
            Enum.GetNames<PdfConformanceStandard>(),
            name => name.Contains("Ua2", StringComparison.Ordinal) || name.Contains("Ua_2", StringComparison.Ordinal));

        Assert.DoesNotContain(
            ProducibleLevels,
            row => row.Option.Contains("ua-2", StringComparison.Ordinal));
    }

    [Fact]
    public void The_compiler_PDF_2_0_output_is_checked_as_PDF_2_0()
    {
        // a4.pdf is real Typst 0.15.1 output for Standards = [PdfStandard.PdfA4]; PDF/A-4 fixes the
        // PDF 2.0 header, so its bytes prove the producer writes %PDF-2.0 and not a 1.7 header with a
        // /Version promise. PdfConformance reads the same version from the header.
        var pdf = Fixture("a4.pdf");

        Assert.Equal("%PDF-2.0", Header(pdf));

        var report = PdfConformance.Check(pdf, PdfConformanceStandard.Pdf20);

        Assert.True(report.IsConformant, report.ToString());
        Assert.Equal(PdfConformanceStandard.Pdf20, report.Standard);
        Assert.Equal(new PdfVersion(2, 0), report.Version.GetValueOrDefault());
    }

    [Fact]
    public void A_PDF_UA_1_claim_over_the_compiler_PDF_2_0_output_is_reported()
    {
        // PDF/UA-1 is built on ISO 32000-1, so a 2.0 file cannot claim it — which is also why
        // typst-pdf cannot produce UA-1 at 2.0: krilla gives UA1 the range PDF 1.4..=1.7 and refuses
        // the combination (configure/validate.rs:1199, configure/mod.rs:132-140). The checker states
        // the same limit from the bytes.
        var report = PdfConformance.Check(Fixture("a4.pdf"), PdfConformanceStandard.PdfUa1);

        var mismatch = Assert.Single(report.Issues, issue => issue.Code == PdfConformanceCodes.VersionMismatch);

        Assert.Equal("header", mismatch.Target);
        Assert.Equal("PDF/UA-1 requires PDF 1.4 to 1.7, but the file declares 2.0", mismatch.Message);
        Assert.False(report.IsConformant);
    }
}
