using System;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>The bridge between the Spanish <c>PaymentMeans</c> codes and UNTDID 4461.</summary>
/// <remarks>
/// Facturae numbers its own payment means where EN 16931 states the UNTDID 4461 code. Only the
/// means both lists name unambiguously are translated; a code outside the table is carried
/// verbatim in both directions rather than being turned into a different instruction to the payer.
/// </remarks>
internal static class FacturaePaymentMeans
{
    /// <summary>The UNTDID 4461 code a Spanish payment means stands for, or the code itself.</summary>
    public static string? TypeCode(string? means)
    {
        if (means is null)
        {
            return null;
        }

        return means switch
        {
            "01" => "10",
            "02" => "59",
            "04" => "30",
            "11" or "16" or "17" => "20",
            "19" => "54",
            _ => means,
        };
    }

    /// <summary>The Spanish payment means a UNTDID 4461 code is written as.</summary>
    public static string Means(string? typeCode) => typeCode switch
    {
        "10" => "01",
        "20" => "11",
        "30" or "42" or "58" => "04",
        "48" or "54" or "55" => "19",
        "49" or "59" => "02",
        null or "" => "04",
        var other => other.Length == 2 && char.IsDigit(other[0]) && char.IsDigit(other[1]) ? other : "04",
    };
}
