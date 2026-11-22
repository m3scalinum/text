namespace M3scalinum.Text.Barcodes;

/// <summary>
/// One Application Identifier this build understands, with the shape of the value that follows it.
/// </summary>
/// <remarks>
/// The list is read from the native library rather than declared here, so a managed build cannot
/// promise an identifier the parser would refuse.
/// </remarks>
public sealed class Gs1ApplicationIdentifier
{
    internal Gs1ApplicationIdentifier(
        string ai,
        string title,
        int minimumLength,
        int maximumLength,
        bool numeric,
        bool checkDigit,
        bool primaryKey)
    {
        Ai = ai;
        Title = title;
        MinimumLength = minimumLength;
        MaximumLength = maximumLength;
        Numeric = numeric;
        CheckDigit = checkDigit;
        PrimaryKey = primaryKey;
    }

    /// <summary>The identifier itself: two to four digits, for example <c>01</c>.</summary>
    public string Ai { get; }

    /// <summary>The GS1 short name, for example <c>GTIN</c> or <c>BATCH/LOT</c>.</summary>
    public string Title { get; }

    /// <summary>Shortest value, in characters.</summary>
    public int MinimumLength { get; }

    /// <summary>Longest value, in characters. Equal to <see cref="MinimumLength"/> when the
    /// identifier is fixed-length.</summary>
    public int MaximumLength { get; }

    /// <summary>Whether the value is digits only.</summary>
    public bool Numeric { get; }

    /// <summary>Whether the last character is a GS1 check digit the parser verifies.</summary>
    public bool CheckDigit { get; }

    /// <summary>Whether the identifier is a Digital Link primary key, which goes in the path of
    /// the URL rather than in its query.</summary>
    public bool PrimaryKey { get; }

    /// <summary>Whether the value has a fixed length.</summary>
    public bool IsFixedLength => MinimumLength == MaximumLength;

    /// <summary>A readable rendering, for logs and for a failing assertion.</summary>
    /// <returns>The identifier, its title and the length range it takes.</returns>
    public override string ToString()
        => IsFixedLength
            ? $"({Ai}) {Title}, {MinimumLength} characters"
            : $"({Ai}) {Title}, {MinimumLength} to {MaximumLength} characters";
}
