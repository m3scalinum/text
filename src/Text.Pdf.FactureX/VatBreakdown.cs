using System;
using System.Collections.Generic;
using System.Linq;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>The VAT breakdown a national writer states when the model carries none.</summary>
/// <remarks>
/// Both national schemas refuse a document without a VAT summary per rate, where CII and UBL only
/// need the totals. A model built in C# often carries lines and totals but no breakdown, so the
/// writers derive one per category and rate — the arithmetic <c>BR-CO-17</c> states, not an
/// invention about the transaction.
/// </remarks>
internal static class VatBreakdown
{
    /// <summary>The breakdown to write: the model's own, or one derived from the lines.</summary>
    public static IReadOnlyList<FacturXTaxBreakdown> Of(FacturXInvoice invoice)
    {
        if (invoice.TaxBreakdown.Count > 0)
        {
            return invoice.TaxBreakdown;
        }

        return invoice.Lines
            .GroupBy(line => (Category: line.TaxCategory ?? "S", Rate: line.TaxRate ?? 0m))
            .Select(group =>
            {
                var basis = group.Sum(line => line.LineNetAmount);

                return new FacturXTaxBreakdown
                {
                    Category = group.Key.Category,
                    Rate = group.Key.Rate,
                    Basis = basis,
                    Amount = Math.Round(basis * group.Key.Rate / 100m, 2, MidpointRounding.AwayFromZero),
                };
            })
            .ToList();
    }
}
