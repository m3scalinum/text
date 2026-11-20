using System.Diagnostics.CodeAnalysis;

namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>Type of a template parameter, as declared in <c>template.toml</c>.</summary>
/// <remarks>
/// The member names mirror the manifest vocabulary (<c>string</c>, <c>object</c>) rather than
/// the .NET type names, so a manifest and its enum read the same way.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Members mirror the manifest's type names, which are Typst's vocabulary.")]
public enum TypstParameterType
{
    /// <summary>A string.</summary>
    String,

    /// <summary>An integer or a real number.</summary>
    Number,

    /// <summary><see langword="true"/> or <see langword="false"/>.</summary>
    Boolean,

    /// <summary>A calendar date, emitted as a Typst <c>datetime</c>.</summary>
    Date,

    /// <summary>A virtual path to a registered asset.</summary>
    Asset,

    /// <summary>A sequence; <see cref="TypstTemplateParameter.ItemType"/> gives the element type.</summary>
    Array,

    /// <summary>A dictionary with string keys.</summary>
    Object,
}
