namespace M3scalinum.Text.Pdf;

/// <summary>The PAdES baseline level a verifier could actually establish for a signature.</summary>
/// <remarks>
/// The names follow ETSI EN 319 142 rather than the colloquial B-B/B-T/B-LT: each value says what was
/// <em>proven</em>, not what the signature claims. A signature dictionary that declares itself PAdES
/// while its timestamp token does not verify is reported as <see cref="BaselineB"/>, whatever the
/// file says about itself.
/// </remarks>
public enum PdfSignatureLevel
{
    /// <summary>Nothing could be established: the signature does not verify.</summary>
    None = 0,

    /// <summary>
    /// PAdES-B-B: the CMS verifies over the bytes the signature covers, and the signer's certificate
    /// is embedded in it.
    /// </summary>
    BaselineB = 1,

    /// <summary>
    /// PAdES-B-T: <see cref="BaselineB"/>, plus a signature timestamp whose imprint is this
    /// signature's value and whose own CMS verifies.
    /// </summary>
    BaselineT = 2,

    /// <summary>
    /// PAdES-B-LT: <see cref="BaselineT"/>, plus a document security store that embeds every
    /// certificate of the chain and carries revocation evidence for each of them.
    /// </summary>
    BaselineLT = 3,

    /// <summary>
    /// PAdES-B-LTA: <see cref="BaselineLT"/>, plus a document timestamp that covers the whole file —
    /// the store included — whose own certificate chain the store embeds. The timestamp says the
    /// store existed when the authority issued it, so the evidence survives the moment the
    /// revocation data stops being current.
    /// </summary>
    BaselineLTA = 4,
}
