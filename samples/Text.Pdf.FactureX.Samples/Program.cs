using System;
using System.IO;
using M3scalinum.Text.Pdf.FactureX;

// Renders Factur-X invoices from three entry points:
//   1. an invoice built in C#,
//   2. a CII file, embedded byte for byte,
//   3. a UBL file, converted to CII because Factur-X accepts nothing else.

var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "samples");
Directory.CreateDirectory(outputDirectory);

Console.WriteLine("M3scalinum.Text.Pdf.FactureX");
Console.WriteLine();

// 1. Built in C#, written by the library.
var invoice = new FacturXInvoice
{
    Number = "FA-2026-0042",
    Profile = FacturXProfile.En16931,
    Currency = "EUR",
    IssueDate = new DateOnly(2026, 2, 1),
    DueDate = new DateOnly(2026, 3, 3),
    BuyerReference = "SE-42",
    PaymentTerms = "Paiement à 30 jours",
    Iban = "FR7630006000011234567890189",
    Bic = "AGRIFRPP",
    Seller = new FacturXParty
    {
        Name = "Atelier Typographique",
        LegalId = "123456789",
        VatId = "FR40123456789",
        Line1 = "12 rue des Glyphes",
        Postcode = "75011",
        City = "Paris",
        Country = "FR",
    },
    Buyer = new FacturXParty
    {
        Name = "Société Exemple",
        VatId = "FR55987654321",
        Line1 = "5 avenue du Test",
        Postcode = "69003",
        City = "Lyon",
        Country = "FR",
    },
    Lines =
    [
        new FacturXLine
        {
            Id = "1",
            Name = "Journée de conseil",
            SellerAssignedId = "CONSULT-01",
            Quantity = 2,
            UnitCode = "C62",
            UnitPrice = 150.00m,
            LineNetAmount = 300.00m,
            TaxCategory = "S",
            TaxRate = 20m,
        },
        new FacturXLine
        {
            Id = "2",
            Name = "Manuel technique",
            SellerAssignedId = "BOOK-07",
            Quantity = 1,
            UnitCode = "C62",
            UnitPrice = 800.00m,
            LineNetAmount = 800.00m,
            TaxCategory = "S",
            TaxRate = 5.5m,
        },
    ],
    TaxBreakdown =
    [
        new FacturXTaxBreakdown { Category = "S", Rate = 20m, Basis = 300.00m, Amount = 60.00m },
        new FacturXTaxBreakdown { Category = "S", Rate = 5.5m, Basis = 800.00m, Amount = 44.00m },
    ],
    Totals = new FacturXTotals
    {
        LineTotal = 1100.00m,
        TaxBasisTotal = 1100.00m,
        TaxTotal = 104.00m,
        GrandTotal = 1204.00m,
        Due = 1204.00m,
    },
};

var validation = FacturXValidator.Validate(invoice);
Console.WriteLine($"built invoice  : {(validation.IsValid ? "valid" : validation.Summary)}");

Write("factur-x-built.pdf", FacturXRenderer.Render(invoice));

// 2 and 3. The fixtures, when the sample runs from the repository root.
RenderFixture("invoice-cii.xml", "factur-x-from-cii.pdf");
RenderFixture("invoice-ubl.xml", "factur-x-from-ubl.pdf");

// 4. A French CIUS invoice: BT-23 declares the billing framework, and the identifiers a platform
//    routes on carry their scheme.
var french = new FacturXInvoice
{
    Number = invoice.Number,
    Profile = FacturXProfile.ExtendedCtcFr,
    Currency = invoice.Currency,
    IssueDate = invoice.IssueDate,
    DueDate = invoice.DueDate,
    BusinessProcess = "B1",
    Notes =
    [
        // The three mentions the French mandate requires, each with the code a platform reads.
        new FacturXNote { SubjectCode = "PMT", Content = "Frais de recouvrement : 40 EUR" },
        new FacturXNote { SubjectCode = "PMD", Content = "Pénalités de retard : 3 fois le taux d'intérêt légal" },
        new FacturXNote { SubjectCode = "AAB", Content = "Escompte pour paiement anticipé : néant" },
    ],
    BuyerReference = invoice.BuyerReference,
    PaymentTerms = invoice.PaymentTerms,
    Iban = invoice.Iban,
    Bic = invoice.Bic,
    Seller = new FacturXParty
    {
        Name = "Atelier Typographique",
        LegalId = "123456789",
        LegalIdScheme = "0002",
        PartyId = "12345678900011",
        PartyIdScheme = "0009",
        ElectronicAddress = "12345678900011",
        ElectronicAddressScheme = "0009",
        VatId = "FR40123456789",
        Line1 = "12 rue des Glyphes",
        Postcode = "75011",
        City = "Paris",
        Country = "FR",
    },
    Buyer = new FacturXParty
    {
        Name = "Société Exemple",
        LegalId = "552100554",
        LegalIdScheme = "0002",
        ElectronicAddress = "552100554",
        ElectronicAddressScheme = "0002",
        Line1 = "5 avenue du Test",
        Postcode = "69003",
        City = "Lyon",
        Country = "FR",
    },
    // The two mentions that have no structured home: they are printed on the document.
    OperationCategory = FacturXOperationCategory.Mixed,
    VatOnDebits = true,
    // The place of delivery, when it is not the place of invoicing.
    DeliveryAddress = new FacturXParty
    {
        Name = "Entrepôt Nord",
        Line1 = "3 rue du Dépôt",
        Postcode = "59000",
        City = "Lille",
        Country = "FR",
    },
    Lines = invoice.Lines,
    TaxBreakdown = invoice.TaxBreakdown,
    Totals = invoice.Totals,
};

// 5. A cascade invoice: a GROUP line stands for a subcontractor's sub-invoice, with the detail
//    lines below it. Only the S8/B8/M8/S9/B9/M9 frameworks allow it.
var cascade = new FacturXInvoice
{
    Number = "FA-2026-0100",
    Profile = FacturXProfile.ExtendedCtcFr,
    Currency = "EUR",
    IssueDate = new DateOnly(2026, 2, 1),
    BusinessProcess = "B8",
    Notes = french.Notes,
    Seller = french.Seller,
    Buyer = french.Buyer,
    Lines =
    [
        new FacturXLine
        {
            Id = "1",
            Name = "Sous-facture sous-traitant",
            Quantity = 1,
            UnitPrice = 300,
            UnitCode = "C62",
            TaxCategory = "S",
            TaxRate = 20m,
            LineNetAmount = 300,
            TaxTotal = 60,
            StatusReason = "GROUP",
            LineSeller = new FacturXParty
            {
                Name = "Sous-traitant SAS",
                Country = "FR",
                LegalId = "552100554",
                LegalIdScheme = "0002",
            },
            ObjectIdentifier = "SUB-2026-0001",
            ObjectIdentifierScheme = "AFL",
        },
        new FacturXLine
        {
            Id = "1.1",
            Name = "Main d'oeuvre",
            Quantity = 2,
            UnitPrice = 150,
            UnitCode = "C62",
            TaxCategory = "S",
            TaxRate = 20m,
            LineNetAmount = 300,
            TaxTotal = 60,
            StatusReason = "DETAIL",
            ParentLineId = "1",
            LineSeller = new FacturXParty
            {
                Name = "Sous-traitant SAS",
                Country = "FR",
                LegalId = "552100554",
                LegalIdScheme = "0002",
            },
            ObjectIdentifier = "SUB-2026-0001",
            ObjectIdentifierScheme = "AFL",
        },
    ],
    TaxBreakdown = [new FacturXTaxBreakdown { Category = "S", Rate = 20m, Basis = 300, Amount = 60 }],
    Totals = new FacturXTotals
    {
        LineTotal = 300,
        TaxBasisTotal = 300,
        TaxTotal = 60,
        GrandTotal = 360,
        Due = 360,
    },
};

var cascadeXml = CiiWriter.Write(cascade);
File.WriteAllText(Path.Combine(outputDirectory, "factur-x-cascade.xml"), cascadeXml);
Console.WriteLine($"  wrote factur-x-cascade.xml ({cascadeXml.Length} chars)");
Write("factur-x-cascade.pdf", FacturXRenderer.Render(cascade));

var frenchXml = CiiWriter.Write(french);
File.WriteAllText(Path.Combine(outputDirectory, "factur-x-french.xml"), frenchXml);
Console.WriteLine($"  wrote factur-x-french.xml ({frenchXml.Length} chars)");
Write("factur-x-french.pdf", FacturXRenderer.Render(french));

return 0;

void RenderFixture(string name, string output)
{
    var path = FindFixture(name);
    if (path is null)
    {
        Console.WriteLine($"fixture        : {name} not found, skipped");
        return;
    }

    var xml = File.ReadAllText(path);
    var read = FacturXReader.Read(xml);
    Console.WriteLine($"read {name,-12}: {read.Lines.Count} line(s), {read.Totals.GrandTotal} {read.Currency}, {read.Profile.ToConformanceLevel()}");

    Write(output, FacturXRenderer.ReadAndRender(xml));
}

void Write(string name, byte[] pdf)
{
    var path = Path.Combine(outputDirectory, name);
    File.WriteAllBytes(path, pdf);
    Console.WriteLine($"  wrote {name} ({pdf.Length} bytes)");
}

static string? FindFixture(string name)
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "facturx", name);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}
