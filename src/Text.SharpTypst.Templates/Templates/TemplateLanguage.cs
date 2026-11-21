namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>
/// A language a layout's labels are supplied in. The layouts carry no text of their own: every
/// word they print comes from the label set the caller passes, so this enum only names the sets
/// that ship with the package.
/// </summary>
public enum TemplateLanguage
{
    /// <summary>English labels: ISO-style dates, a space-free thousands separator.</summary>
    English,

    /// <summary>French labels: day-first dates and the French number separators.</summary>
    French,
}
