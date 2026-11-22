using M3scalinum.Text.Typst;
using M3scalinum.Text.Typst.Compilation;

// Compiles a minimal Typst source to PDF through the native compiler the package ships. The page
// count comes from the compilation itself, so an empty render fails here rather than downstream.

const string Source = """
    = M3scalinum.Text.Typst sample

    A paragraph compiled by the native Typst library shipped in the package.

    #table(
      columns: 2,
      [Region], [Amount],
      [Ile-de-France], [1 200,00 €],
    )
    """;

Console.WriteLine("M3scalinum.Text.Typst");
Console.WriteLine($"  assemblies   {TypstEnvironment.LibraryVersion}, compiler {TypstEnvironment.TypstVersion}");

using var world = new TypstWorldBuilder().AddSource("main.typ", Source).Build();
using var compilation = new TypstCompiler().Compile(world);

if (compilation.Diagnostics.Count > 0)
{
    foreach (var diagnostic in compilation.Diagnostics)
    {
        Console.WriteLine($"  diagnostic   {diagnostic.Severity}: {diagnostic.Message}");
    }
}

var pdf = compilation.ExportPdf();
var svg = compilation.ExportSvg(0);

Console.WriteLine($"  compiled     {compilation.PageCount} page(s), pdf {pdf.Length} bytes,"
    + $" svg {svg.Length} bytes");

if (compilation.PageCount == 0 || pdf.Length == 0 || svg.Length == 0)
{
    throw new InvalidOperationException("the compiler produced an empty document");
}

if (pdf.Length < 5 || pdf[0] != '%' || pdf[1] != 'P' || pdf[2] != 'D' || pdf[3] != 'F')
{
    throw new InvalidOperationException("the export did not produce a PDF header");
}

var outline = compilation.Evaluate().Outline;

if (outline.Count == 0)
{
    throw new InvalidOperationException("the compiled document has no heading in its outline");
}

Console.WriteLine($"  outline      {string.Join(" > ", outline.Select(entry => entry.Title))}");
Console.WriteLine();
Console.WriteLine("M3scalinum.Text.Typst: the native compiler loaded from the package and compiled.");
