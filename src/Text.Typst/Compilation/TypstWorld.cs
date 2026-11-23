using System;
using System.Collections.Generic;
using System.Text;
using M3scalinum.Text.Typst.Internal;
using M3scalinum.Text.Typst.Native;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// The set of inputs a compilation sees: sources, assets, fonts and the clock.
/// </summary>
/// <remarks>
/// <para>
/// A world is built by <see cref="TypstWorldBuilder"/> and then either cloned or edited.
/// <see cref="Clone"/> returns a new world sharing the expensive parts — the library, the font book
/// and the fonts — so a host can build one template world and specialise a clone per request; a
/// clone copies the sources, so editing one never touches another.
/// </para>
/// <para>
/// A world is safe to compile from several threads at once. Editing it — <see cref="SetSource"/>
/// — is not, in the sense that a compile running at the same time sees one version or the other;
/// serialise edits per world.
/// </para>
/// </remarks>
public sealed class TypstWorld : IDisposable
{
    private readonly ITypstNative _native;
    private readonly TypstWorldHandle _handle;
    private bool _disposed;

    internal TypstWorld(ITypstNative native, TypstWorldHandle handle)
    {
        _native = native;
        _handle = handle;
    }

    /// <summary>Number of registered sources.</summary>
    public int SourceCount => _handle.SourceCount;

    /// <summary>Number of registered fonts, including the embedded ones.</summary>
    public int FontCount => _handle.FontCount;

    /// <summary>
    /// The font families this world can resolve, sorted.
    /// </summary>
    /// <remarks>
    /// Use it to diagnose an "unknown font family" warning: it lists exactly what the world has,
    /// which is the embedded fonts plus whatever <c>AddFont</c>, <c>AddFontDirectory</c> or
    /// <c>AddSystemFonts</c> contributed.
    /// </remarks>
    public IReadOnlyList<string> FontFamilies
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                var json = Encoding.UTF8.GetString(_native.FontFamilies(_handle));
                return System.Text.Json.JsonSerializer.Deserialize(json, TypstJsonContext.Default.StringArray)
                    ?? [];
            }
            catch (NativeCallException exception)
            {
                throw NativeErrorTranslator.Translate(exception);
            }
        }
    }

    internal TypstWorldHandle Handle => _handle;

    /// <summary>
    /// Returns a new world sharing the library, the font book and the fonts, with its own copy of
    /// the sources and assets.
    /// </summary>
    /// <remarks>
    /// A clone never re-scans fonts and never re-reads packages, so it is cheap enough to take one
    /// per request. The original is unaffected.
    /// </remarks>
    public TypstWorld Clone()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new TypstWorld(_native, _native.CloneWorld(_handle));
    }

    /// <summary>
    /// Returns a clone with one source replaced, or added when the path is new.
    /// </summary>
    /// <param name="path">Virtual path of the source.</param>
    /// <param name="markup">The new content.</param>
    /// <remarks>
    /// This is the "one template world, one document per request" pattern: build the world once
    /// with the fonts and packages, then specialise a clone per document.
    /// </remarks>
    public TypstWorld WithSource(string path, string markup)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(markup);
        TypstPath.Validate(path);

        var clone = Clone();

        try
        {
            _native.AddSource(clone._handle, path, Encoding.UTF8.GetBytes(markup));
            return clone;
        }
        catch
        {
            clone.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Replaces one source inside this world, in place.
    /// </summary>
    /// <param name="path">Virtual path of the source.</param>
    /// <param name="markup">The new content.</param>
    /// <remarks>
    /// <para>
    /// This is the difference between <see cref="WithSource"/> and an editing session: a clone is
    /// a new world, and a new world is a new compilation from Typst's point of view only as far as
    /// its inputs are concerned — what survives an edit is the memoized state keyed by those
    /// inputs. Replacing the source here keeps the world, its font book and its package handles,
    /// which is what a caller iterating on one document wants.
    /// </para>
    /// <para>
    /// The world is no longer immutable, and that is deliberate: a world is the document a caller
    /// edits. What replaces "immutable" is "changing it affects no one else" — a clone copies the
    /// sources, so a template world handed to <see cref="WithSource"/> is never touched. Replacing
    /// a source while another thread compiles the same world is memory-safe but ambiguous: the
    /// compile sees either the old text or the new one. Serialise edits per world.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="markup"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> breaks the path rules.</exception>
    /// <exception cref="TypstNativeException">
    /// The source exceeds <see cref="TypstLimits.MaxSourceBytes"/>, or the world has as many
    /// sources as <see cref="TypstLimits.MaxSources"/> allows.
    /// </exception>
    public void SetSource(string path, string markup)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(markup);
        TypstPath.Validate(path);

        _native.AddSource(_handle, path, Encoding.UTF8.GetBytes(markup));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }
}
