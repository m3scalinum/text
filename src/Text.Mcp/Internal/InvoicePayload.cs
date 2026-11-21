using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using M3scalinum.Text.Pdf.FactureX;

namespace M3scalinum.Text.Mcp.Internal;

/// <summary>
/// Reads the JSON invoice payload of <c>render_invoice</c> into the e-invoicing model, term by term.
/// </summary>
/// <remarks>
/// <para>
/// The payload is read from a <see cref="JsonElement"/> rather than deserialized into a mirror class,
/// because a Model Context Protocol client is untrusted and the model is the contract: a mirror type
/// would let the e-invoicing package gain a property that silently becomes accepted input, and the
/// source generator would emit a writer for it that no call can reach. Every term below is a
/// decision about what this server accepts, an unknown name is ignored rather than guessed at, and
/// the error names the path — <c>invoice.lines[0].unitPrice</c> — so a client can fix the call.
/// </para>
/// <para>
/// Terms the payload omits take the value the renderer documents: <c>380</c> for the document type,
/// <c>EUR</c> for the currency, EN 16931 for the conformance level.
/// </para>
/// </remarks>
internal static class InvoicePayload
{
    /// <summary>Reads one invoice.</summary>
    /// <exception cref="McpToolArgumentsException">
    /// A term has the wrong JSON kind, or the profile is not a conformance level.
    /// </exception>
    internal static FacturXInvoice Read(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new McpToolArgumentsException("'invoice' must be an object");
        }

        return new FacturXInvoice
        {
            Number = Text(payload, "number", "invoice.number") ?? string.Empty,
            TypeCode = Text(payload, "typeCode", "invoice.typeCode") is { Length: > 0 } typeCode
                ? typeCode
                : "380",
            Profile = Profile(payload),
            Currency = Text(payload, "currency", "invoice.currency") is { Length: > 0 } currency
                ? currency
                : "EUR",
            TaxCurrency = Text(payload, "taxCurrency", "invoice.taxCurrency"),
            IssueDate = Date(payload, "issueDate", "invoice.issueDate") ?? default,
            DueDate = Date(payload, "dueDate", "invoice.dueDate"),
            Period = Period(payload, "period"),
            BuyerReference = Text(payload, "buyerReference", "invoice.buyerReference"),
            BusinessProcess = Text(payload, "businessProcess", "invoice.businessProcess"),
            OrderReference = Text(payload, "orderReference", "invoice.orderReference"),
            ContractReference = Text(payload, "contractReference", "invoice.contractReference"),
            ProjectReference = Text(payload, "projectReference", "invoice.projectReference"),
            Note = Text(payload, "note", "invoice.note"),
            Notes = Notes(payload),
            PaymentTerms = Text(payload, "paymentTerms", "invoice.paymentTerms"),
            PaymentMeansCode = Text(payload, "paymentMeansCode", "invoice.paymentMeansCode"),
            PaymentReference = Text(payload, "paymentReference", "invoice.paymentReference"),
            Iban = Text(payload, "iban", "invoice.iban"),
            Bic = Text(payload, "bic", "invoice.bic"),
            Seller = Party(payload, "seller") ?? new FacturXParty(),
            Buyer = Party(payload, "buyer") ?? new FacturXParty(),
            DeliveryAddress = Party(payload, "deliveryAddress"),
            Lines = ReadItems(payload, "lines", "invoice.lines", Line),
            AllowancesCharges = ReadItems(
                payload, "allowancesCharges", "invoice.allowancesCharges", AllowanceCharge),
            TaxBreakdown = ReadItems(payload, "taxBreakdown", "invoice.taxBreakdown", TaxBreakdown),
            Totals = Totals(payload),
        };
    }

    /// <summary>Reads the conformance level, by the names the standard and this suite use.</summary>
    private static FacturXProfile Profile(JsonElement payload)
    {
        var profile = Text(payload, "profile", "invoice.profile");

        if (string.IsNullOrWhiteSpace(profile))
        {
            return FacturXProfile.En16931;
        }

        if (Matches(profile, "minimum"))
        {
            return FacturXProfile.Minimum;
        }

        if (Matches(profile, "basicwl", "basic-wl", "basic-without-lines", "basic_without_lines"))
        {
            return FacturXProfile.BasicWithoutLines;
        }

        if (Matches(profile, "basic"))
        {
            return FacturXProfile.Basic;
        }

        if (Matches(profile, "en16931", "en-16931", "comfort"))
        {
            return FacturXProfile.En16931;
        }

        if (Matches(profile, "extended"))
        {
            return FacturXProfile.Extended;
        }

        if (Matches(profile, "extendedctcfr", "extended-ctc-fr", "extended_ctc_fr"))
        {
            return FacturXProfile.ExtendedCtcFr;
        }

        throw new McpToolArgumentsException(
            $"'invoice.profile' is '{profile}', which is not a Factur-X conformance level: use "
            + "minimum, basic-wl, basic, en16931, extended or extended-ctc-fr");
    }

    private static bool Matches(string value, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads a postal party.</summary>
    private static FacturXParty? Party(JsonElement payload, string name)
    {
        if (Object(payload, name, $"invoice.{name}") is not { } party)
        {
            return null;
        }

        var path = $"invoice.{name}";

        return new FacturXParty
        {
            Name = Text(party, "name", $"{path}.name"),
            LegalId = Text(party, "legalId", $"{path}.legalId"),
            LegalIdScheme = Text(party, "legalIdScheme", $"{path}.legalIdScheme"),
            PartyId = Text(party, "partyId", $"{path}.partyId"),
            PartyIdScheme = Text(party, "partyIdScheme", $"{path}.partyIdScheme"),
            ElectronicAddress = Text(party, "electronicAddress", $"{path}.electronicAddress"),
            ElectronicAddressScheme = Text(
                party, "electronicAddressScheme", $"{path}.electronicAddressScheme"),
            VatId = Text(party, "vatId", $"{path}.vatId"),
            Line1 = Text(party, "line1", $"{path}.line1"),
            Line2 = Text(party, "line2", $"{path}.line2"),
            Postcode = Text(party, "postcode", $"{path}.postcode"),
            City = Text(party, "city", $"{path}.city"),
            Country = Text(party, "country", $"{path}.country"),
            Email = Text(party, "email", $"{path}.email"),
        };
    }

    /// <summary>Reads one invoice line.</summary>
    private static FacturXLine Line(JsonElement line, string path) => new()
    {
        Id = Text(line, "id", $"{path}.id"),
        Name = Text(line, "name", $"{path}.name"),
        SellerAssignedId = Text(line, "sellerAssignedId", $"{path}.sellerAssignedId"),
        Quantity = Number(line, "quantity", $"{path}.quantity") ?? 0m,
        BaseQuantity = Number(line, "baseQuantity", $"{path}.baseQuantity"),
        UnitCode = Text(line, "unitCode", $"{path}.unitCode"),
        UnitPrice = Number(line, "unitPrice", $"{path}.unitPrice") ?? 0m,
        LineNetAmount = Number(line, "lineNetAmount", $"{path}.lineNetAmount") ?? 0m,
        TaxCategory = Text(line, "taxCategory", $"{path}.taxCategory"),
        TaxRate = Number(line, "taxRate", $"{path}.taxRate"),
    };

    /// <summary>Reads one document-level allowance or charge.</summary>
    private static FacturXAllowanceCharge AllowanceCharge(JsonElement adjustment, string path) => new()
    {
        IsCharge = Boolean(adjustment, "isCharge", $"{path}.isCharge") ?? false,
        Amount = Number(adjustment, "amount", $"{path}.amount") ?? 0m,
        BaseAmount = Number(adjustment, "baseAmount", $"{path}.baseAmount"),
        Percentage = Number(adjustment, "percentage", $"{path}.percentage"),
        Reason = Text(adjustment, "reason", $"{path}.reason"),
        ReasonCode = Text(adjustment, "reasonCode", $"{path}.reasonCode"),
        VatCategory = Text(adjustment, "vatCategory", $"{path}.vatCategory"),
        VatRate = Number(adjustment, "vatRate", $"{path}.vatRate"),
    };

    /// <summary>Reads one VAT breakdown line.</summary>
    private static FacturXTaxBreakdown TaxBreakdown(JsonElement tax, string path) => new()
    {
        Category = Text(tax, "category", $"{path}.category") is { Length: > 0 } category ? category : "S",
        Rate = Number(tax, "rate", $"{path}.rate"),
        Basis = Number(tax, "basis", $"{path}.basis") ?? 0m,
        Amount = Number(tax, "amount", $"{path}.amount") ?? 0m,
        ExemptionReason = Text(tax, "exemptionReason", $"{path}.exemptionReason"),
    };

    /// <summary>Reads the period of the document, when it has one.</summary>
    private static FacturXPeriod? Period(JsonElement payload, string name)
    {
        if (Object(payload, name, $"invoice.{name}") is not { } period)
        {
            return null;
        }

        return new FacturXPeriod
        {
            Start = Date(period, "start", $"invoice.{name}.start"),
            End = Date(period, "end", $"invoice.{name}.end"),
        };
    }

    /// <summary>Reads the free-text notes, with their subject codes.</summary>
    private static FacturXNote[] Notes(JsonElement payload)
    {
        if (Items(payload, "notes", "invoice.notes") is not { } notes)
        {
            return [];
        }

        var values = new List<FacturXNote>(notes.Count);

        for (var index = 0; index < notes.Count; index++)
        {
            var path = $"invoice.notes[{index}]";

            values.Add(new FacturXNote
            {
                Content = Text(notes[index], "content", $"{path}.content") ?? string.Empty,
                SubjectCode = Text(notes[index], "subjectCode", $"{path}.subjectCode"),
            });
        }

        return [.. values];
    }

    /// <summary>Reads the document totals.</summary>
    private static FacturXTotals Totals(JsonElement payload)
    {
        if (Object(payload, "totals", "invoice.totals") is not { } totals)
        {
            return new FacturXTotals();
        }

        const string TotalsPath = "invoice.totals";

        return new FacturXTotals
        {
            LineTotal = Number(totals, "lineTotal", $"{TotalsPath}.lineTotal") ?? 0m,
            AllowanceTotal = Number(totals, "allowanceTotal", $"{TotalsPath}.allowanceTotal") ?? 0m,
            ChargeTotal = Number(totals, "chargeTotal", $"{TotalsPath}.chargeTotal") ?? 0m,
            TaxBasisTotal = Number(totals, "taxBasisTotal", $"{TotalsPath}.taxBasisTotal") ?? 0m,
            TaxTotal = Number(totals, "taxTotal", $"{TotalsPath}.taxTotal") ?? 0m,
            GrandTotal = Number(totals, "grandTotal", $"{TotalsPath}.grandTotal") ?? 0m,
            Prepaid = Number(totals, "prepaid", $"{TotalsPath}.prepaid") ?? 0m,
            Due = Number(totals, "due", $"{TotalsPath}.due") ?? 0m,
        };
    }

    /// <summary>Reads an array of objects with a per-item reader.</summary>
    private static T[] ReadItems<T>(
        JsonElement payload,
        string name,
        string path,
        Converter<T> read)
    {
        if (Items(payload, name, path) is not { } items)
        {
            return [];
        }

        var values = new T[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            values[index] = read(items[index], $"{path}[{index}]");
        }

        return values;
    }

    private delegate T Converter<T>(JsonElement item, string path);

    /// <summary>Reads the items of an array-of-objects term, refusing anything else.</summary>
    private static List<JsonElement>? Items(JsonElement payload, string name, string path)
    {
        if (Property(payload, name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new McpToolArgumentsException($"'{path}' must be an array");
        }

        var items = new List<JsonElement>(value.GetArrayLength());

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new McpToolArgumentsException($"'{path}' must carry objects");
            }

            items.Add(item);
        }

        return items;
    }

    /// <summary>Reads an object term, refusing anything else.</summary>
    private static JsonElement? Object(JsonElement payload, string name, string path)
    {
        if (Property(payload, name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new McpToolArgumentsException($"'{path}' must be an object");
        }

        return value;
    }

    private static string? Text(JsonElement payload, string name, string path)
    {
        if (Property(payload, name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new McpToolArgumentsException($"'{path}' must be a string");
        }

        return value.GetString();
    }

    private static decimal? Number(JsonElement payload, string name, string path)
    {
        if (Property(payload, name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
        {
            throw new McpToolArgumentsException($"'{path}' must be a number");
        }

        return number;
    }

    private static bool? Boolean(JsonElement payload, string name, string path)
    {
        if (Property(payload, name) is not { } value)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new McpToolArgumentsException($"'{path}' must be true or false"),
        };
    }

    private static DateOnly? Date(JsonElement payload, string name, string path)
    {
        if (Property(payload, name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new McpToolArgumentsException($"'{path}' must be a date written yyyy-MM-dd");
    }

    /// <summary>The property, or <see langword="null"/> when it is absent or explicitly null.</summary>
    private static JsonElement? Property(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value
            : null;
}
