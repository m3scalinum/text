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
/// A world is immutable once built. <see cref="Clone"/> returns a new world sharing the expensive
/// parts — the library, the font book and the fonts — so a host can build one template world and
/// specialise a clone per request. A world is safe to compile from several threads at once; it is
/// the sources that are copied, not the fonts.
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
