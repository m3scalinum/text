using System;
using System.IO;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.FactureX.Tests;

/// <summary>
/// The profile decides two strings that must agree: the guideline URN in the XML and the
/// conformance level in the XMP. A mismatch is a document a receiver rejects, so every value is
/// pinned in both spellings.
/// </summary>
public sealed class ProfileTests
{
    [Theory]
    [InlineData(FacturXProfile.Minimum, "MINIMUM", "urn:factur-x.eu:1p0:minimum")]
    [InlineData(FacturXProfile.BasicWithoutLines, "BASIC WL", "urn:factur-x.eu:1p0:basicwl")]
    [InlineData(FacturXProfile.Basic, "BASIC", "urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic")]
    [InlineData(FacturXProfile.En16931, "EN 16931", "urn:cen.eu:en16931:2017")]
    [InlineData(FacturXProfile.Extended, "EXTENDED", "urn:cen.eu:en16931:2017#conformant#urn:factur-x.eu:1p0:extended")]
    public void Each_profile_has_its_two_wire_spellings(FacturXProfile profile, string level, string urn)
    {
        Assert.Equal(level, profile.ToConformanceLevel());
        Assert.Equal(urn, profile.ToGuidelineUrn());
    }

    [Theory]
    [InlineData("MINIMUM", FacturXProfile.Minimum)]
    [InlineData("BASIC WL", FacturXProfile.BasicWithoutLines)]
    [InlineData("BASIC", FacturXProfile.Basic)]
    [InlineData("EN 16931", FacturXProfile.En16931)]
    [InlineData("EXTENDED", FacturXProfile.Extended)]
    [InlineData("urn:factur-x.eu:1p0:minimum", FacturXProfile.Minimum)]
    [InlineData("urn:factur-x.eu:1p0:basicwl", FacturXProfile.BasicWithoutLines)]
    [InlineData("urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic", FacturXProfile.Basic)]
    [InlineData("urn:cen.eu:en16931:2017", FacturXProfile.En16931)]
    [InlineData("urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:comfort", FacturXProfile.En16931)]
    [InlineData("urn:cen.eu:en16931:2017#conformant#urn:factur-x.eu:1p0:extended", FacturXProfile.Extended)]
    public void Both_spellings_parse_back(string value, FacturXProfile expected)
    {
        Assert.True(FacturXProfiles.TryParse(value, out var profile));
        Assert.Equal(expected, profile);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("COMFORT")]
    [InlineData("urn:something:else")]
    public void An_unknown_value_is_refused(string? value)
    {
        Assert.False(FacturXProfiles.TryParse(value, out _));
    }

    [Fact]
    public void An_undefined_profile_throws_rather_than_guessing()
    {
        var undefined = (FacturXProfile)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => undefined.ToConformanceLevel());
        Assert.Throws<ArgumentOutOfRangeException>(() => undefined.ToGuidelineUrn());
    }
}
