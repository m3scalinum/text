using M3scalinum.Text.Docx;

// Reads a real .docx through the native reader the package ships, then converts the model to the
// Typst document model: the second half is the package dependency a forgotten reference would break.

var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "report.docx");
var document = DocxReader.Read(File.ReadAllBytes(path));

var paragraphs = document.Blocks.OfType<DocxParagraph>().ToList();
var text = string.Join("\n", paragraphs.Select(paragraph => paragraph.Text));

Console.WriteLine("M3scalinum.Text.Docx");
Console.WriteLine($"  blocks       {document.Blocks.Count} ({paragraphs.Count} paragraphs)");
Console.WriteLine($"  headings     {string.Join(", ", paragraphs
    .Where(paragraph => paragraph.HeadingLevel is not null)
    .Select(paragraph => $"h{paragraph.HeadingLevel} {paragraph.Text}"))}");
Console.WriteLine($"  images       {document.Images.Count}");
Console.WriteLine($"  characters   {text.Length}");

if (document.Blocks.Count == 0 || paragraphs.Count == 0)
{
    throw new InvalidOperationException("the reader returned a document with no paragraph");
}

if (!text.Contains("Quarterly report", StringComparison.Ordinal)
    || !text.Contains("Revenue grew twelve percent", StringComparison.Ordinal))
{
    throw new InvalidOperationException($"the expected text is missing from the document: '{text}'");
}

Console.WriteLine($"  first line   {paragraphs[0].Text}");

// The model to Typst conversion, for a caller that renders the document with M3scalinum.Text.Typst.
var markup = document.ToTypstDocument().ToMarkup();

Console.WriteLine($"  typst        {markup.Length} chars of markup");

if (markup.Length == 0)
{
    throw new InvalidOperationException("the document converted to no Typst markup");
}

Console.WriteLine();
Console.WriteLine("M3scalinum.Text.Docx: the native reader loaded from the package and read the document.");
