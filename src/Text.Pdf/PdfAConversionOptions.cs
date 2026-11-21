using System;

namespace M3scalinum.Text.Pdf;

/// <summary>What a conversion to PDF/A or PDF/UA should produce.</summary>
/// <remarks>
/// <para>
/// <see cref="Target"/> must be a PDF/A or a PDF/UA flavour: converting to plain PDF 1.7 or 2.0 has
/// nothing to declare, so it is rejected as a mistake rather than performed as a no-op.
/// </para>
/// <para>
/// <see cref="IccProfile"/> is required by the PDF/A flavours that demand an output intent, and
/// optional for PDF/A-4 and PDF/UA-1. This package ships no ICC profile: an sRGB profile is not
/// something a library may redistribute without its licence, so the caller supplies the bytes and
/// answers for them.
/// </para>
/// </remarks>
public sealed class PdfAConversionOptions
{
    /// <summary>The standard the result must claim. Defaults to PDF/A-2b.</summary>
    public PdfConformanceStandard Target { get; init; } = PdfConformanceStandard.PdfA2b;

    /// <summary>
    /// The ICC profile written as the destination profile of the document's output intent, as whole
    /// profile bytes. Required for PDF/A-1, PDF/A-2 and PDF/A-3.
    /// </summary>
    /// <remarks>
    /// The profile is written unchanged: the converter reads its data colour space from the
    /// <c>RGB </c>, <c>CMYK</c> or <c>GRAY</c> signature at byte 16 and refuses anything else. An
    /// sRGB profile is the usual choice, and the caller is responsible for having the right to
    /// embed it.
    /// </remarks>
    public byte[]? IccProfile { get; init; }

    /// <summary>
    /// The <c>/OutputConditionIdentifier</c> of the output intent. Defaults to <c>Custom</c>, which
    /// is what a caller that does not know the printing condition should declare.
    /// </summary>
    public string? OutputConditionIdentifier { get; init; }

    /// <summary>
    /// The natural language written to the catalog as <c>/Lang</c> when the document declares none,
    /// as a BCP 47 tag such as <c>fr</c> or <c>fr-FR</c>. Left alone when the document already has
    /// one.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>Whether the target fixes a version that needs an output intent.</summary>
    private bool RequiresIccProfile
        => Target is PdfConformanceStandard.PdfA1a
            or PdfConformanceStandard.PdfA1b
            or PdfConformanceStandard.PdfA2a
            or PdfConformanceStandard.PdfA2b
            or PdfConformanceStandard.PdfA2u
            or PdfConformanceStandard.PdfA3a
            or PdfConformanceStandard.PdfA3b
            or PdfConformanceStandard.PdfA3u;

    /// <summary>Checks the options and returns what the target requires.</summary>
    /// <exception cref="ArgumentException">
    /// The target is not an archive or accessibility flavour, an ICC profile the target needs was not
    /// supplied, or the profile supplied is not an ICC profile this converter can describe.
    /// </exception>
    internal PdfStandardRequirements Validate()
    {
        var requirements = PdfStandardRequirements.Of(Target);

        if (requirements.ArchivePart is null && Target != PdfConformanceStandard.PdfUa1)
        {
            throw new ArgumentException(
                $"{Target} is not a PDF/A or PDF/UA flavour, so a conversion to it would claim nothing",
                nameof(Target));
        }

        if (IccProfile is { Length: > 0 } profile)
        {
            PdfAConverter.ColourSpace(profile);
        }
        else if (RequiresIccProfile)
        {
            throw new ArgumentException(
                $"{requirements.Name} requires an /OutputIntent, and this package carries no ICC profile to put in it: "
                + $"pass the profile's bytes in {nameof(IccProfile)}",
                nameof(IccProfile));
        }

        return requirements;
    }
}
