using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using M3scalinum.Text.SharpTypst;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// The EN 16931 business rules that can be decided from the document itself — presence of the
/// mandatory terms and the arithmetic the standard pins down. Profile-specific cardinalities and
/// the code lists are outside this check.
/// </summary>
public static class FacturXValidator
{
    private const decimal Tolerance = 0.02m;

    /// <summary>Validates an invoice.</summary>
    public static FacturXValidationResult Validate(FacturXInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var issues = new List<FacturXIssue>();

        if (string.IsNullOrWhiteSpace(invoice.Number))
        {
            issues.Add(new("BR-02", "the invoice number (BT-1) is missing"));
        }

        if (invoice.IssueDate == default)
        {
            issues.Add(new("BR-03", "the issue date (BT-2) is missing or unreadable"));
        }

        if (!FacturXCodeLists.IsCurrencyCode(invoice.Currency))
        {
            issues.Add(new("BR-05", $"`{invoice.Currency}` is not an ISO 4217 currency code (BT-5)"));
        }

        if (invoice.TypeCode is not { Length: 3 } || !invoice.TypeCode.All(char.IsAsciiDigit))
        {
            issues.Add(new("BR-04", $"`{invoice.TypeCode}` is not a three-digit document type code (BT-3)"));
        }

        if (string.IsNullOrWhiteSpace(invoice.Seller.Name))
        {
            issues.Add(new("BR-06", "the seller name (BT-27) is missing"));
        }

        if (string.IsNullOrWhiteSpace(invoice.Buyer.Name))
        {
            issues.Add(new("BR-07", "the buyer name (BT-44) is missing"));
        }

        if (string.IsNullOrWhiteSpace(invoice.Seller.Country))
        {
            issues.Add(new("BR-08", "the seller postal address has no country (BT-40)"));
        }

        if (string.IsNullOrWhiteSpace(invoice.Buyer.Country))
        {
            issues.Add(new("BR-09", "the buyer postal address has no country (BT-55)"));
        }

        var needsLines = invoice.Profile is not FacturXProfile.BasicWithoutLines and not FacturXProfile.Minimum;

        // In a cascade invoice a GROUP line already contains its sub-lines, so the header totals are
        // the sum of the root lines, not of every line.
        var counted = invoice.BusinessProcess is { Length: > 0 } framework
            && FacturXCodeLists.CascadeFrameworks.Contains(framework)
                ? invoice.Lines.Where(line => line.ParentLineId is not { Length: > 0 }).ToList()
                : invoice.Lines;

        if (needsLines && invoice.Lines.Count == 0)
        {
            issues.Add(new("BR-16", $"a {invoice.Profile.ToConformanceLevel()} invoice must carry at least one line (BG-25)"));
        }

        foreach (var line in invoice.Lines)
        {
            var expected = Math.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero);

            if (Math.Abs(expected - line.LineNetAmount) > Tolerance)
            {
                issues.Add(new(
                    "BR-CO-10",
                    $"line {line.Id ?? "(no id)"}: {line.Quantity} × {line.UnitPrice} = {expected}, but the document states {line.LineNetAmount}"));
            }

            if (string.IsNullOrWhiteSpace(line.TaxCategory))
            {
                issues.Add(new("BR-CO-4", $"line {line.Id ?? "(no id)"} has no VAT category (BT-151)"));
            }
            else if (!FacturXCodeLists.IsVatCategory(line.TaxCategory))
            {
                issues.Add(new("BR-CL-17", $"line {line.Id ?? "(no id)"}: `{line.TaxCategory}` is not a VAT category of EN 16931"));
            }

            if (line.UnitCode is { Length: > 0 } unit && !FacturXCodeLists.IsUnitCode(unit))
            {
                issues.Add(new("BR-CL-23", $"line {line.Id ?? "(no id)"}: `{unit}` is not a UN/ECE unit code"));
            }
        }

        if (invoice.TaxBreakdown.Count == 0 && needsLines)
        {
            issues.Add(new("BR-CO-18", "the document has no VAT breakdown (BG-23)"));
        }

        foreach (var tax in invoice.TaxBreakdown)
        {
            var expected = Math.Round(tax.Basis * (tax.Rate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero);

            if (tax.Rate is not null && Math.Abs(expected - tax.Amount) > Tolerance)
            {
                issues.Add(new(
                    "BR-CO-17",
                    $"VAT {tax.Category} at {tax.Rate}%: {tax.Basis} × {tax.Rate}% = {expected}, but the document states {tax.Amount}"));
            }

            if (!FacturXCodeLists.IsVatCategory(tax.Category))
            {
                issues.Add(new("BR-CL-17", $"`{tax.Category}` is not a VAT category of EN 16931"));
            }

            if (FacturXCodeLists.ExemptCategories.Contains(tax.Category) && tax.Rate is > 0m)
            {
                issues.Add(new("BR-Z-08", $"the {tax.Category} breakdown carries a {tax.Rate}% rate; an exempt category has none"));
            }

            if (tax.Category == "S" && tax.Rate is null or <= 0m)
            {
                issues.Add(new("BR-S-08", "a standard-rated breakdown needs a rate above zero (BT-119)"));
            }

            if (FacturXCodeLists.ExemptCategories.Contains(tax.Category) && string.IsNullOrWhiteSpace(tax.ExemptionReason))
            {
                issues.Add(new("BR-E-10", $"the {tax.Category} breakdown has no exemption reason (BT-120)"));
            }
        }

        // BR-CO-9: every VAT category and rate in the breakdown must be the sum of the lines that
        // carry it. A breakdown that does not match the lines is the classic silent error.
        foreach (var tax in invoice.TaxBreakdown)
        {
            var lines = counted
                .Where(line => string.Equals(line.TaxCategory, tax.Category, StringComparison.Ordinal)
                    && (line.TaxRate ?? 0m) == (tax.Rate ?? 0m))
                .ToList();

            if (lines.Count == 0)
            {
                continue;
            }

            var sum = lines.Sum(line => line.LineNetAmount);

            if (Math.Abs(sum - tax.Basis) > Tolerance)
            {
                issues.Add(new(
                    "BR-CO-9",
                    $"the {tax.Category} {tax.Rate}% lines sum to {sum}, but the breakdown states a basis of {tax.Basis}"));
            }
        }

        if (invoice.Lines.Any(line => line.TaxCategory == "S") && string.IsNullOrWhiteSpace(invoice.Seller.VatId))
        {
            issues.Add(new("BR-18", "a standard-rated invoice needs the seller's VAT identifier (BT-31)"));
        }

        if (invoice.PaymentMeansCode is { Length: > 0 } means && !FacturXCodeLists.IsPaymentMeansCode(means))
        {
            issues.Add(new("BR-CL-16", $"`{means}` is not a payment means code of EN 16931 (BT-81)"));
        }

        if (invoice.Lines.Count > 0)
        {
            Check(issues, "BR-CO-10", "the sum of the line net amounts", counted.Sum(line => line.LineNetAmount), invoice.Totals.LineTotal);
        }

        if (invoice.TaxBreakdown.Count > 0)
        {
            Check(issues, "BR-CO-13", "the sum of the VAT bases", invoice.TaxBreakdown.Sum(tax => tax.Basis), invoice.Totals.TaxBasisTotal);
            Check(issues, "BR-CO-14", "the sum of the VAT amounts", invoice.TaxBreakdown.Sum(tax => tax.Amount), invoice.Totals.TaxTotal);
        }

        Check(issues, "BR-CO-15", "tax basis + tax total against the grand total", invoice.Totals.TaxBasisTotal + invoice.Totals.TaxTotal, invoice.Totals.GrandTotal);
        Check(issues, "BR-CO-16", "grand total − prepaid against the amount due", invoice.Totals.GrandTotal - invoice.Totals.Prepaid, invoice.Totals.Due);

        CheckFrench(issues, invoice);

        return new FacturXValidationResult(issues);
    }

    /// <summary>
    /// The rules of the French CIUS, which are checked only when the document declares a French
    /// billing framework.
    /// </summary>
    /// <remarks>
    /// The CIUS is a national overlay: a document falls under it because its issuer says so, and in
    /// the data that declaration is BT-23, the <em>cadre de facturation</em>. Gating on the seller's
    /// country instead would demand a SIREN from every invoice issued by a French company to a
    /// foreign customer, which the standard does not.
    /// </remarks>
    private static void CheckFrench(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        if (invoice.BusinessProcess is not { Length: > 0 })
        {
            return;
        }

        var siren = invoice.Seller.LegalId;

        if (siren is not { Length: > 0 })
        {
            issues.Add(new("BR-FR-10", "a French invoice carries the seller's SIREN in BT-30"));
        }
        else if (!IsSiren(siren))
        {
            issues.Add(new("BR-FR-32", $"the seller's SIREN `{siren}` is not nine digits"));
        }

        if (invoice.Buyer.LegalId is { Length: > 0 } buyerSiren && !IsSiren(buyerSiren))
        {
            issues.Add(new("BR-FR-32", $"the buyer's SIREN `{buyerSiren}` is not nine digits"));
        }

        // A SIRET is the SIREN of the entity followed by the five digits of the establishment.
        if (invoice.Seller.PartyId is { Length: > 0 } siret
            && siren is { Length: 9 }
            && !siret.StartsWith(siren, StringComparison.Ordinal))
        {
            issues.Add(new(
                "BR-FR-09",
                $"the seller's SIRET `{siret}` does not start with its SIREN `{siren}`"));
        }

        // BR-FR-13 and BR-FR-12: a platform has nowhere to deliver without the routing addresses.
        if (invoice.Seller.ElectronicAddress is not { Length: > 0 })
        {
            issues.Add(new("BR-FR-13", "a French invoice carries the seller's electronic address in BT-34"));
        }

        if (invoice.Buyer.ElectronicAddress is not { Length: > 0 })
        {
            issues.Add(new("BR-FR-12", "a French invoice carries the buyer's electronic address in BT-49"));
        }

        // BR-FR-05: three legal mentions are mandatory, each carried by a note with its subject
        // code. A note without a code is a remark; a platform never reads it.
        var codes = invoice.Notes
            .Where(note => note.SubjectCode is { Length: > 0 })
            .Select(note => note.SubjectCode!)
            .ToList();

        foreach (var required in new[] { "PMT", "PMD", "AAB" })
        {
            if (!codes.Contains(required, StringComparer.Ordinal))
            {
                issues.Add(new(
                    "BR-FR-05",
                    $"the legal mention carried by the note with subject code `{required}` is missing (BT-22)"));
            }
        }

        // BR-FR-06: a subject code qualifies one note, not two.
        foreach (var duplicate in codes.GroupBy(code => code, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            issues.Add(new("BR-FR-06", $"the subject code `{duplicate.Key}` appears {duplicate.Count()} times"));
        }

        // BR-FR-01: the invoice number is a token, not a sentence.
        if (!IsIdentifier(invoice.Number))
        {
            issues.Add(new("BR-FR-01", $"the invoice number `{invoice.Number}` carries a character the French rules refuse"));
        }

        // BR-FR-03: the issue date is a real date in this century.
        if (invoice.IssueDate.Year is < 2000 or > 2099)
        {
            issues.Add(new("BR-FR-03", $"the issue date {invoice.IssueDate:yyyy-MM-dd} is outside 2000–2099"));
        }

        // BR-FR-04: the document type code is one the French rules accept.
        if (!FacturXCodeLists.DocumentTypeCodes.Contains(invoice.TypeCode))
        {
            issues.Add(new("BR-FR-04", $"`{invoice.TypeCode}` is not a document type code the French rules accept"));
        }

        // BR-FR-08: the billing framework is one of the published ones.
        if (!FacturXCodeLists.BillingModes.Contains(invoice.BusinessProcess))
        {
            issues.Add(new("BR-FR-08", $"`{invoice.BusinessProcess}` is not a billing framework of the French CIUS"));
        }

        // BR-FR-15 and BR-FR-16: the French lists are narrower than the European ones.
        foreach (var line in invoice.Lines)
        {
            if (line.TaxCategory is { Length: > 0 } category && !FacturXCodeLists.FrenchVatCategories.Contains(category))
            {
                issues.Add(new("BR-FR-15", $"line {line.Id ?? "(no id)"}: `{category}` is not a French VAT category"));
            }

            if (line.TaxRate is { } rate && !FacturXCodeLists.FrenchVatRates.Contains(rate))
            {
                issues.Add(new("BR-FR-16", $"line {line.Id ?? "(no id)"}: {rate}% is not a French VAT rate"));
            }

            // BR-FR-DEC-02: a quantity carries at most four decimals.
            if (decimal.Round(line.Quantity, 4) != line.Quantity)
            {
                issues.Add(new("BR-FR-DEC-02", $"line {line.Id ?? "(no id)"}: the quantity {line.Quantity} has more than four decimals"));
            }
        }

        foreach (var tax in invoice.TaxBreakdown)
        {
            if (!FacturXCodeLists.FrenchVatCategories.Contains(tax.Category))
            {
                issues.Add(new("BR-FR-15", $"`{tax.Category}` is not a French VAT category"));
            }

            if (tax.Rate is { } rate && !FacturXCodeLists.FrenchVatRates.Contains(rate))
            {
                issues.Add(new("BR-FR-16", $"{rate}% is not a French VAT rate"));
            }

            // BR-FR-DEC-04: a rate carries at most two decimals.
            if (tax.Rate is { } precise && decimal.Round(precise, 2) != precise)
            {
                issues.Add(new("BR-FR-DEC-04", $"the rate {precise}% has more than two decimals"));
            }
        }

        // BR-FR-DEC-01: an amount carries at most two decimals.
        foreach (var (name, amount) in new (string, decimal)[]
        {
            ("line total", invoice.Totals.LineTotal),
            ("tax basis", invoice.Totals.TaxBasisTotal),
            ("tax total", invoice.Totals.TaxTotal),
            ("grand total", invoice.Totals.GrandTotal),
            ("amount due", invoice.Totals.Due),
        })
        {
            if (decimal.Round(amount, 2) != amount)
            {
                issues.Add(new("BR-FR-DEC-01", $"the {name} {amount} has more than two decimals"));
            }
        }

        // BR-FR-23: an address declared as an e-mail has to look like one.
        foreach (var (who, party) in new[] { ("seller", invoice.Seller), ("buyer", invoice.Buyer) })
        {
            if (party.ElectronicAddress is { Length: > 0 } address
                && string.Equals(party.ElectronicAddressScheme, "0225", StringComparison.Ordinal)
                && !IsEmail(address))
            {
                issues.Add(new("BR-FR-23", $"the {who}'s electronic address `{address}` is not an e-mail"));
            }
        }

        // BR-FR-31: a BAR note declares exactly one known treatment.
        var bar = invoice.Notes
            .Where(note => string.Equals(note.SubjectCode, "BAR", StringComparison.Ordinal))
            .Select(note => note.Content.Trim())
            .ToList();

        if (bar.Count > 1)
        {
            issues.Add(new("BR-FR-31", $"the BAR note appears {bar.Count} times; it declares one treatment"));
        }

        foreach (var treatment in bar.Where(value => !FacturXCodeLists.BarTreatments.Contains(value)))
        {
            issues.Add(new("BR-FR-31", $"`{treatment}` is not a treatment a BAR note may declare"));
        }

        // BR-FR-CO-07: the due date cannot precede the issue date.
        if (invoice.DueDate is { } due && due < invoice.IssueDate)
        {
            issues.Add(new(
                "BR-FR-CO-07",
                $"the due date {due:yyyy-MM-dd} precedes the issue date {invoice.IssueDate:yyyy-MM-dd}"));
        }

        CheckCascade(issues, invoice);
    }

    /// <summary>
    /// The rules of cascade invoicing, where a <c>GROUP</c> line stands for a sub-invoice issued by
    /// another seller and the detail lines below it carry that seller's own lines.
    /// </summary>
    /// <remarks>
    /// They apply only under the frameworks that declare a cascade — <c>S8</c>, <c>B8</c>,
    /// <c>M8</c>, <c>S9</c>, <c>B9</c>, <c>M9</c> — and the bidirectional ones add their own.
    /// </remarks>
    private static void CheckCascade(List<FacturXIssue> issues, FacturXInvoice invoice)
    {
        var framework = invoice.BusinessProcess!;
        var cascade = FacturXCodeLists.CascadeFrameworks.Contains(framework);
        var bidirectional = FacturXCodeLists.BidirectionalFrameworks.Contains(framework);

        if (!cascade)
        {
            return;
        }

        var groups = invoice.Lines
            .Where(line => string.Equals(line.StatusReason, "GROUP", StringComparison.Ordinal))
            .ToList();

        var roots = groups.Where(line => line.ParentLineId is not { Length: > 0 }).ToList();

        // BR-FR-MV-01: every line says whether it is a group or a detail.
        foreach (var line in invoice.Lines.Where(line => line.StatusReason is not { Length: > 0 }))
        {
            issues.Add(new("BR-FR-MV-01", $"line {line.Id ?? "(no id)"} has no line status reason (BT-127)"));
        }

        // BR-FR-MV-02: a cascade invoice has at least one root group line.
        if (roots.Count == 0)
        {
            issues.Add(new("BR-FR-MV-02", "a cascade invoice carries at least one GROUP line without a parent"));
        }

        // BR-FR-MV-12: the sub-invoice numbers of the root group lines are unique.
        foreach (var duplicate in roots
            .Where(line => line.ObjectIdentifier is { Length: > 0 })
            .GroupBy(line => line.ObjectIdentifier!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            issues.Add(new("BR-FR-MV-12", $"the sub-invoice number `{duplicate.Key}` appears on {duplicate.Count()} root lines"));
        }

        foreach (var group in roots)
        {
            var id = group.Id ?? "(no id)";

            // BR-FR-MV-03: a root group line names the sub-invoice, its seller and its VAT.
            if (group.ObjectIdentifier is not { Length: > 0 })
            {
                issues.Add(new("BR-FR-MV-03", $"group line {id} carries no sub-invoice number (BT-128, scheme AFL)"));
            }

            if (group.LineSeller is not { Name.Length: > 0 })
            {
                issues.Add(new("BR-FR-MV-03", $"group line {id} names no seller (BG-29)"));
            }
            else if (group.LineSeller.LegalId is not { Length: > 0 })
            {
                issues.Add(new("BR-FR-MV-03", $"group line {id}: the line seller has no legal identifier"));
            }
            else if (group.LineSeller.Country is not { Length: > 0 })
            {
                issues.Add(new("BR-FR-MV-03", $"group line {id}: the line seller has no country"));
            }

            if (group.TaxTotal is null)
            {
                issues.Add(new("BR-FR-MV-03", $"group line {id} carries no VAT amount"));
            }

            // BR-FR-MV-05 and BR-FR-MV-09: the group line totals its sub-lines.
            var children = invoice.Lines
                .Where(line => string.Equals(line.ParentLineId, group.Id, StringComparison.Ordinal))
                .ToList();

            if (children.Count > 0)
            {
                var tolerance = 0.01m * children.Count;
                var sum = children.Sum(line => line.LineNetAmount);

                if (Math.Abs(sum - group.LineNetAmount) > tolerance)
                {
                    issues.Add(new(
                        "BR-FR-MV-05",
                        $"group line {id}: its {children.Count} sub-lines sum to {sum}, but the line states {group.LineNetAmount}"));
                }

                if (group.TaxTotal is { } groupTax)
                {
                    var taxSum = children.Sum(line => line.TaxTotal ?? 0m);

                    if (Math.Abs(taxSum - groupTax) > 0.01m)
                    {
                        issues.Add(new(
                            "BR-FR-MV-09",
                            $"group line {id}: its sub-lines carry {taxSum} of VAT, but the line states {groupTax}"));
                    }
                }
            }
        }

        // BR-FR-MV-06 and BR-FR-MV-07: a detail line agrees with its parent on the seller and the
        // sub-invoice number.
        foreach (var line in invoice.Lines.Where(line => line.ParentLineId is { Length: > 0 }))
        {
            var parent = invoice.Lines.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, line.ParentLineId, StringComparison.Ordinal));

            if (parent is null)
            {
                issues.Add(new("BR-FR-MV-06", $"line {line.Id ?? "(no id)"} points at a parent that does not exist"));
                continue;
            }

            if (line.LineSeller?.LegalId is { Length: > 0 } legal
                && parent.LineSeller?.LegalId is { Length: > 0 } parentLegal
                && !string.Equals(legal, parentLegal, StringComparison.Ordinal))
            {
                issues.Add(new(
                    "BR-FR-MV-06",
                    $"line {line.Id}: its seller `{legal}` differs from its parent's `{parentLegal}`"));
            }

            if (line.ObjectIdentifier is { Length: > 0 } number
                && parent.ObjectIdentifier is { Length: > 0 } parentNumber
                && !string.Equals(number, parentNumber, StringComparison.Ordinal))
            {
                issues.Add(new(
                    "BR-FR-MV-07",
                    $"line {line.Id}: its sub-invoice `{number}` differs from its parent's `{parentNumber}`"));
            }
        }

        // BR-FR-MV-13 and BR-FR-BD-13: the document type says who bills whom.
        var selfBilled = FacturXCodeLists.SelfBilledDocumentTypeCodes.Contains(invoice.TypeCode);

        if (bidirectional && !selfBilled)
        {
            issues.Add(new(
                "BR-FR-BD-13",
                $"under {framework} the document type must be a self-billed one, and `{invoice.TypeCode}` is not"));
        }

        if (!bidirectional && selfBilled)
        {
            issues.Add(new(
                "BR-FR-MV-13",
                $"under {framework} the document type must not be a self-billed one, and `{invoice.TypeCode}` is"));
        }

        // BR-FR-BD-02: a bidirectional invoice carries exactly two root group lines.
        if (bidirectional && roots.Count != 2)
        {
            issues.Add(new("BR-FR-BD-02", $"a bidirectional invoice carries two root GROUP lines, and this one has {roots.Count}"));
        }

        // BR-FR-MV-14 and BR-FR-BD-14: a corrective cascade line names the invoice it corrects.
        if (invoice.IsCreditNote || invoice.TypeCode is "384" or "471" or "472" or "473")
        {
            foreach (var line in invoice.Lines.Where(line => line.PriorInvoiceNumber is not { Length: > 0 }))
            {
                issues.Add(new("BR-FR-MV-14", $"line {line.Id ?? "(no id)"} names no prior invoice (BT-25)"));
            }
        }

        if (bidirectional)
        {
            foreach (var line in invoice.Lines.Where(line => line.PriorInvoiceDate is null))
            {
                issues.Add(new("BR-FR-BD-14", $"line {line.Id ?? "(no id)"} names no prior invoice date (BT-26)"));
            }
        }
    }

    /// <summary>Whether a value is a token: letters, digits and <c>+ - _ /</c>, no spaces.</summary>
    private static bool IsIdentifier(string value)
        => value.Length > 0 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '_' or '/');

    /// <summary>Whether a value has the shape of an e-mail address.</summary>
    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at < value.Length - 1
            && value.IndexOf('@', at + 1) < 0
            && value.IndexOf(' ', StringComparison.Ordinal) < 0
            && value.AsSpan(at + 1).Contains('.');
    }

    /// <summary>Whether a value has the shape of a SIREN: nine digits.</summary>
    public static bool IsSiren(string value)
        => value.Length == 9 && value.All(char.IsAsciiDigit);

    /// <summary>Whether a value has the shape of a SIRET: fourteen digits.</summary>
    public static bool IsSiret(string value)
        => value.Length == 14 && value.All(char.IsAsciiDigit);

    private static void Check(List<FacturXIssue> issues, string code, string what, decimal expected, decimal stated)
    {
        if (Math.Abs(expected - stated) > Tolerance)
        {
            issues.Add(new(code, $"{what}: computed {expected}, the document states {stated}"));
        }
    }
}
