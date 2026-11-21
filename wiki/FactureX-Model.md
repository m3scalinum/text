# FactureX — the invoice model

One model covers both syntaxes the standard allows: CII (`CrossIndustryInvoice`) and UBL (`Invoice`,
`CreditNote`). The reader fills it, `CiiWriter` and `UblWriter` emit it, and the rest of your code
does not care which one the sender used.

## The header

```csharp
var invoice = new FacturXInvoice
{
    Number = "FA-2026-0042",              // BT-1
    TypeCode = "380",                     // BT-3: invoice. "381" is a credit note
    Profile = FacturXProfile.En16931,
    Currency = "EUR",                     // BT-5
    IssueDate = new DateOnly(2026, 2, 1), // BT-2
    DueDate = new DateOnly(2026, 3, 3),   // BT-9
    BusinessProcess = "B1",               // BT-23, the billing framework
    BuyerReference = "PO-7781",           // BT-10
    OrderReference = "CMD-4410",          // BT-13
    PaymentMeansCode = "30",              // BT-81
    PaymentTerms = "30 jours net",
    Iban = "FR7630006000011234567890189", // BT-84
};
```

## The parties

`Seller` and `Buyer` are `FacturXParty`: a name, an address, and the identifiers each ecosystem asks
for.

```csharp
Seller = new FacturXParty
{
    Name = "Atelier Typographique",        // BT-27
    LegalId = "123456789",                 // BT-30: SIREN, nine digits
    LegalIdScheme = "0002",                // BT-30-1: 0002 = SIREN, 0009 = SIRET
    PartyId = "AT-001",                    // BT-29, your own identifier
    PartyIdScheme = "CUST",
    ElectronicAddress = "factures@atelier.fr",  // BT-34
    ElectronicAddressScheme = "0225",      // BT-34-1: 0225 = e-mail
    VatId = "FR40123456789",               // BT-31
    Line1 = "12 rue des Presses",
    Postcode = "59000",
    City = "Lille",
    Country = "FR",
};
```

`OperationCategory` (BT-118) and `VatOnDebits` cover the French regimes where VAT is due on debits
rather than on the invoice.

A `FacturXContact` carries the point of contact the standard makes optional: `Name` (BT-41 / BT-56),
`Phone` (BT-42 / BT-57) and `Email` (BT-43 / BT-58). `FacturXParty.Email` remains as the shorthand for
the contact e-mail, so a document read before contacts existed still has its address.

```csharp
Seller = new FacturXParty
{
    Name = "Atelier Typographique",
    Contact = new FacturXContact
    {
        Name = "Jeanne Martin",
        Phone = "+33123456789",
        Email = "jeanne@atelier.fr",
    },
};
```

## The lines and the totals

Each `FacturXLine` carries its own net amount and tax rate; `TaxBreakdown` groups them by category and
rate; `Totals` holds the header figures. The validator checks that they agree — a document whose
lines do not sum to its totals is refused before it is rendered.

```csharp
Lines =
[
    new FacturXLine
    {
        Id = "1",                 // BT-126
        Name = "Journée de conseil",
        Quantity = 2,
        UnitPrice = 150.00m,
        LineNetAmount = 300.00m,  // BT-131
        TaxCategory = "S",        // BT-151: S, Z, E, AE, K, G, O, L, M
        TaxRate = 20m,
    },
],
```

## Allowances, charges and periods

`FacturXAllowanceCharge` covers both directions of an adjustment, at document level (`BG-20` /
`BG-21`) and at line level (`BG-27` / `BG-28`). `IsCharge` says which: the amount stays positive, and
the flag states whether it is added or subtracted — the standard never writes one with the other's
sign.

```csharp
AllowancesCharges =
[
    new FacturXAllowanceCharge
    {
        Amount = 30.00m,          // BT-92
        BaseAmount = 300.00m,     // BT-93
        Percentage = 10m,         // BT-94
        Reason = "Remise fidélité", // BT-97
        ReasonCode = "95",        // BT-98
        VatCategory = "S",        // BT-95, what places it in the breakdown
        VatRate = 20m,            // BT-96
    },
    new FacturXAllowanceCharge
    {
        IsCharge = true,          // BG-21
        Amount = 10.00m,          // BT-99
        Reason = "Frais de dossier",
        VatCategory = "S",
        VatRate = 20m,
    },
],
```

A line carries its own `AllowancesCharges` (BG-27 / BG-28) and a `Period` (BG-26). The document
carries `Period` (BG-14) as well. `FacturXPeriod` holds a `Start` and an `End`, either of which may be
the only one known.

`Totals.AllowanceTotal` (BT-107) and `Totals.ChargeTotal` (BT-108) are the sums of the document-level
adjustments; they are what makes `TaxBasisTotal` differ from `LineTotal`.

## References and payment

`ContractReference` (BT-12), `ProjectReference` (BT-11) and `PaymentReference` (BT-83) are plain
strings; `SupportingDocuments` is a list of `FacturXSupportingDocument` with an identifier, its
scheme, a description and a location (BG-24).

```csharp
PaymentReference = "RF18539007547034",   // BT-83, what the payer quotes
SupportingDocuments =
[
    new FacturXSupportingDocument
    {
        Id = "TS-2026-04",              // BT-122
        Scheme = "ABZ",                 // BT-18-1
        Description = "Feuille de temps", // BT-123
        Uri = "https://example.test/ts.pdf", // BT-124
    },
],
```

A SEPA direct debit is a `FacturXMandate`: the mandate reference (BT-89) and the account debited
(BT-91). `TaxCurrency` (BT-6) names the currency the VAT is accounted for in when it is not the
invoice currency.

## Notes

`Note` holds a single free-text note (BT-22). `Notes` holds notes that carry a subject code (BT-21),
which is what the French profile requires:

```csharp
Notes =
[
    new FacturXNote { Content = "Frais de recouvrement : 40 €", SubjectCode = "PMT" },
    new FacturXNote { Content = "Pénalités de retard : 3 fois le taux légal", SubjectCode = "PMD" },
    new FacturXNote { Content = "Escompte : néant", SubjectCode = "AAB" },
],
```

## The place of delivery

`DeliveryAddress` is a third `FacturXParty`, written as `ShipToTradeParty` (BG-13). Nothing is emitted
when it is empty.

## Cascade invoicing

A line may hold sub-lines: set `ParentLineId` on the children and the parent becomes a `GROUP` line.
The header totals count the **root** lines only — a `GROUP` line already contains its children, and
summing both double-counts the invoice.

```csharp
Lines =
[
    new FacturXLine { Id = "1", Name = "Prestation globale", LineNetAmount = 300.00m, TaxCategory = "S", TaxRate = 20m },
    new FacturXLine { Id = "1.1", ParentLineId = "1", Name = "Conseil", LineNetAmount = 200.00m, TaxCategory = "S", TaxRate = 20m },
    new FacturXLine { Id = "1.2", ParentLineId = "1", Name = "Documentation", LineNetAmount = 100.00m, TaxCategory = "S", TaxRate = 20m },
],
```

`PriorInvoiceNumber` and `PriorInvoiceDate` (BT-25/BT-26) link a corrective invoice to the one it
corrects, and `LineSeller` (BG-29) identifies a supplier that differs from the one on the header.
