# FactureX — the French profile

`FacturXProfile.ExtendedCtcFr` is the profile French platforms ask for. It is a subset of `EXTENDED`,
so `fx:ConformanceLevel` stays `EXTENDED`; only the guideline URN distinguishes it:

```
urn:cen.eu:en16931:2017#conformant#urn.cpro.gouv.fr:1p0:extended-ctc-fr
```

`FacturXProfiles.TryParse` reads that URN back into the enum, which is how a received document is
identified.

## The billing framework

```csharp
var invoice = new FacturXInvoice
{
    Profile = FacturXProfile.ExtendedCtcFr,
    BusinessProcess = "B1",   // B1 = B2B, S1 = public sector
    // ...
};
```

`BusinessProcess` (BT-23) is what tells a French platform which rules apply. Set it: an invoice with
`ExtendedCtcFr` and no framework is refused.

## Legal identifiers

French rules are about SIREN and SIRET, and they need to be declared as such:

```csharp
Seller = new FacturXParty
{
    LegalId = "123456789",     // the SIREN, nine digits
    LegalIdScheme = "0002",    // 0002 = SIREN, 0009 = SIRET
    ElectronicAddress = "factures@atelier.fr",
    ElectronicAddressScheme = "0225",   // 0225 = e-mail address
};
```

`BR-FR-10` requires a SIREN for a French seller, `BR-FR-32` requires it to be nine digits, and
`BR-FR-23` requires an address declared as `0225` to actually look like an e-mail. The French rules
apply only when the document is French — a `BusinessProcess` is set, or the seller is in `FR` — so a
German invoice is not rejected for a SIREN it does not need.

## The mandatory notes

Three mentions must be on every French invoice, each with its subject code (BT-21):

```csharp
Notes =
[
    new FacturXNote { Content = "Frais de recouvrement : 40 €", SubjectCode = "PMT" },
    new FacturXNote { Content = "Pénalités de retard : 3 fois le taux légal", SubjectCode = "PMD" },
    new FacturXNote { Content = "Escompte : néant", SubjectCode = "AAB" },
];
```

`BR-FR-05` refuses a document missing one of them, `BR-FR-31` checks that a `BAR` note carries a value
from the closed list, and `BR-FR-06` covers the wording. In the XML the content comes before the
subject code — the schema sequence is not negotiable.

## The place of delivery

`DeliveryAddress` is written as `ShipToTradeParty` and rendered under the buyer's address as
« Livraison : … ». Nothing is emitted when it is empty.

## Cascade invoicing

`CascadeFrameworks` (`S8`, `B8`, `M8`, `S9`, `B9`, `M9`) are the frameworks that allow a line to hold
sub-lines. `BidirectionalFrameworks` (`S9`, `B9`, `M9`) allow the buyer and the seller to switch
roles. `SelfBilledDocumentTypeCodes` lists the codes a self-billed invoice may carry.

The header totals count root lines only: a `GROUP` line already contains its children, and summing
both double-counts the invoice. `BR-CO-10` enforces it.
