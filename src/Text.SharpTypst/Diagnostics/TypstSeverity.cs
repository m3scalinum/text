namespace M3scalinum.Text.SharpTypst.Diagnostics;

/// <summary>Severity of a compiler diagnostic.</summary>
public enum TypstSeverity
{
    /// <summary>The document could not be produced.</summary>
    Error,

    /// <summary>The document was produced, but something is probably wrong.</summary>
    Warning,

    /// <summary>A suggestion from the compiler.</summary>
    Hint,
}
