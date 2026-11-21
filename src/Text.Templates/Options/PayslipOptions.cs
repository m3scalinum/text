using M3scalinum.Text.Typst.Templates;

namespace M3scalinum.Text.Templates;

/// <summary>
/// Typed parameters of the payslip layout (<see cref="TemplateIds.Payslip"/>), generated from
/// <c>layouts/payslip/template.toml</c>.
/// </summary>
[TypstTemplate("layouts/payslip")]
public partial record PayslipOptions;
