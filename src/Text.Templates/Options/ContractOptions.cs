using M3scalinum.Text.Typst.Templates;

namespace M3scalinum.Text.Templates;

/// <summary>
/// Typed parameters of the contract layout (<see cref="TemplateIds.Contract"/>), generated from
/// <c>layouts/contract/template.toml</c>.
/// </summary>
[TypstTemplate("layouts/contract")]
public partial record ContractOptions;
