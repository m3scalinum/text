namespace M3scalinum.Text.Templates.Generator;

/// <summary>One parameter declared by a template manifest.</summary>
internal sealed class TemplateParameter
{
    public TemplateParameter(string name, string type, string? itemType, bool required, object? defaultValue)
    {
        Name = name;
        Type = type;
        ItemType = itemType;
        Required = required;
        Default = defaultValue;
    }

    /// <summary>Name as written in the manifest, which is also the Typst parameter name.</summary>
    public string Name { get; }

    /// <summary>Declared type: <c>string</c>, <c>number</c>, <c>boolean</c>, <c>date</c>, <c>asset</c>, <c>array</c> or <c>object</c>.</summary>
    public string Type { get; }

    /// <summary>Element type of an <c>array</c> parameter.</summary>
    public string? ItemType { get; }

    /// <summary>Whether the caller must supply a value.</summary>
    public bool Required { get; }

    /// <summary>Value used when the caller omits the parameter.</summary>
    public object? Default { get; }
}
