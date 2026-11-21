using System;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// The bridge between the Italian <c>Natura</c> codes and the EN 16931 VAT categories.
/// </summary>
/// <remarks>
/// FatturaPA states a rate and, for a zero rate, one of the codes N1 to N7 — a national
/// classification of <em>why</em> there is no tax. EN 16931 states one of nine categories. The two
/// are not the same list: several Italian codes collapse onto one category, and going back picks
/// the most usual spelling of the category. Both directions are tabulated here so that the reader
/// and the writer cannot drift apart.
/// </remarks>
internal static class FatturaPaNature
{
    /// <summary>The EN 16931 category an Italian <c>Natura</c> code stands for.</summary>
    /// <remarks>
    /// <c>N1</c> is an exclusion, <c>N2</c> an operation outside the scope, <c>N3.1</c> an export,
    /// <c>N3.2</c> an intra-community supply, <c>N3.5</c> an exemption and <c>N6</c> a reverse
    /// charge; those are the distinctions EN 16931 draws with <c>E</c>, <c>O</c>, <c>G</c>,
    /// <c>K</c> and <c>AE</c>.
    /// </remarks>
    public static string Category(string? natura)
    {
        if (string.IsNullOrEmpty(natura))
        {
            return "S";
        }

        if (natura.StartsWith("N3", StringComparison.Ordinal))
        {
            return natura switch
            {
                "N3.1" => "G",
                "N3.2" => "K",
                "N3.3" => "G",
                "N3.4" => "G",
                "N3.5" => "E",
                "N3.6" => "E",
                _ => "E",
            };
        }

        if (natura.StartsWith("N1", StringComparison.Ordinal)
            || natura.StartsWith("N4", StringComparison.Ordinal)
            || natura.StartsWith("N5", StringComparison.Ordinal))
        {
            return "E";
        }

        if (natura.StartsWith("N2", StringComparison.Ordinal))
        {
            return "O";
        }

        if (natura.StartsWith("N6", StringComparison.Ordinal)
            || natura.StartsWith("N7", StringComparison.Ordinal))
        {
            return "AE";
        }

        return "S";
    }

    /// <summary>
    /// The <c>Natura</c> code a category and a rate need, or <see langword="null"/> when the
    /// standard-rated line carries none.
    /// </summary>
    /// <remarks>
    /// FatturaPA refuses a zero rate without a code, so a category that EN 16931 states without one
    /// — <c>Z</c>, zero-rated with no exemption reason — is written as <c>N4</c> (exempt). It
    /// reads back as <c>E</c>: the Italian schema has no way to say "zero-rated and not exempt",
    /// and inventing one would produce a document no exchange system accepts.
    /// </remarks>
    public static string? Natura(string? category, decimal? rate) => category switch
    {
        null or "" => rate is > 0m ? null : "N1",
        "S" => rate is > 0m ? null : "N1",
        "E" => "N1",
        "G" => "N3.1",
        "K" => "N3.2",
        "O" => "N2",
        "AE" => "N6.1",
        "Z" => "N4",
        _ => "N1",
    };
}
