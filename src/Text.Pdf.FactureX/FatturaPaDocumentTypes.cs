using System;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>The document type codes of FatturaPA and the EN 16931 codes they stand for.</summary>
/// <remarks>
/// FatturaPA names eleven kinds of document where EN 16931 keeps one code per taxable event; the
/// table below is the part both lists agree on. A code with no Italian counterpart is written as a
/// plain invoice (<c>TD01</c>), which is what a receiver books in the absence of anything else.
/// </remarks>
internal static class FatturaPaDocumentTypes
{
    /// <summary>The EN 16931 <c>BT-3</c> code an Italian <c>TipoDocumento</c> stands for.</summary>
    public static string TypeCode(string? tipo) => tipo switch
    {
        "TD04" => "381",
        "TD05" => "383",
        "TD02" or "TD03" => "386",
        _ => "380",
    };

    /// <summary>The Italian <c>TipoDocumento</c> an EN 16931 code is written as.</summary>
    public static string Tipo(string? typeCode) => typeCode switch
    {
        "381" => "TD04",
        "383" => "TD05",
        "386" => "TD02",
        _ => "TD01",
    };

    /// <summary>The transmission format: the private one, because it is the B2B case.</summary>
    /// <remarks>
    /// <c>FPA12</c> addresses a public administration and carries a different <c>CodiceDestinatario</c>
    /// grammar; the model states neither, so the writer emits <c>FPR12</c> and a document that
    /// circulated as <c>FPA12</c> is not round-tripped as one.
    /// </remarks>
    public static string Format => "FPR12";
}
