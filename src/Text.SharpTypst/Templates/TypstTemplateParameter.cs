namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>A parameter declared by a template manifest.</summary>
public sealed class TypstTemplateParameter
{
    internal TypstTemplateParameter(
        string name,
        TypstParameterType type,
        bool required,
        object? defaultValue,
        TypstParameterType? itemType)
    {
        Name = name;
        Type = type;
        Required = required;
        Default = defaultValue;
        ItemType = itemType;
    }

    /// <summary>Parameter name, as used in the template's <c>render</c> function.</summary>
    public string Name { get; }

    /// <summary>Declared type.</summary>
    public TypstParameterType Type { get; }

    /// <summary>Whether the caller must supply a value.</summary>
    public bool Required { get; }

    /// <summary>Value used when the caller passes <see langword="null"/>.</summary>
    public object? Default { get; }

    /// <summary>Element type of an <see cref="TypstParameterType.Array"/> parameter.</summary>
    public TypstParameterType? ItemType { get; }
}
