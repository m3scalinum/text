using System;

namespace M3scalinum.Text.Barcodes;

/// <summary>
/// What a caller wants printed: a resolution, a physical size, and the quiet zone to leave around
/// the symbol.
/// </summary>
/// <remarks>
/// <para>
/// This is the difference between a PNG and a printable symbol. A raster at four pixels per module
/// says nothing about the paper — the same file is a two centimetre sticker or an eight centimetre
/// label — so a print target names the resolution and either the width of the finished symbol or
/// the size of one module, and <see cref="Barcode.Print"/> answers with the size it really prints at.
/// </para>
/// <para>
/// Exactly one of <see cref="WidthMillimetres"/> and <see cref="ModuleMillimetres"/> sizes the
/// symbol; with neither, the module takes the symbology's minimum, the smallest size the
/// specification allows. One default cannot fit every symbology — a Code 128 carries a hundred
/// modules where a QR Code carries twenty-one — so the default is a floor rather than a width. The
/// floors are not preferences: a target that would print a module below the symbology's minimum,
/// or a quiet zone narrower than its norm, is refused with the specification named in the message
/// rather than rendered into something a scanner cannot read.
/// </para>
/// </remarks>
public sealed class BarcodePrintOptions
{
    /// <summary>
    /// Output resolution, in dots per inch; 300 by default, the resolution of an office laser
    /// printer. Accepted range: 72 to 2400.
    /// </summary>
    public int Dpi { get; init; } = 300;

    /// <summary>
    /// Width of the whole symbol, quiet zone included, in millimetres, or <see langword="null"/>
    /// to size by <see cref="ModuleMillimetres"/> or by the symbology's minimum module.
    /// </summary>
    /// <remarks>
    /// The raster is whole pixels, so the module is rounded down: the printed symbol never exceeds
    /// the width asked for. <see cref="BarcodePrint.WidthMillimetres"/> reports the width that was
    /// actually produced.
    /// </remarks>
    public double? WidthMillimetres { get; init; }

    /// <summary>
    /// The X-dimension: the printed size of one module, in millimetres, or <see langword="null"/>.
    /// Exclusive with <see cref="WidthMillimetres"/>.
    /// </summary>
    public double? ModuleMillimetres { get; init; }

    /// <summary>
    /// Width the caller's layout reserves, in modules, or <see langword="null"/> to leave it
    /// unchecked.
    /// </summary>
    /// <remarks>
    /// When set, the symbol and its quiet zone must fit. A layout too narrow is refused — the
    /// quiet zone the symbology requires is never cropped to make a symbol fit.
    /// </remarks>
    public int? WidthModules { get; init; }

    /// <summary>
    /// Quiet zone in modules on each side, or <see langword="null"/> for the symbology's normative
    /// minimum: four for a QR Code, one for a DataMatrix, ten for a Code 128, eleven for an EAN-13.
    /// </summary>
    /// <remarks>
    /// A value below the minimum is refused; <see cref="Barcode.MinimumQuietZone"/> reports the
    /// floor for a symbology. Wider is accepted up to a safety ceiling of 64 modules.
    /// </remarks>
    public int? QuietZone { get; init; }

    /// <summary>
    /// Height of a one-dimensional symbol's bars, in modules, or <see langword="null"/> for forty.
    /// Ignored by the two-dimensional symbologies.
    /// </summary>
    public int? BarHeight { get; init; }

    /// <summary>
    /// Error correction for a QR Code, as in <see cref="BarcodeOptions.ErrorCorrection"/>.
    /// </summary>
    public BarcodeErrorCorrection ErrorCorrection { get; init; } = BarcodeErrorCorrection.Default;

    /// <summary>
    /// The module colour, or <see langword="null"/> for black, under the same contrast rule as
    /// <see cref="BarcodeOptions.Foreground"/>.
    /// </summary>
    public BarcodeColor? Foreground { get; init; }

    /// <summary>
    /// The background colour, quiet zone included, or <see langword="null"/> for white.
    /// </summary>
    public BarcodeColor? Background { get; init; }

    /// <summary>
    /// A square centre reserved for a logo, or <see langword="null"/> for none. QR Code only, and
    /// read back with this package's own decoder before the raster is returned — see
    /// <see cref="BarcodeOptions.Logo"/>.
    /// </summary>
    public BarcodeLogo? Logo { get; init; }

    /// <summary>Whether any style option is set, which picks the styled ABI call.</summary>
    internal bool HasStyle => Foreground is not null || Background is not null || Logo is not null;

    /// <summary>Validates the numeric ranges the managed side owns, before the native call.</summary>
    /// <exception cref="BarcodeException">A value is outside its accepted range, or both sizing
    /// options are set.</exception>
    internal void Validate()
    {
        if (Dpi is < 72 or > 2400)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"Dpi must be between 72 and 2400 dots per inch, got {Dpi}");
        }

        if (WidthMillimetres is not null && ModuleMillimetres is not null)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                "a print target takes WidthMillimetres or ModuleMillimetres, not both");
        }

        if (WidthMillimetres is { } width && (!double.IsFinite(width) || width <= 0))
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"WidthMillimetres must be a positive number of millimetres, got {width}");
        }

        if (ModuleMillimetres is { } module && (!double.IsFinite(module) || module <= 0))
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"ModuleMillimetres must be a positive number of millimetres, got {module}");
        }

        if (WidthModules is < 1 or > 4096)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"WidthModules must be between 1 and 4096 modules, got {WidthModules}");
        }

        if (QuietZone is < 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"QuietZone must not be negative, got {QuietZone}");
        }

        if (BarHeight is < 1 or > 1024)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"BarHeight must be between 1 and 1024 modules, got {BarHeight}");
        }

        Logo?.Validate();
    }

    /// <summary>The width to pass to the native call: zero when the module size sizes the symbol.</summary>
    internal double EffectiveWidthMillimetres => WidthMillimetres ?? 0;

    /// <summary>
    /// The module size to pass to the native call: zero when the width sizes the symbol, and the
    /// symbology's minimum when the caller named no target at all.
    /// </summary>
    internal double EffectiveModuleMillimetres(double minimumModuleMillimetres)
        => ModuleMillimetres ?? (WidthMillimetres is null ? minimumModuleMillimetres : 0);
}
