namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// The comparison base of <see cref="TypstCompiler.CompileChanged"/>: one fingerprint per page of
/// the previous compilation of a document.
/// </summary>
/// <remarks>
/// <para>
/// Hold one instance per document, next to the world you edit. It carries no native handle — only
/// a 64-bit digest per page — so it outlives every document it compares and costs a few bytes per
/// page. It does not keep the previous compilation alive, and comparing against it never needs the
/// previous document to still exist.
/// </para>
/// <para>
/// Not thread-safe, by design: one state belongs to one document edited by one caller. A preview
/// that compiles the same document from two threads at once needs one state per thread or its own
/// lock. Two <em>different</em> documents need two states — they share nothing.
/// </para>
/// <para>
/// A state is also valid across a world that is replaced rather than edited: fingerprints depend on
/// the page, not on the world that produced it.
/// </para>
/// </remarks>
public sealed class TypstIncrementalState
{
    private ulong[] _fingerprints = [];

    /// <summary>
    /// How many pages the previous compilation had. <c>0</c> before the first one.
    /// </summary>
    public int PageCount => _fingerprints.Length;

    /// <summary>
    /// Whether a compilation has been recorded yet. <see langword="false"/> means the next
    /// <see cref="TypstCompiler.CompileChanged"/> reports every page as changed, because there is
    /// nothing to compare against.
    /// </summary>
    public bool HasBaseline { get; private set; }

    /// <summary>
    /// Forgets the baseline, so the next comparison reports every page as changed.
    /// </summary>
    /// <remarks>
    /// The reason to call it is a document that was replaced wholesale — a re-import, a template
    /// switch — where a page-by-page diff would be noise. It does not return memory: the array is
    /// one <see cref="ulong"/> per page.
    /// </remarks>
    public void Reset()
    {
        _fingerprints = [];
        HasBaseline = false;
    }

    /// <summary>The fingerprints of the last recorded compilation, in page order.</summary>
    internal ulong[] Fingerprints => _fingerprints;

    /// <summary>Replaces the baseline with the fingerprints of the compilation just made.</summary>
    internal void Record(ulong[] fingerprints)
    {
        _fingerprints = fingerprints;
        HasBaseline = true;
    }
}
