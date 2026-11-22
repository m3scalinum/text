namespace M3scalinum.Text.Barcodes;

/// <summary>
/// One element of a GS1 element string: an Application Identifier and the value that follows it.
/// </summary>
public sealed class Gs1Element
{
    internal Gs1Element(string ai, string title, string value, bool primaryKey)
    {
        Ai = ai;
        Title = title;
        Value = value;
        PrimaryKey = primaryKey;
    }

    /// <summary>The Application Identifier, for example <c>01</c>.</summary>
    public string Ai { get; }

    /// <summary>The GS1 short name of the identifier, for example <c>GTIN</c>.</summary>
    public string Title { get; }

    /// <summary>The value, exactly as it appeared in the data.</summary>
    public string Value { get; }

    /// <summary>Whether this identifier is a Digital Link primary key.</summary>
    public bool PrimaryKey { get; }

    /// <summary>The printed form of this element: <c>(01)09501101530003</c>.</summary>
    /// <returns>The identifier in parentheses, followed by the value.</returns>
    public override string ToString() => $"({Ai}){Value}";
}
