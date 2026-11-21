using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>What a verifier established about a PDF's signatures.</summary>
/// <remarks>
/// One entry per signature dictionary that is not a document timestamp, in the order the revisions
/// were written. A document that carries no signature has an empty <see cref="Signatures"/>, which
/// <see cref="IsSigned"/> reports; that is not an error, it is a document that was never signed.
/// </remarks>
public sealed class PdfSignatureVerification
{
    internal PdfSignatureVerification(
        IReadOnlyList<PdfSignatureInfo> signatures,
        IReadOnlyList<PdfDocumentTimestampInfo> documentTimestamps)
    {
        Signatures = signatures;
        DocumentTimestamps = documentTimestamps;
    }

    /// <summary>The signatures, oldest first.</summary>
    public IReadOnlyList<PdfSignatureInfo> Signatures { get; }

    /// <summary>The document timestamps, oldest first.</summary>
    public IReadOnlyList<PdfDocumentTimestampInfo> DocumentTimestamps { get; }

    /// <summary>Whether the document carries at least one signature.</summary>
    public bool IsSigned => Signatures.Count > 0;

    /// <summary>Whether every signature verifies over the bytes it covers.</summary>
    public bool AllIntact
    {
        get
        {
            foreach (var signature in Signatures)
            {
                if (!signature.IsIntact)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Whether every signature covers the whole file. A document signed once and never touched again
    /// answers true; a document with a document timestamp appended after the signature answers false,
    /// because the timestamp is part of the file and no signature covers it.
    /// </summary>
    public bool AllCoverWholeFile
    {
        get
        {
            foreach (var signature in Signatures)
            {
                if (!signature.CoversWholeFile)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Whether the document carries at least one document timestamp.</summary>
    public bool HasDocumentTimestamp => DocumentTimestamps.Count > 0;

    /// <summary>Whether every document timestamp verifies.</summary>
    public bool AllDocumentTimestampsValid
    {
        get
        {
            foreach (var timestamp in DocumentTimestamps)
            {
                if (!timestamp.IsIntact)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// The weakest level proven across the signatures: a document is only as strong as its weakest
    /// signature. <see cref="PdfSignatureLevel.None"/> for a document that carries none.
    /// </summary>
    public PdfSignatureLevel ProvenLevel
    {
        get
        {
            if (Signatures.Count == 0)
            {
                return PdfSignatureLevel.None;
            }

            var level = PdfSignatureLevel.BaselineLT;

            foreach (var signature in Signatures)
            {
                if (signature.Level < level)
                {
                    level = signature.Level;
                }
            }

            return level;
        }
    }
}
