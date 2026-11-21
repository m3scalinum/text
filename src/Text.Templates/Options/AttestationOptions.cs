using M3scalinum.Text.Typst.Templates;

namespace M3scalinum.Text.Templates;

/// <summary>
/// Typed parameters of the attestation layout (<see cref="TemplateIds.Attestation"/>), generated
/// from <c>layouts/attestation/template.toml</c>.
/// </summary>
[TypstTemplate("layouts/attestation")]
public partial record AttestationOptions;
