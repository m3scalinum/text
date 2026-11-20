namespace M3scalinum.Text.Pdf;

/// <summary>What a signature says about itself, beyond the certificate.</summary>
public sealed class PdfSignatureOptions
{
    /// <summary>The field name a reader shows. Default <c>Signature1</c>.</summary>
    public string FieldName { get; init; } = "Signature1";

    /// <summary>Why the document is signed, as the signer declares it. Default: none.</summary>
    public string? Reason { get; init; }

    /// <summary>Where the signature was made. Default: none.</summary>
    public string? Location { get; init; }

    /// <summary>
    /// The moment the signature claims, in UTC. Default: the current time.
    /// </summary>
    /// <remarks>
    /// Set it when the signature must be reproducible: the same document, certificate and timestamp
    /// give the same bytes, which a test can then assert on.
    /// </remarks>
    public DateTimeOffset? SignedAt { get; init; }
}

