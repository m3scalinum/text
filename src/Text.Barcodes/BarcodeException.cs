using System;

namespace M3scalinum.Text.Barcodes;

/// <summary>The category of a <see cref="BarcodeException"/>.</summary>
public enum BarcodeErrorCode
{
    /// <summary>A caller argument was wrong: a null payload, a scale out of range.</summary>
    InvalidArgument,

    /// <summary>The payload cannot be represented in the chosen symbology: a twelve-digit EAN-13,
    /// characters a Code 39 symbol has no encoding for, a payload past the symbol's capacity.</summary>
    Encode,

    /// <summary>The symbology, option or native entry point is not supported by this build.</summary>
    Unsupported,

    /// <summary>The bytes handed to <see cref="Barcode.Decode"/> are not a PNG this build can
    /// read.</summary>
    Decode,

    /// <summary>A hard limit was reached: payload size, raster size.</summary>
    Limit,

    /// <summary>The GS1 data is malformed: an Application Identifier this build does not know, a
    /// value outside the length the standard gives its identifier, a check digit that does not
    /// match, or a Digital Link that is not one.</summary>
    Gs1,

    /// <summary>The native library panicked, or does not speak the ABI this assembly was built
    /// against.</summary>
    Native,
}

/// <summary>
/// The base of every exception this package throws. The native status is mapped once, at the
/// interop boundary, so no caller ever sees an interop shape.
/// </summary>
public class BarcodeException : Exception
{
    /// <summary>Creates an exception with a category and a message.</summary>
    /// <param name="code">The category.</param>
    /// <param name="message">What went wrong, in terms of the caller's input.</param>
    public BarcodeException(BarcodeErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Creates an exception with a category, a message and an inner exception.</summary>
    /// <param name="code">The category.</param>
    /// <param name="message">What went wrong, in terms of the caller's input.</param>
    /// <param name="innerException">The underlying failure.</param>
    public BarcodeException(BarcodeErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>What kind of failure this is.</summary>
    public BarcodeErrorCode Code { get; }
}
