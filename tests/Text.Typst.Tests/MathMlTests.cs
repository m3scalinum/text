using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.Documents;
using Xunit;

namespace M3scalinum.Text.Typst.Tests;

/// <summary>
/// The MathML import and the export that closes the loop: the subset that maps onto the model's math
/// nodes, the markup each element writes, the tree a round trip keeps, and the refusals — an unknown
/// element named in the exception, a DTD, a bound, a shape that does not match its element.
/// </summary>
public sealed class MathMlTests
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
    public void A_simple_formula_imports_to_the_expected_typst_markup()
    {
        var expression = TypstMathMlExpression.Import(Pythagorean);

        Assert.Equal("x^2 + y^2 = z^2", expression.ToTypst());
        Assert.True(expression.Display);
    }

    [Fact]
    public void A_display_formula_becomes_a_display_equation()
    {
        var markup = TypstDocument.Create().MathMl(Pythagorean).ToMarkup();

        Assert.Contains("$ x^2 + y^2 = z^2 $", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inline_formula_stays_inline()
    {
        const string Inline = """<math><msup><mi>x</mi><mn>2</mn></msup></math>""";

        var markup = TypstDocument.Create().MathMl(Inline).ToMarkup();

        Assert.Contains("$x^2$", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("$ x^2 $", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_namespace_is_optional()
    {
        Assert.Equal("x", TypstMathMlExpression.Import("<math><mi>x</mi></math>").ToTypst());
    }

    [Fact]
    public void A_fraction_imports_to_frac()
    {
        const string Fraction = """<math display="block"><mfrac><mn>1</mn><mn>2</mn></mfrac></math>""";

        Assert.Equal("frac(1, 2)", TypstMathMlExpression.Import(Fraction).ToTypst());
    }

    [Fact]
    public void A_square_root_imports_to_sqrt()
    {
        const string Root = """<math display="block"><msqrt><mi>x</mi></msqrt></math>""";

        Assert.Equal("sqrt(x)", TypstMathMlExpression.Import(Root).ToTypst());
    }

    [Fact]
    public void An_nth_root_imports_to_root_with_its_index()
    {
        const string Root =
            """<math display="block"><mroot><mi>x</mi><mn>3</mn></mroot></math>""";

        Assert.Equal("root(x, 3)", TypstMathMlExpression.Import(Root).ToTypst());
    }

    [Fact]
    public void An_under_script_element_is_refused_by_name()
    {
        const string Sum =
            """
            <math display="block">
              <munder><mo>+</mo></munder>
            </math>
            """;

        // munder is outside the subset; the refusal must name it rather than drop the script.
        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(Sum));

        Assert.Equal(TypstMathMlError.Unsupported, refusal.Error);
        Assert.Equal("munder", refusal.ElementName);
    }

    [Fact]
    public void A_grouped_base_with_a_script_uses_attach()
    {
        const string Grouped =
            """
            <math display="block">
              <msup><mrow><mi>x</mi><mo>+</mo><mi>y</mi></mrow><mn>2</mn></msup>
            </math>
            """;

        Assert.Equal("attach(x + y, t: 2)", TypstMathMlExpression.Import(Grouped).ToTypst());
    }

    [Fact]
    public void A_sub_and_superscript_imports_to_both_scripts()
    {
        const string Both =
            """
            <math display="block">
              <msubsup><mi>x</mi><mn>1</mn><mn>2</mn></msubsup>
            </math>
            """;

        Assert.Equal("x_1^2", TypstMathMlExpression.Import(Both).ToTypst());
    }

    [Fact]
    public void A_named_operator_is_written_with_its_typst_name()
    {
        const string Product = """<math display="block"><mn>2</mn><mo>×</mo><mi>n</mi></math>""";

        Assert.Equal("2 times n", TypstMathMlExpression.Import(Product).ToTypst());
    }

    [Fact]
    public void A_multi_letter_identifier_is_upright()
    {
        const string Sine = """<math display="block"><mi>sin</mi></math>""";

        Assert.Equal("upright(\"sin\")", TypstMathMlExpression.Import(Sine).ToTypst());
    }

    [Fact]
    public void A_matrix_imports_to_mat()
    {
        const string Matrix =
            """
            <math display="block">
              <mtable>
                <mtr><mtd><mi>a</mi></mtd><mtd><mi>b</mi></mtd></mtr>
                <mtr><mtd><mi>c</mi></mtd><mtd><mi>d</mi></mtd></mtr>
              </mtable>
            </math>
            """;

        Assert.Equal("mat(a, b; c, d)", TypstMathMlExpression.Import(Matrix).ToTypst());
    }

    [Fact]
    public void A_matrix_cell_holding_a_comma_is_wrapped_so_mat_keeps_it_whole()
    {
        const string Matrix =
            """
            <math display="block">
              <mtable>
                <mtr>
                  <mtd><mrow><mi>f</mi><mo>(</mo><mi>x</mi><mo>,</mo><mi>y</mi><mo>)</mo></mrow></mtd>
                  <mtd><mn>0</mn></mtd>
                </mtr>
              </mtable>
            </math>
            """;

        Assert.Equal("mat(#[$ f ( x , y ) $], 0)", TypstMathMlExpression.Import(Matrix).ToTypst());
    }

    [Fact]
    public void A_round_trip_through_the_model_preserves_the_tree()
    {
        const string Formula =
            """
            <math xmlns="http://www.w3.org/1998/Math/MathML" display="block">
              <mrow>
                <mfrac><mrow><mn>1</mn><mo>+</mo><msqrt><mi>x</mi></msqrt></mrow><mn>2</mn></mfrac>
                <mo>=</mo>
                <msub><mi>a</mi><mn>0</mn></msub>
                <mo>+</mo>
                <mtable>
                  <mtr><mtd><mi>p</mi></mtd><mtd><mi>q</mi></mtd></mtr>
                </mtable>
              </mrow>
            </math>
            """;

        var imported = TypstMathMlExpression.Import(Formula);
        var exported = imported.ToMathMl();
        var again = TypstMathMlExpression.Import(exported);

        AssertSameTree(imported.Children, again.Children);
        Assert.Equal(imported.Display, again.Display);
        Assert.Equal(exported, again.ToMathMl());
    }

    [Fact]
    public void The_html_export_writes_the_formula_as_mathml()
    {
        var result = TypstDocument.Create().MathMl(Pythagorean).ToHtml();

        Assert.Equal(
            "<div class=\"math\"><math xmlns=\"http://www.w3.org/1998/Math/MathML\" display=\"block\">"
            + "<mrow><msup><mi>x</mi><mn>2</mn></msup><mo>+</mo><msup><mi>y</mi><mn>2</mn></msup>"
            + "<mo>=</mo><msup><mi>z</mi><mn>2</mn></msup></mrow></math></div>\n",
            result.Html);
        Assert.Equal(0, result.UnrepresentableCount);
    }

    [Fact]
    public void The_html_export_writes_inline_mathml_inside_a_paragraph()
    {
        const string Inline = """<math><mi>x</mi></math>""";

        var document = TypstDocument.Create().Paragraph(c => c.Text("so ").MathMl(Inline).Text(" is x"));

        Assert.Equal(
            "<p>so <math xmlns=\"http://www.w3.org/1998/Math/MathML\" display=\"inline\"><mi>x</mi></math>"
            + " is x</p>\n",
            document.ToHtml().Html);
    }

    [Fact]
    public void An_unknown_element_is_refused_by_name()
    {
        const string Phantom = """<math><mphantom><mi>x</mi></mphantom></math>""";

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(Phantom));

        Assert.Equal(TypstMathMlError.Unsupported, refusal.Error);
        Assert.Equal("mphantom", refusal.ElementName);
        Assert.Contains("mphantom", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_element_deep_in_the_tree_is_refused_by_name()
    {
        const string Nested =
            """<math><mrow><mfrac><mi>x</mi><menclose><mi>y</mi></menclose></mfrac></mrow></math>""";

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(Nested));

        Assert.Equal("menclose", refusal.ElementName);
    }

    [Fact]
    public void A_document_type_declaration_is_refused()
    {
        const string WithDtd =
            """
            <?xml version="1.0"?>
            <!DOCTYPE math [<!ELEMENT math ANY>]>
            <math><mi>x</mi></math>
            """;

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(WithDtd));

        Assert.Equal(TypstMathMlError.XmlPolicy, refusal.Error);
        Assert.Contains("document type", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entity_declaration_is_refused()
    {
        const string Entity =
            """
            <!DOCTYPE math [<!ENTITY bomb "boom">]>
            <math><mi>&bomb;</mi></math>
            """;

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(Entity));

        Assert.Equal(TypstMathMlError.XmlPolicy, refusal.Error);
    }

    [Fact]
    public void A_formula_over_the_character_cap_is_refused()
    {
        var huge = "<math><mi>" + new string('x', 1024 * 1024) + "</mi></math>";

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(huge));

        Assert.Equal(TypstMathMlError.Limit, refusal.Error);
    }

    [Fact]
    public void A_formula_nested_past_the_depth_cap_is_refused()
    {
        var deep = "<math>" + string.Concat(Enumerable.Repeat("<mrow>", 70)) + "<mi>x</mi>"
            + string.Concat(Enumerable.Repeat("</mrow>", 70)) + "</math>";

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(deep));

        Assert.Equal(TypstMathMlError.Limit, refusal.Error);
    }

    [Fact]
    public void A_formula_that_is_not_well_formed_is_refused()
    {
        var refusal = Assert.Throws<TypstMathMlException>(
            () => TypstMathMlExpression.Import("<math><mi>x</math>"));

        Assert.Equal(TypstMathMlError.Malformed, refusal.Error);
    }

    [Fact]
    public void An_element_with_the_wrong_number_of_children_is_refused()
    {
        const string Short = """<math><msup><mi>x</mi></msup></math>""";

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(Short));

        Assert.Equal(TypstMathMlError.Malformed, refusal.Error);
        Assert.Equal("msup", refusal.ElementName);
    }

    [Fact]
    public void Text_outside_a_token_element_is_refused()
    {
        const string Loose = """<math><mrow>+</mrow></math>""";

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(Loose));

        Assert.Equal(TypstMathMlError.Malformed, refusal.Error);
        Assert.Contains("token element", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_element_holding_an_element_is_refused()
    {
        const string NestedToken = """<math><mi><mn>2</mn></mi></math>""";

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(NestedToken));

        Assert.Equal(TypstMathMlError.Malformed, refusal.Error);
        Assert.Equal("mi", refusal.ElementName);
    }

    [Fact]
    public void A_root_element_that_is_not_math_is_refused()
    {
        const string Wrong = """<mrow><mi>x</mi></mrow>""";

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(Wrong));

        Assert.Equal(TypstMathMlError.Unsupported, refusal.Error);
        Assert.Equal("mrow", refusal.ElementName);
    }

    [Fact]
    public void An_empty_math_element_is_refused()
    {
        var refusal = Assert.Throws<TypstMathMlException>(
            () => TypstMathMlExpression.Import("<math></math>"));

        Assert.Equal(TypstMathMlError.Malformed, refusal.Error);
    }

    [Fact]
    public void A_table_whose_rows_differ_in_width_is_refused()
    {
        const string Ragged =
            """
            <math>
              <mtable>
                <mtr><mtd><mi>a</mi></mtd><mtd><mi>b</mi></mtd></mtr>
                <mtr><mtd><mi>c</mi></mtd></mtr>
              </mtable>
            </math>
            """;

        var refusal = Assert.Throws<TypstMathMlException>(() => TypstMathMlExpression.Import(Ragged));

        Assert.Equal(TypstMathMlError.Malformed, refusal.Error);
        Assert.Equal("mtable", refusal.ElementName);
    }

    [Fact]
    public void A_blank_formula_is_refused_as_an_argument()
    {
        Assert.Throws<ArgumentException>(() => TypstMathMlExpression.Import("  "));
    }

    [Fact]
    public void A_null_formula_is_refused_as_an_argument()
    {
        Assert.Throws<ArgumentNullException>(() => TypstMathMlExpression.Import(null!));
    }

    [Fact]
    public void The_document_builder_refuses_a_null_formula()
    {
        Assert.Throws<ArgumentNullException>(() => TypstDocument.Create().MathMl((string)null!));
        Assert.Throws<ArgumentNullException>(() => TypstDocument.Create().MathMl((TypstMathMlExpression)null!));
    }

    [Fact]
    public void The_markdown_export_carries_the_imported_formula()
    {
        var markdown = TypstDocument.Create().MathMl(Pythagorean).ToMarkdown();

        Assert.Contains("x^2 + y^2 = z^2", markdown.Markdown, StringComparison.Ordinal);
    }

    private static void AssertSameTree(
        IReadOnlyList<TypstMathMlElement> expected,
        IReadOnlyList<TypstMathMlElement> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Name, actual[index].Name);
            Assert.Equal(expected[index].Text, actual[index].Text);
            AssertSameTree(expected[index].Children, actual[index].Children);
        }
    }
}
