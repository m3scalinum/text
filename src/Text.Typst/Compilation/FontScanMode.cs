namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// How a world obtains the host font index it compiles against.
/// </summary>
/// <remarks>
/// The metadata scan of a font directory — enumerating the files and reading each face's family,
/// style and coverage — happens once per process and is shared by every world that asks for the
/// same sources. <see cref="Shared"/> joins that index; <see cref="Refresh"/> scans again and
/// replaces it, which is how a font installed after the process started becomes visible.
/// </remarks>
public enum FontScanMode
{
    /// <summary>
    /// Reuses the process-wide index, scanning only if no world has built one yet. The default.
    /// </summary>
    Shared = 0,

    /// <summary>
    /// Scans the sources again, replaces the process-wide index and uses the result.
    /// </summary>
    /// <remarks>
    /// A font installed after the process started is invisible to <see cref="Shared"/> until
    /// something refreshes the index. One refreshing world is enough: the worlds built after it
    /// share the new index.
    /// </remarks>
    Refresh = 1,
}
