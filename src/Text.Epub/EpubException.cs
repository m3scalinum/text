using System;

namespace M3scalinum.Text.Epub;

/// <summary>Base class for the failures this package raises.</summary>
public class EpubException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public EpubException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    public EpubException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A book or a chapter that cannot become a valid EPUB: an empty required metadata value, a name
/// that would leave the container, a reference no manifest item backs, or an id that collides.
/// </summary>
public sealed class EpubValidationException : EpubException
{
    /// <summary>Creates the exception with a message.</summary>
    public EpubValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    public EpubValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Input that is not HTML this package can read, or a fragment it cannot make well-formed.</summary>
public sealed class EpubFormatException : EpubException
{
    /// <summary>Creates the exception with a message.</summary>
    public EpubFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    public EpubFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
