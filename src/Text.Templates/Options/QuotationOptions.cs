using M3scalinum.Text.Typst.Templates;

namespace M3scalinum.Text.Templates;

/// <summary>
/// Typed parameters of the quotation layout (<see cref="TemplateIds.Quotation"/>), generated from
/// <c>layouts/quotation/template.toml</c>.
/// </summary>
[TypstTemplate("layouts/quotation")]
public partial record QuotationOptions;
