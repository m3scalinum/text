using System;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>The wire spellings of <see cref="FacturXProfile"/>.</summary>
public static class FacturXProfiles
{
    /// <summary>The XMP <c>fx:ConformanceLevel</c> value.</summary>
    public static string ToConformanceLevel(this FacturXProfile profile) => profile switch
    {
        FacturXProfile.Minimum => "MINIMUM",
        FacturXProfile.BasicWithoutLines => "BASIC WL",
        FacturXProfile.Basic => "BASIC",
        FacturXProfile.En16931 => "EN 16931",
        FacturXProfile.Extended => "EXTENDED",
        FacturXProfile.ExtendedCtcFr => "EXTENDED",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "unknown profile"),
    };

    /// <summary>
    /// The <c>GuidelineSpecifiedDocumentContextParameter</c> URN the XML carries. CII and UBL use
    /// the same URN, which is what makes the two readable by the same validator.
    /// </summary>
    public static string ToGuidelineUrn(this FacturXProfile profile) => profile switch
    {
        FacturXProfile.Minimum => "urn:factur-x.eu:1p0:minimum",
        FacturXProfile.BasicWithoutLines => "urn:factur-x.eu:1p0:basicwl",
        FacturXProfile.Basic => "urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic",
        FacturXProfile.En16931 => "urn:cen.eu:en16931:2017",
        FacturXProfile.Extended => "urn:cen.eu:en16931:2017#conformant#urn:factur-x.eu:1p0:extended",
        FacturXProfile.ExtendedCtcFr => "urn:cen.eu:en16931:2017#conformant#urn.cpro.gouv.fr:1p0:extended-ctc-fr",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "unknown profile"),
    };

    /// <summary>Reads a profile back from either spelling.</summary>
    public static bool TryParse(string? value, out FacturXProfile profile)
    {
        switch (value)
        {
            case "MINIMUM":
            case "urn:factur-x.eu:1p0:minimum":
                profile = FacturXProfile.Minimum;
                return true;
            case "BASIC WL":
            case "urn:factur-x.eu:1p0:basicwl":
                profile = FacturXProfile.BasicWithoutLines;
                return true;
            case "BASIC":
            case "urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic":
                profile = FacturXProfile.Basic;
                return true;
            case "EN 16931":
            case "urn:cen.eu:en16931:2017":
            case "urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:comfort":
                profile = FacturXProfile.En16931;
                return true;
            case "EXTENDED":
            case "urn:cen.eu:en16931:2017#conformant#urn:factur-x.eu:1p0:extended":
                profile = FacturXProfile.Extended;
                return true;
            case "urn:cen.eu:en16931:2017#conformant#urn.cpro.gouv.fr:1p0:extended-ctc-fr":
                profile = FacturXProfile.ExtendedCtcFr;
                return true;
            default:
                profile = default;
                return false;
        }
    }
}