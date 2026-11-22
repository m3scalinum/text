namespace M3scalinum.Text.Pdf;

/// <summary>The PDF could not be signed.</summary>
public class PdfSignatureException : Exception
{
    /// <summary>Initialises the exception.</summary>
    public PdfSignatureException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}