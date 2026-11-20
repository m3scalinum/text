using System;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>An invoice document that is not valid CII or UBL.</summary>
public sealed class FacturXFormatException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public FacturXFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    public FacturXFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}