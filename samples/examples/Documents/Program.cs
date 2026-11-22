using M3scalinum.Text.Documents;

// Builds a document with the model, then writes it twice: Markdown and HTML. Both exports are pure
// managed code, so this example proves the package stands alone — no compiler, no native library.

var document = TypstDocument.Create()
    .Heading(1, "Revenue")
    .Paragraph(content => content
        .Text("Revenue grew by ")
        .Bold("12%")
        .Text(" year over year, driven by ")
        .Emphasis("subscriptions")
        .Text("."))
    .BulletList("Hosting", "Support")
    .Table(table => table
        .Columns(TypstLength.Fr(2), TypstLength.Fr(1))
        .HeaderRow("Item", "Amount")
        .Row("Hosting", "1 200 EUR")
        .Row("Support", "800 EUR"));

var markdown = document.ToMarkdown();
var html = document.ToHtml();

Console.WriteLine("M3scalinum.Text.Documents");
Console.WriteLine($"  markup       {document.ToMarkup().Length} chars of Typst markup");
Console.WriteLine($"  markdown     {markdown.Markdown.Length} chars,"
    + $" {markdown.UnrepresentableCount} construct(s) dropped");
Console.WriteLine($"  html         {html.Html.Length} chars, {html.UnrepresentableCount} element(s) dropped");

if (markdown.Markdown.Length == 0 || html.Html.Length == 0)
{
    throw new InvalidOperationException("an export produced no text");
}

if (!markdown.Markdown.Contains("**12%**", StringComparison.Ordinal))
{
    throw new InvalidOperationException($"the bold run is missing from the Markdown: '{markdown.Markdown}'");
}

if (!html.Html.Contains("<strong>12%</strong>", StringComparison.Ordinal))
{
    throw new InvalidOperationException($"the bold run is missing from the HTML: '{html.Html}'");
}

Console.WriteLine("  excerpt      " + markdown.Markdown.Split('\n')[2]);

// A round trip through the Markdown importer, which is the other half of the same package.
var imported = TypstDocument.FromMarkdown(markdown.Markdown);

Console.WriteLine($"  re-imported  {imported.ToMarkup().Length} chars of markup");

if (imported.ToMarkup().Length == 0)
{
    throw new InvalidOperationException("the Markdown importer read nothing back");
}

Console.WriteLine();
Console.WriteLine("M3scalinum.Text.Documents: the model and both exporters ran from the package alone.");
