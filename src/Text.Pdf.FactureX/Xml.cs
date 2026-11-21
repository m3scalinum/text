using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Namespace-agnostic navigation over a CII or UBL document.</summary>
/// <remarks>
/// Both syntaxes are namespace-heavy and the prefixes differ between producers (and between
/// versions of the same producer). Matching on the local name is what makes one reader work on
/// every file the standard allows; the element names themselves are fixed by the schemas.
/// </remarks>
internal static class Xml
{
    public static XElement? Child(this XElement? parent, string localName)
        => parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName);

    public static IEnumerable<XElement> Children(this XElement? parent, string localName)
        => parent is null
            ? []
            : parent.Elements().Where(element => element.Name.LocalName == localName);

    public static string? Text(this XElement? parent, string localName)
    {
        var value = parent.Child(localName)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static string? AttributeValue(this XElement? element, string localName)
        => element?.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value.Trim();

    public static decimal? Amount(this XElement? parent, string localName)
    {
        var text = parent.Text(localName);
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static decimal Number(this XElement? parent, string localName) => parent.Amount(localName) ?? 0m;

    /// <summary>Reads <c>yyyyMMdd</c> (CII, format 102) or <c>yyyy-MM-dd</c> (UBL).</summary>
    public static DateOnly? Date(this XElement? parent, string localName)
        => DateOf(parent.Child(localName));

    /// <summary>Parses the text of an element holding a date in either syntax.</summary>
    public static DateOnly? DateOf(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var format in new[] { "yyyyMMdd", "yyyy-MM-dd" })
        {
            if (DateOnly.TryParseExact(element.Value.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>The guideline URN, which is where the conformance level is declared.</summary>
    public static FacturXProfile Profile(XElement root)
    {
        var urn = root.Descendants().FirstOrDefault(element =>
            element.Name.LocalName is "GuidelineSpecifiedDocumentContextParameter" or "CustomizationID");

        var value = urn?.Descendants().FirstOrDefault(element => element.Name.LocalName == "ID")?.Value.Trim()
            ?? urn?.Value.Trim();

        return FacturXProfiles.TryParse(value, out var profile) ? profile : FacturXProfile.En16931;
    }

    /// <summary>An amount, as both syntaxes want it: a dot and two decimals.</summary>
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// A percentage, without trailing zeros. Both schemas accept <c>20</c> and <c>5.5</c>; writing
    /// <c>20.00</c> is legal but differs from every producer's output, so the round trips would
    /// compare text rather than meaning.
    /// </summary>
    public static string Rate(decimal value)
        => value.ToString(value == decimal.Truncate(value) ? "0" : "0.00", CultureInfo.InvariantCulture);

    /// <summary>A quantity, up to four decimals.</summary>
    public static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>A date in the CII <c>format="102"</c> spelling.</summary>
    public static string CompactDate(DateOnly value) => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>A date in the UBL <c>yyyy-MM-dd</c> spelling.</summary>
    public static string IsoDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
