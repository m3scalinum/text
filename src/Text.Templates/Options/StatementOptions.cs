using M3scalinum.Text.Typst.Templates;

namespace M3scalinum.Text.Templates;

/// <summary>
/// Typed parameters of the account-statement layout (<see cref="TemplateIds.Statement"/>),
/// generated from <c>layouts/statement/template.toml</c>.
/// </summary>
[TypstTemplate("layouts/statement")]
public partial record StatementOptions;
