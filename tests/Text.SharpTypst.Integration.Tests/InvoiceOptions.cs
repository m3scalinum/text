using M3scalinum.Text.SharpTypst.Templates;

namespace M3scalinum.Text.SharpTypst.Integration.Tests;

/// <summary>
/// The options record is generated from <c>templates/invoice/template.toml</c> at compile time, so
/// this file only declares the type: a missing or renamed parameter is a build error, not a
/// runtime one.
/// </summary>
[TypstTemplate("templates/invoice")]
public partial record InvoiceOptions;