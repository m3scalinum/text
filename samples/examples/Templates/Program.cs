using M3scalinum.Text.Templates;
using M3scalinum.Text.Typst.Compilation;

// Renders one layout of the pack to PDF. The layouts are resources inside the package: a package
// that lost its embedded templates fails when the catalog loads, and a package that lost its Typst
// dependency fails when the compiler is constructed.

var catalog = TemplateCatalog.Default;

Console.WriteLine("M3scalinum.Text.Templates");
Console.WriteLine($"  layouts      {catalog.Templates.Count}");

foreach (var descriptor in catalog.Templates)
{
    Console.WriteLine($"    {descriptor.Id,-15} v{descriptor.Version} [{string.Join(", ", descriptor.Languages)}]");
}

var invoice = catalog.Get(TemplateIds.Invoice);
var compiler = new TypstCompiler();

using var rendered = invoice.Template.RenderDocument(compiler, Parameters());
var pdf = rendered.ExportPdf();

Console.WriteLine($"  rendered     {invoice.Id} v{invoice.Version}, {rendered.PageCount} page(s),"
    + $" pdf {pdf.Length} bytes");

if (rendered.PageCount == 0 || pdf.Length == 0)
{
    throw new InvalidOperationException("the layout rendered an empty document");
}

if (pdf.Length < 5 || pdf[0] != '%' || pdf[1] != 'P' || pdf[2] != 'D' || pdf[3] != 'F')
{
    throw new InvalidOperationException("the render did not produce a PDF header");
}

Console.WriteLine();
Console.WriteLine("M3scalinum.Text.Templates: the pack's layouts and the compiler dependency both came "
    + "from the package.");

static Dictionary<string, object?> Parameters() => new(StringComparer.Ordinal)
{
    ["title"] = "Facture 2026-0042",
    ["number"] = "FA-2026-0042",
    ["issued"] = new DateOnly(2026, 3, 1),
    ["due"] = new DateOnly(2026, 3, 31),
    ["seller"] = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["name"] = "Atelier Dupont SAS",
        ["address"] = new[] { "12 rue des Lilas", "75011 Paris" },
        ["phone"] = "+33 1 55 00 12 34",
        ["email"] = "facturation@atelier-dupont.example",
        ["legal"] = "SAS au capital de 50 000 € · RCS Paris 812 345 678",
        ["terms"] = "Paiement à 30 jours",
        ["remit"] = "Atelier Dupont SAS — Service facturation",
        ["bank"] = "IBAN FR76 3000 4000 0300 0000 1234 567",
        ["tax_id"] = "FR 32 812345678",
    },
    ["customer"] = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["name"] = "Clinique des Tilleuls",
        ["address"] = new[] { "4 avenue de la Gare", "69002 Lyon" },
        ["email"] = "comptabilite@tilleuls.example",
        ["account"] = "CT-4471",
        ["po"] = "PO-2026-0117",
    },
    ["items"] = new[]
    {
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["description"] = "Audit technique",
            ["quantity"] = 1,
            ["unit_price"] = 950.00m,
        },
    },
    ["labels"] = TemplateLabels.For(TemplateIds.Invoice, TemplateLanguage.French),
};
