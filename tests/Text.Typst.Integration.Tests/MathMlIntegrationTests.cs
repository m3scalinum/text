using System;
using M3scalinum.Text.Documents;
using M3scalinum.Text.Typst.Compilation;
using Xunit;

namespace M3scalinum.Text.Typst.Integration.Tests;

/// <summary>
/// MathML imported into the model, laid out by the real compiler: the markup the import writes has to
/// be Typst the compiler accepts, and the glyphs it lays out have to be the ones the formula names.
/// </summary>
/// <remarks>
/// The formulas cover every element of the subset that reaches the rendering differently — a script,
/// a fraction, a root, a matrix and the <c>attach()</c> a grouped base takes — because a markup that
/// parses is not yet a markup that means what the MathML said.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class MathMlIntegrationTests
{
    private const string Pythagorean =
        """
        <math xmlns="http://www.w3.org/1998/Math/MathML" display="block">
          <mrow>
            <msup><mi>x</mi><mn>2</mn></msup>
            <mo>+</mo>
            <msup><mi>y</mi><mn>2</mn></msup>
            <mo>=</mo>
            <msup><mi>z</mi><mn>2</mn></msup>
          </mrow>
        </math>
        """;

    [Fact]
    public void A_formula_imported_from_mathml_compiles()
    {
        Compiled(TypstDocument.Create().MathMl(Pythagorean), result =>
        {
            Assert.Empty(result.Diagnostics);
            Assert.True(result.PageCount >= 1);
        });
    }

    [Fact]
    public void The_extracted_text_carries_the_symbols_of_the_formula()
    {
        Compiled(TypstDocument.Create().MathMl(Pythagorean), result =>
        {
            var text = result.ExtractText(0);

            // The layout draws math letters in the mathematical italic block, so the extracted text
            // carries U+1D465 etc., not ASCII: the test asserts the glyph the compiler really laid
            // down, and that the formula reached the page as a formula.
            Assert.Contains("𝑥", text, StringComparison.Ordinal);
            Assert.Contains("𝑦", text, StringComparison.Ordinal);
            Assert.Contains("𝑧", text, StringComparison.Ordinal);
            Assert.Contains("=", text, StringComparison.Ordinal);
            Assert.Contains("+", text, StringComparison.Ordinal);
            Assert.Contains("2", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_fraction_and_a_root_compile()
    {
        const string Formula =
            """
            <math display="block">
              <mrow>
                <mfrac><mrow><mi>a</mi><mo>+</mo><mi>b</mi></mrow><mn>2</mn></mfrac>
                <mo>=</mo>
                <msqrt><mfrac><mn>1</mn><mn>4</mn></mfrac></msqrt>
                <mo>+</mo>
                <mroot><mi>c</mi><mn>3</mn></mroot>
              </mrow>
            </math>
            """;

        Compiled(TypstDocument.Create().MathMl(Formula), result =>
        {
            Assert.Empty(result.Diagnostics);
            Assert.Contains("𝑎", result.ExtractText(0), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_matrix_and_a_grouped_base_compile()
    {
        const string Formula =
            """
            <math display="block">
              <mrow>
                <mtable>
                  <mtr><mtd><mi>a</mi></mtd><mtd><mi>b</mi></mtd></mtr>
                  <mtr><mtd><mi>c</mi></mtd><mtd><mi>d</mi></mtd></mtr>
                </mtable>
                <mo>+</mo>
                <msup><mrow><mi>p</mi><mo>+</mo><mi>q</mi></mrow><mn>2</mn></msup>
              </mrow>
            </math>
            """;

        Compiled(TypstDocument.Create().MathMl(Formula), result => Assert.Empty(result.Diagnostics));
    }

    [Fact]
    public void A_formula_imported_inline_sits_in_a_paragraph()
    {
        var document = TypstDocument.Create()
            .Paragraph(c => c
                .Text("La relation ")
                .MathMl("""<math><mi>e</mi></math>""")
                .Text(" est une constante."));

        Compiled(document, result =>
        {
            Assert.Empty(result.Diagnostics);
            Assert.Contains("est une constante", result.ExtractText(0), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_html_export_reimports_to_the_same_tree()
    {
        var document = TypstDocument.Create().MathMl(Pythagorean);
        var html = document.ToHtml().Html;
        var start = html.IndexOf("<math", StringComparison.Ordinal);
        var end = html.IndexOf("</math>", StringComparison.Ordinal) + "</math>".Length;

        var reimported = TypstMathMlExpression.Import(html[start..end]);

        Assert.Equal(
            TypstMathMlExpression.Import(Pythagorean).ToMathMl(),
            reimported.ToMathMl());
    }

    private static void Compiled(TypstDocument document, Action<CompilationResult> assert)
    {
        using var world = new TypstWorldBuilder()
            .AddSource("main.typ", document.ToMarkup())
            .Build();
        using var result = new TypstCompiler().Compile(world);

        assert(result);
    }
}
