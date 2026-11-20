namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>One rule a document breaks.</summary>
/// <param name="Code">The rule identifier, for example <c>BR-CO-13</c>.</param>
/// <param name="Message">What is wrong, naming the values involved.</param>
public sealed record FacturXIssue(string Code, string Message);