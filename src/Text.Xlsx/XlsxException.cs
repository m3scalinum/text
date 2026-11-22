using System;

namespace M3scalinum.Text.Xlsx;

/// <summary>A workbook could not be read.</summary>
/// <remarks>
/// This is for the reasons that leave nothing to read: the bytes are not a package, the package is
/// over the size limit, or a part of it does not parse. A cell holding a type this reader does not
/// represent never throws — it is reported as an error cell, and
/// <c>docs/specifications/28-xlsx.md</c> says what.
/// </remarks>
public sealed class XlsxException : Exception
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="code">Why the workbook could not be read.</param>
    /// <param name="message">What went wrong, in terms the caller can act on.</param>
    public XlsxException(XlsxErrorCode code, string message)
        : base(message) => Code = code;

    /// <summary>Initialises the exception around another failure.</summary>
    /// <param name="code">Why the workbook could not be read.</param>
    /// <param name="message">What went wrong, in terms the caller can act on.</param>
    /// <param name="innerException">The failure this one wraps.</param>
    public XlsxException(XlsxErrorCode code, string message, Exception innerException)
        : base(message, innerException) => Code = code;

    /// <summary>Why the workbook could not be read.</summary>
    public XlsxErrorCode Code { get; }
}
