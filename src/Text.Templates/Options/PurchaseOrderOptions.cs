using M3scalinum.Text.Typst.Templates;

namespace M3scalinum.Text.Templates;

/// <summary>
/// Typed parameters of the purchase-order layout (<see cref="TemplateIds.PurchaseOrder"/>), generated
/// from <c>layouts/purchase-order/template.toml</c>.
/// </summary>
[TypstTemplate("layouts/purchase-order")]
public partial record PurchaseOrderOptions;
