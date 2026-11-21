using M3scalinum.Text.Typst.Templates;

namespace M3scalinum.Text.Templates;

/// <summary>
/// Typed parameters of the invoice layout (<see cref="TemplateIds.Invoice"/>), generated from
/// <c>layouts/invoice/template.toml</c>: one property per declared parameter, so a renamed
/// parameter is a build error rather than a render-time one.
/// </summary>
[TypstTemplate("layouts/invoice")]
public partial record InvoiceOptions;
