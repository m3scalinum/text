using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Documents;
using M3scalinum.Text.SharpTypst.Export;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>Renders a Factur-X invoice as a PDF/A-3 with the XML attached.</summary>
public static class FacturXRenderer
{
    /// <summary>
    /// Renders the invoice, writing its CII XML and embedding it.
    /// </summary>
    /// <exception cref="FacturXValidationException">The invoice breaks an EN 16931 rule.</exception>
    /// <exception cref="TypstException">Typst rejected the generated document.</exception>
    public static byte[] Render(FacturXInvoice invoice, FacturXRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        options ??= new FacturXRenderOptions();

        return Render(invoice, CiiWriter.WriteBytes(invoice), options);
    }

    /// <summary>
    /// Renders the invoice with the XML it came from, byte for byte — what a receiver will read back.
    /// </summary>
    /// <exception cref="FacturXValidationException">The invoice breaks an EN 16931 rule.</exception>
    /// <exception cref="TypstException">Typst rejected the generated document.</exception>
    public static byte[] Render(FacturXInvoice invoice, ReadOnlySpan<byte> xml, FacturXRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        options ??= new FacturXRenderOptions();

        if (options.Validate)
        {
            FacturXValidator.Validate(invoice).ThrowIfInvalid();
        }

        var source = BuildMarkup(invoice, options);

        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", source)
            .AddAsset(options.FileName, xml.ToArray())
            .Build();

        using var result = new TypstCompiler().Compile(world);

        var pdf = result.ExportPdf(new PdfExportOptions
        {
            Standards = [PdfStandard.PdfA3b],
            Timestamp = new DateTimeOffset(
                invoice.IssueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            Ident = string.IsNullOrWhiteSpace(invoice.Number) ? null : invoice.Number,
            Creator = "M3scalinum.Text.Pdf.FactureX",
        });

        return FacturXPdf.AddMetadata(pdf, options.FileName, invoice.Profile);
    }

    /// <summary>Reads an invoice, then renders it with the XML it came from.</summary>
    /// <remarks>
    /// Factur-X mandates the CII syntax: a UBL source is converted through <see cref="CiiWriter"/>
    /// instead of being embedded as-is, which no receiver would accept.
    /// </remarks>
    public static byte[] ReadAndRender(string xml, FacturXRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(xml);
        options ??= new FacturXRenderOptions();

        var invoice = FacturXReader.Read(xml);
        var embedded = IsCii(xml) ? Encoding.UTF8.GetBytes(xml) : CiiWriter.WriteBytes(invoice);

        return Render(invoice, embedded, options);
    }

    private static bool IsCii(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
        return FacturXReader.PeekSyntax(stream) == "CII";
    }

    /// <summary>
    /// The whole Typst source: the attachment statement, then the layout.
    /// </summary>
    internal static string BuildMarkup(FacturXInvoice invoice, FacturXRenderOptions options)
        => new StringBuilder()
            .Append("#pdf.attach(")
            .Append('"').Append(EscapeTypst(options.FileName)).Append("\", ")
            .Append("relationship: \"supplement\", ")
            .Append("mime-type: \"text/xml\", ")
            .Append("description: \"Factur-X invoice data\")")
            .Append('\n')
            .Append(Layout(invoice, options))
            .ToString();

    private static string Layout(FacturXInvoice invoice, FacturXRenderOptions options)
    {
        // A credit note is the same document with the opposite sign: the title is the only thing
        // the reader needs to tell them apart at a glance.
        var kind = invoice.IsCreditNote ? "Avoir" : "Facture";

        var document = TypstDocument.Create()
            .Metadata(metadata => metadata
                .Title($"{kind} {invoice.Number}")
                .Author(invoice.Seller.Name ?? "M3scalinum.Text.SharpTypst")
                .Keywords("factur-x", invoice.Currency))
            .Page(page => page
                .Paper("a4")
                .Margin(TypstLength.Cm(2.2))
                .Numbering("1"));

        document.Heading(1, invoice.Seller.Name ?? kind);

        var sellerLines = Address(invoice.Seller);
        if (sellerLines.Length > 0)
        {
            document.Paragraph(sellerLines);
        }

        document.Divider();
        document.Heading(3, "Facturé à");

        var buyer = new StringBuilder();
        if (invoice.Buyer.Name is { Length: > 0 } buyerName)
        {
            buyer.Append(buyerName);
        }

        foreach (var line in Address(invoice.Buyer).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            buyer.Append('\n').Append(line);
        }

        if (invoice.Buyer.VatId is { Length: > 0 } buyerVat)
        {
            buyer.Append("\nTVA : ").Append(buyerVat);
        }

        document.Paragraph(buyer.ToString());

        // One of the four mentions the French mandate added: the place of delivery, printed only
        // when it is not the place of invoicing.
        if (invoice.DeliveryAddress is { } shipTo)
        {
            var delivery = new StringBuilder("Livraison :");

            if (shipTo.Name is { Length: > 0 } shipToName)
            {
                delivery.Append(' ').Append(shipToName);
            }

            foreach (var line in Address(shipTo).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                delivery.Append('\n').Append(line);
            }

            document.Paragraph(delivery.ToString());
        }

        var header = new StringBuilder()
            .Append("Numéro : ").Append(invoice.Number)
            .Append("\nDate : ").Append(FormatDate(invoice.IssueDate));

        if (invoice.DueDate is { } due)
        {
            header.Append("\nÉchéance : ").Append(FormatDate(due));
        }

        if (invoice.BuyerReference is { Length: > 0 } reference)
        {
            header.Append("\nRéférence acheteur : ").Append(reference);
        }

        if (invoice.OrderReference is { Length: > 0 } order)
        {
            header.Append("\nCommande : ").Append(order);
        }

        // The other two mentions the French mandate added. They have no home in the CII of the
        // profile — the French Schematron never reads them — so they live on the document itself.
        if (invoice.OperationCategory is { } category)
        {
            header.Append("\nNature de l'opération : ").Append(category switch
            {
                FacturXOperationCategory.Goods => "livraison de biens",
                FacturXOperationCategory.Services => "prestation de services",
                FacturXOperationCategory.Mixed => "livraison de biens et prestation de services",
                _ => "non précisée",
            });
        }

        if (invoice.VatOnDebits)
        {
            header.Append("\nOption pour le paiement de la taxe d'après les débits");
        }

        document.Paragraph(header.ToString());

        if (invoice.Lines.Count > 0)
        {
            document.Table(table =>
            {
                table.Columns(
                    TypstLength.Fr(1),
                    TypstLength.Fr(3),
                    TypstLength.Fr(1),
                    TypstLength.Fr(1.2),
                    TypstLength.Fr(1),
                    TypstLength.Fr(1.4));

                table.HeaderRow("Réf.", "Désignation", "Qté", "P.U. HT", "TVA", $"Total HT ({invoice.Currency})");

                foreach (var line in invoice.Lines)
                {
                    table.Row(
                        line.SellerAssignedId ?? line.Id ?? string.Empty,
                        line.Name ?? string.Empty,
                        FormatQuantity(line.Quantity),
                        FormatAmount(line.UnitPrice),
                        line.TaxRate is { } rate ? $"{rate.ToString("0.##", CultureInfo.InvariantCulture)} %" : (line.TaxCategory ?? string.Empty),
                        FormatAmount(line.LineNetAmount));
                }
            });
        }

        if (invoice.TaxBreakdown.Count > 0)
        {
            document.Heading(3, "TVA");

            document.Table(table =>
            {
                table.Columns(
                    TypstLength.Fr(1),
                    TypstLength.Fr(1),
                    TypstLength.Fr(2),
                    TypstLength.Fr(2));

                table.HeaderRow("Catégorie", "Taux", $"Base ({invoice.Currency})", $"Montant ({invoice.Currency})");

                foreach (var tax in invoice.TaxBreakdown)
                {
                    table.Row(
                        tax.Category,
                        tax.Rate is { } rate ? $"{rate.ToString("0.##", CultureInfo.InvariantCulture)} %" : "—",
                        FormatAmount(tax.Basis),
                        FormatAmount(tax.Amount));
                }
            });
        }

        var totals = new StringBuilder()
            .Append("Total HT : ").Append(FormatAmount(invoice.Totals.LineTotal)).Append(' ').Append(invoice.Currency)
            .Append("\nTotal TVA : ").Append(FormatAmount(invoice.Totals.TaxTotal)).Append(' ').Append(invoice.Currency);

        if (invoice.Totals.Prepaid != 0m)
        {
            totals.Append("\nDéjà réglé : ").Append(FormatAmount(invoice.Totals.Prepaid)).Append(' ').Append(invoice.Currency);
        }

        totals.Append("\n*Total TTC : ").Append(FormatAmount(invoice.Totals.GrandTotal)).Append(' ').Append(invoice.Currency);
        totals.Append("\n*Reste à payer : ").Append(FormatAmount(invoice.Totals.Due)).Append(' ').Append(invoice.Currency);

        document.Paragraph(totals.ToString());

        var payment = new StringBuilder();
        if (invoice.PaymentTerms is { Length: > 0 } terms)
        {
            payment.Append("Conditions de paiement : ").Append(terms);
        }

        if (invoice.Iban is { Length: > 0 } iban)
        {
            payment.Append(payment.Length > 0 ? "\n" : string.Empty).Append("IBAN : ").Append(iban);
        }

        if (invoice.Bic is { Length: > 0 } bic)
        {
            payment.Append("\nBIC : ").Append(bic);
        }

        if (payment.Length > 0)
        {
            document.Heading(3, "Paiement");
            document.Paragraph(payment.ToString());
        }

        if (invoice.Note is { Length: > 0 } note)
        {
            document.Divider();
            document.Paragraph(note);
        }

        return document.ToMarkup();
    }

    private static string Address(FacturXParty party)
    {
        var builder = new StringBuilder();

        void Append(string? part)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(part);
        }

        Append(party.Line1);
        Append(party.Line2);
        Append(string.Join(' ', new[] { party.Postcode, party.City }.Where(part => !string.IsNullOrWhiteSpace(part))));
        Append(party.Country);

        if (party.VatId is { Length: > 0 } vat)
        {
            Append($"TVA : {vat}");
        }

        if (party.LegalId is { Length: > 0 } legal)
        {
            Append($"SIREN : {legal}");
        }

        return builder.ToString();
    }

    private static string FormatDate(DateOnly date)
        => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// The French convention — <c>1 204,00</c> — built from the invariant format.
    /// </summary>
    /// <remarks>
    /// A <see cref="CultureInfo"/> would be the obvious implementation and the wrong one: a host
    /// running with <c>InvariantGlobalization</c> (the default in many containers) has no ICU, and
    /// <c>GetCultureInfo("fr-FR")</c> throws there. Formatting from the invariant culture works
    /// everywhere and keeps the output byte-identical between machines.
    /// </remarks>
    private static string FormatAmount(decimal value) => value
        .ToString("N2", CultureInfo.InvariantCulture)
        .Replace(",", " ", StringComparison.Ordinal)
        .Replace(".", ",", StringComparison.Ordinal);

    private static string FormatQuantity(decimal value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string EscapeTypst(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
