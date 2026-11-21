using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using M3scalinum.Text.Typst;
using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Samples;
using M3scalinum.Text.Typst.Templates;

// One scenario per use case of docs/specifications/00-overview.md §3.
// Every scenario writes its artifacts under artifacts/samples and prints what it proved.

var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "samples");
Directory.CreateDirectory(outputDirectory);

Console.WriteLine($"M3scalinum.Text.Typst {TypstEnvironment.LibraryVersion}");
Console.WriteLine();

OneOffDocument();
StreamedExport();
AssetsFromMemory();
PdfAndHtmlInOnePass();
TypedTemplate();
DocumentModel();
HeaderAndFooter();
MarkdownAndPdf();
WarningsWithoutFailing();
BrokenDocument();
ConcurrentCompilations();
ByteForByteReproducible();

return 0;

// UC-1 — render a one-off document from a string.
void OneOffDocument()
{
    Section("UC-1  one-off document from a string");

    var pdf = TypstCompiler.CompileToPdf("""
        #set page(paper: "a4", margin: 2cm)

        = M3scalinum.Text.Typst

        A document compiled in-process by the Typst compiler. No browser, no Office,
        no command line.
        """);

    Write("hello.pdf", pdf);
}

// UC-1 — the same document, written straight to a file instead of a byte[].
void StreamedExport()
{
    Section("UC-1  streamed export");

    using var world = new TypstWorldBuilder()
        .AddSource("main.typ", """
            #set page(paper: "a4", margin: 2cm)

            = Streamed

            The PDF is copied out of the native layer in 64 KiB chunks, so it is never
            materialised a second time in a managed array.
            """)
        .Build();

    using var result = new TypstCompiler().Compile(world);

    var path = Path.Combine(outputDirectory, "hello-streamed.pdf");
    using (var file = File.Create(path))
    {
        result.ExportPdf(file);
    }

    Console.WriteLine($"  wrote hello-streamed.pdf ({new FileInfo(path).Length} bytes)");
}

// UC-2 — images and a custom font, all from memory.
void AssetsFromMemory()
{
    Section("UC-2  assets and fonts from memory");

    const string Logo = """
        <svg xmlns="http://www.w3.org/2000/svg" width="120" height="40" viewBox="0 0 120 40">
          <rect width="120" height="40" rx="6" fill="#2b6cb0"/>
          <text x="60" y="26" font-family="sans-serif" font-size="16" fill="#ffffff"
                text-anchor="middle">M3scalinum.Text.Typst</text>
        </svg>
        """;

    var builder = new TypstWorldBuilder()
        .AddAsset("logo.svg", Encoding.UTF8.GetBytes(Logo))
        .AddSource("main.typ", """
            #set page(paper: "a4", margin: 2cm)

            #image("logo.svg", width: 40%)

            = Assets from memory

            The logo above was registered as bytes; nothing was read from disk.
            """);

    var font = FindSystemFont();
    if (font is not null)
    {
        builder.AddFont(File.ReadAllBytes(font));
        Console.WriteLine($"  registered font {Path.GetFileName(font)}");
    }
    else
    {
        Console.WriteLine("  no system font found; using the embedded fonts");
    }

    using var world = builder.Build();
    using var result = new TypstCompiler().Compile(world);

    Write("assets.pdf", result.ExportPdf());
}

// UC-3 — the same document to PDF and HTML in one pass.
void PdfAndHtmlInOnePass()
{
    Section("UC-3  PDF and HTML in one pass");

    using var world = new TypstWorldBuilder()
        .AddSource("main.typ", """
            = One source, two targets

            The layout pass runs once; each export only encodes.
            """)
        .Build();

    using var both = new TypstCompiler().CompileBoth(world);

    Write("both.pdf", both.Paged.ExportPdf());
    Write("both.html", Encoding.UTF8.GetBytes(both.Html.ExportHtmlString()));
}

// UC-4 — fill a shipped template with a typed options record.
void TypedTemplate()
{
    Section("UC-4  typed template");

    var directory = Path.Combine(AppContext.BaseDirectory, "templates", "invoice");
    if (!Directory.Exists(directory))
    {
        Console.WriteLine("  templates/invoice not found; skipped");
        return;
    }

    var template = TypstTemplate<InvoiceOptions>.Load(directory);

    using var invoice = template.RenderDocument(new TypstCompiler(), new InvoiceOptions
    {
        Title = "Edge platform deployment — Cascade Health Group",
        Number = "INV-2026-0042",
        Issued = new DateOnly(2026, 3, 1),
        Due = new DateOnly(2026, 3, 31),
        Seller = new Dictionary<string, object?>
        {
            ["name"] = "Northwind Systems, Inc.",
            ["logo"] = "assets/logo.svg",
            ["address"] = new List<string>
            {
                "1200 Market Street, Suite 400",
                "San Francisco, CA 94103",
                "United States",
            },
            ["phone"] = "+1 (415) 555-0142",
            ["email"] = "billing@northwind.example",
            ["website"] = "northwind.example",
            ["terms"] = "Net 30 — wire transfer or ACH",
            ["remit"] = "Northwind Systems, Inc. — Accounts Receivable",
            ["bank"] = "First Cascade Bank · Routing 121000248 · Account 4471-8820-31",
            ["tax_id"] = "EIN 84-2719930",
            ["legal"] =
                "Northwind Systems, Inc. · 1200 Market Street, Suite 400, San Francisco, CA 94103 · "
                + "EIN 84-2719930 · CA seller's permit 104-8823-1. Payment is due within 30 days of the "
                + "invoice date; a 1.5% monthly service charge applies to overdue balances. This invoice "
                + "is governed by the Master Services Agreement between the parties. Questions: "
                + "billing@northwind.example or +1 (415) 555-0142.",
        },
        Customer = new Dictionary<string, object?>
        {
            ["name"] = "Cascade Health Group",
            ["contact"] = "Accounts Payable — Dana Whitfield",
            ["address"] = new List<string>
            {
                "4820 Willamette Avenue, Floor 9",
                "Portland, OR 97205",
            },
            ["email"] = "ap@cascadehealth.example",
            ["account"] = "CHG-118204",
            ["po"] = "PO-7741-B",
        },
        Items =
        [
            Line("Hardware", "NX-2200", "Edge gateway", "Dual-core ARM, 8 GB RAM, 128 GB NVMe", 4, 1249.00m),
            Line("Hardware", "NX-PSU", "Redundant power supply", "Spare, hot-swappable", 4, 89.50m),
            Line("Hardware", "SW-48P", "48-port PoE+ switch", "Managed, 740 W power budget", 2, 1899.00m),
            Line("Hardware", "RK-1U", "Rack mount kit", "1U, tool-less rails", 2, 74.00m),
            Line("Hardware", "C6A-3", "Cat6A patch cable, 3 ft", "10-pack, shielded", 6, 42.75m),
            Line("Hardware", "SFP-10G", "SFP+ 10G transceiver", "SR, LC duplex", 8, 129.00m),
            Line("Hardware", "UPS-1500", "UPS, 1500 VA", "Rack unit, network card included", 2, 649.00m),
            Line("Hardware", "ENV-KIT", "Environmental sensor kit", "Temperature, humidity, door contact", 3, 215.40m),

            Line("Software and licenses", "CP-50", "Control Plane subscription", "Annual, up to 50 managed sites", 1, 14400.00m),
            Line("Software and licenses", "EOS-1", "Edge OS license", "Per device, 3-year term", 8, 320.00m),
            Line("Software and licenses", "TEL-13", "Telemetry retention add-on", "13 months, 500 GB", 1, 2400.00m),
            Line("Software and licenses", "SSO-SCIM", "SSO and SCIM connector", "SAML 2.0, Okta and Entra ID", 1, 1800.00m),
            Line("Software and licenses", "API-SBX", "API sandbox tenant", "Non-production, 12 months", 2, 450.00m),

            Line("Professional services", "PS-ARCH", "Solution architecture workshop", "2 days on site, Portland", 2, 2400.00m),
            Line("Professional services", "PS-DEPLOY", "Deployment engineering", "Gateway staging and cutover", 36, 185.00m),
            Line("Professional services", "PS-ASSESS", "Network assessment", "Site survey, 3 buildings", 1, 3200.00m),
            Line("Professional services", "PS-MIGR", "Data migration", "Legacy CMDB import", 18, 165.00m),
            Line("Professional services", "PS-TRAIN", "Operator and admin training", "Two tracks, remote delivery", 3, 950.00m),
            Line("Professional services", "PS-CUTOVER", "Cutover support", "After hours, 6 pm to 2 am PT", 12, 245.00m),

            Line("Support and maintenance", "SUP-PREM", "Premium support", "24x7, one-hour P1 response", 1, 9600.00m),
            Line("Support and maintenance", "SUP-TAM", "Dedicated technical account manager", "Quarterly business reviews", 4, 1250.00m),
            Line("Support and maintenance", "SUP-SPARE", "Advance replacement pool", "Next-business-day hardware swap", 1, 1800.00m),
            Line("Support and maintenance", "SUP-FW", "Firmware maintenance", "Edge OS, 3 years", 1, 2700.00m),

            Line("Travel and expenses", "T&E-AIR", "Airfare, SFO to PDX", "Round trip, two engineers", 2, 386.40m, taxable: false),
            Line("Travel and expenses", "T&E-HOTEL", "Lodging", "Four nights, downtown Portland", 4, 219.00m, taxable: false),
            Line("Travel and expenses", "T&E-PERDIEM", "Per diem", "Meals and incidentals", 6, 79.00m, taxable: false),
            Line("Travel and expenses", "T&E-GROUND", "Ground transportation", "Airport transfers and site visits", 3, 64.25m, taxable: false),
        ],
        TaxRate = 0.0825,
        AmountPaid = 5000,
        Notes =
            "Thank you for your business. Please reference INV-2026-0042 on the wire or ACH memo. "
            + "Hardware ships from our Reno warehouse within five business days of the purchase order.",
    });

    Write("invoice.pdf", invoice.ExportPdf());
    Write("invoice.png", invoice.ExportPngMerged(ppi: 80f));

    Console.WriteLine($"  {invoice.PageCount} page(s)");
}

// UC-5 — build a report from a domain model without writing Typst.
void DocumentModel()
{
    Section("UC-5  document model");

    var report = TypstDocument.Create()
        .Metadata(m => m.Title("Q3 2026 revenue report").Author("Finance").Language("en"))
        .Page(p => p.Paper("a4").Margin(TypstLength.Cm(2)).Numbering("1 / 1"))
        .Heading(1, "Revenue")
        .Paragraph(c => c
            .Text("Revenue grew by ")
            .Bold("12%")
            .Text(" year over year, driven by ")
            .Emphasis("subscriptions")
            .Text("."))
        .Heading(2, "Breakdown")
        .Table(t => t
            .Columns(TypstLength.Fr(2), TypstLength.Fr(1))
            .HeaderRow("Item", "Amount")
            .Row("Hosting", "1 200 EUR")
            .Row("Support", "800 EUR"))
        .BulletList("Hosting", "Support")
        .Math("sum_(k=1)^n k = (n(n+1))/2")
        .Divider()
        .Paragraph(c => c.Text("See ").Link("https://typst.app", "the reference").Text("."));

    using var world = new TypstWorldBuilder().AddSource("main.typ", report.ToMarkup()).Build();
    using var result = new TypstCompiler().Compile(world);

    Write("report.pdf", result.ExportPdf());
    Write("report.png", result.ExportPng(ppi: 110f));

    var evaluation = result.Evaluate();
    Console.WriteLine($"  outline: {string.Join(" > ", evaluation.Outline.Select(e => e.Title))}");
}

// Extra — a running header and footer across several pages.
void HeaderAndFooter()
{
    Section("Extra  header and footer across pages");

    using var world = new TypstWorldBuilder()
        .AddSource("main.typ", """
            #set page(
              paper: "a4",
              margin: (x: 2.2cm, top: 3.2cm, bottom: 3.2cm),
              header: context {
                // No header on the title page.
                if counter(page).get().first() > 1 {
                  set text(size: 8pt, fill: luma(110))
                  grid(
                    columns: (1fr, auto),
                    [M3scalinum.Text.Typst — Annual report 2026],
                    [Internal],
                  )
                  v(-4pt)
                  line(length: 100%, stroke: 0.4pt + luma(180))
                }
              },
              footer: context {
                set text(size: 8pt, fill: luma(110))
                grid(
                  columns: (1fr, auto),
                  [Confidential],
                  [Page #counter(page).display() of #counter(page).final().first()],
                )
              },
            )

            #set text(size: 10pt)

            = Annual report 2026

            #lorem(90)

            == Revenue

            #lorem(200)

            == Costs

            #lorem(200)

            == Outlook

            #lorem(200)

            == Appendix

            #lorem(200)
            """)
        .Build();

    using var result = new TypstCompiler().Compile(world);

    Write("header-footer.pdf", result.ExportPdf());
    Write("header-footer.png", result.ExportPngMerged(ppi: 90f));

    Console.WriteLine($"  {result.PageCount} page(s)");
}

// UC-6 — Markdown for an e-mail body, PDF for the attachment.
void MarkdownAndPdf()
{
    Section("UC-6  Markdown and PDF from one source");

    using var world = new TypstWorldBuilder()
        .AddSource("main.typ", """
            = Weekly summary

            - shipped the package resolver
            - added the dependency-injection package

            See #link("https://typst.app")[the reference].
            """)
        .Build();

    using var both = new TypstCompiler().CompileBoth(world);

    Write("email.md", Encoding.UTF8.GetBytes(both.Html.ExportMarkdown()));
    Write("email.pdf", both.Paged.ExportPdf());
}

// UC-7 — surface warnings without failing.
void WarningsWithoutFailing()
{
    Section("UC-7  warnings without failing");

    using var world = new TypstWorldBuilder()
        .AddSource("main.typ", """
            #set text(font: "A Font That Does Not Exist")

            = Still compiles
            """)
        .Build();

    using var result = new TypstCompiler().Compile(world);

    foreach (var diagnostic in result.Diagnostics)
    {
        Console.WriteLine($"  {diagnostic.Severity}: {diagnostic.ToDisplayString()}");
    }

    Console.WriteLine($"  {result.Diagnostics.Count} diagnostic(s), {result.PageCount} page(s)");
    Console.WriteLine($"  {world.FontFamilies.Count} font families available, e.g. {string.Join(", ", world.FontFamilies.Take(3))}");
}

// UC-8 — fail with a precise error when the document is broken.
void BrokenDocument()
{
    Section("UC-8  precise failure");

    try
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", "= Report\n\n#let total = 42\n#let x = totl")
            .Build();

        using var result = new TypstCompiler().Compile(world);
        Console.WriteLine("  unexpected success");
    }
    catch (TypstCompilationException exception)
    {
        Console.WriteLine($"  {exception.GetType().Name}: {exception.Message}");

        foreach (var diagnostic in exception.Diagnostics)
        {
            Console.WriteLine(diagnostic.ToDisplayString(includeHints: true, includeTrace: false));
        }
    }
}

// UC-9 — compile concurrently, one world per compilation.
void ConcurrentCompilations()
{
    Section("UC-9  concurrent compilations");

    var compiler = new TypstCompiler();
    var failures = 0;

    Parallel.For(0, 8, index =>
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", $"= Document {index}\n\nBody {index}.")
            .Build();

        using var result = compiler.Compile(world);

        if (result.PageCount != 1)
        {
            System.Threading.Interlocked.Increment(ref failures);
        }
    });

    Console.WriteLine($"  8 parallel compilations, {failures} failure(s)");
}

// UC-10 — reproduce a PDF byte-for-byte.
void ByteForByteReproducible()
{
    Section("UC-10  byte-for-byte reproducibility");

    const string Markup = """
        #set page(paper: "a4")

        = Reproducible

        Today is #datetime.today().display().
        """;

    var first = CompileAndHash(Markup);
    var second = CompileAndHash(Markup);

    Console.WriteLine($"  {first}");
    Console.WriteLine($"  {second}");
    Console.WriteLine($"  identical: {first == second}");

    static string CompileAndHash(string markup)
    {
        using var world = new TypstWorldBuilder().AddSource("main.typ", markup).Build();
        using var result = new TypstCompiler().Compile(world);

        return Convert.ToHexString(SHA256.HashData(result.ExportPdf()));
    }
}

void Section(string title)
{
    Console.WriteLine(title);
    Console.WriteLine(new string('-', title.Length));
}

void Write(string name, byte[] bytes)
{
    var path = Path.Combine(outputDirectory, name);
    File.WriteAllBytes(path, bytes);
    Console.WriteLine($"  wrote {name} ({bytes.Length} bytes)");
}

// One invoice line: the template groups by category and computes the amounts.
static Dictionary<string, object?> Line(
    string category,
    string sku,
    string description,
    string details,
    int quantity,
    decimal unitPrice,
    bool taxable = true) => new()
{
    ["category"] = category,
    ["sku"] = sku,
    ["description"] = description,
    ["details"] = details,
    ["quantity"] = quantity,
    ["unit_price"] = unitPrice,
    ["taxable"] = taxable,
};

static string? FindSystemFont()
{
    string[] candidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        @"C:\Windows\Fonts\segoeui.ttf",
        @"C:\Windows\Fonts\arial.ttf",
    ];

    return candidates.FirstOrDefault(File.Exists);
}
