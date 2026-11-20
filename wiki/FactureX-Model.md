# FactureX — the invoice model

One model covers both syntaxes the standard allows: CII (`CrossIndustryInvoice`) and UBL (`Invoice`).
The reader fills it, the writer emits CII, and the rest of your code does not care which one the
sender used.

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
