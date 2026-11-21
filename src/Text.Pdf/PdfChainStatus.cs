namespace M3scalinum.Text.Pdf;

/// <summary>Whether the signer's certificate chains to the trust store the caller supplied.</summary>
/// <remarks>
/// The machine's trust store is never consulted: a verification that succeeds because the operating
/// system happens to trust a root says nothing about the caller's policy, so a verifier that reports
/// it is worse than one that reports nothing.
/// </remarks>
public enum PdfChainStatus
{
    /// <summary>No trust store was supplied, or the signature carries no certificate to chain.</summary>
    NotEvaluated = 0,

    /// <summary>The chain reaches a certificate of the supplied store, and every element is valid.</summary>
    Trusted = 1,

    /// <summary>The chain does not reach the supplied store, or an element is expired or unusable.</summary>
    Untrusted = 2,
}
