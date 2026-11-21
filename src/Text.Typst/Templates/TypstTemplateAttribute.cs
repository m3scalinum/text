using System;

namespace M3scalinum.Text.Typst.Templates;

/// <summary>
/// Marks a partial type as the typed options record of a template. The source generator reads the
/// template's <c>template.toml</c> and emits one property per declared parameter, plus
/// <see cref="ITypstTemplateOptions.ToParameters"/>.
/// </summary>
/// <remarks>
/// <para>
/// The type must be <see langword="partial"/> and must not declare a primary constructor. The
/// generator adds the properties, so the declaration is just:
/// </para>
/// <code>
/// [TypstTemplate("templates/invoice")]
/// public partial record InvoiceOptions;
/// </code>
/// <para>
/// The path is relative to the project directory and points at the directory holding
/// <c>template.toml</c>. The file must be an <c>AdditionalFiles</c> item; the generator package
/// adds every <c>template.toml</c> in the project automatically.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class TypstTemplateAttribute : Attribute
{
    /// <summary>Creates the attribute for the template at <paramref name="path"/>.</summary>
    /// <param name="path">Directory holding <c>template.toml</c>, relative to the project.</param>
    public TypstTemplateAttribute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>Directory holding <c>template.toml</c>, relative to the project.</summary>
    public string Path { get; }
}
