using System;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>The bridge between the Italian <c>ModalitaPagamento</c> codes and UNTDID 4461.</summary>
/// <remarks>
/// FatturaPA numbers its own payment modalities (<c>MP01</c> to <c>MP23</c>) where EN 16931 states
/// the UNTDID 4461 code. Only the modalities both lists name unambiguously are translated: cash,
/// cheque, credit transfer, credit card and direct debit. A code outside the table is carried
/// verbatim in both directions, so a document that states <c>MP13</c> — a MAV, which UNTDID 4461
/// does not have — is not silently turned into something a receiver would book differently.
/// </remarks>
internal static class FatturaPaPaymentMeans
{
    /// <summary>The UNTDID 4461 code an Italian modality stands for, or the code itself.</summary>
    public static string? TypeCode(string? modality)
    {
        if (modality is null)
        {
            return null;
        }

        return modality switch
        {
            "MP01" or "MP04" => "10",
            "MP02" or "MP03" => "20",
            "MP05" or "MP09" or "MP13" or "MP18" => "30",
            "MP08" => "54",
            "MP10" or "MP12" or "MP19" or "MP20" or "MP21" => "59",
            _ => modality,
        };
    }

    /// <summary>The Italian modality a UNTDID 4461 code is written as.</summary>
    /// <remarks>
    /// <c>58</c>, a SEPA credit transfer, and <c>30</c>, any credit transfer, are both
    /// <c>MP05</c> in the Italian list: reading the document back yields <c>30</c>, because the
    /// schema does not distinguish the two.
    /// </remarks>
    public static string Modality(string? typeCode) => typeCode switch
    {
        "10" => "MP01",
        "20" => "MP02",
        "30" or "58" => "MP05",
        "54" => "MP08",
        "49" or "59" => "MP19",
        null or "" => "MP05",
        var other => other.StartsWith("MP", StringComparison.Ordinal) ? other : "MP05",
    };
}
