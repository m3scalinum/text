using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Barcodes;

/// <summary>What a <see cref="ShippingLabel"/> carries besides its SSCC.</summary>
/// <remarks>
/// <para>
/// A logistics label is a fixed template: who the parcel is for, where it goes, and which parcel it
/// is. Nothing here is free-form layout — the fields are the ones the template has a slot for, and a
/// value that does not fit is refused rather than clipped.
/// </para>
/// <para>
/// The recipient and the address are the human half of the label; the reference is the carrier's or
/// the shipper's own parcel number, printed for the person holding the box. The machine half is the
/// SSCC in the GS1-128 symbol, which is not an option: it is the key the label is created from.
/// </para>
/// </remarks>
public sealed class ShippingLabelOptions
{
    /// <summary>The page. Defaults to <see cref="ShippingLabelFormat.Millimetres100x150"/>.</summary>
    public ShippingLabelFormat Format { get; init; } = ShippingLabelFormat.Millimetres100x150;

    /// <summary>
    /// The resolution the symbol is rasterised at. Defaults to 300 dots per inch, the resolution a
    /// thermal label printer is driven at, which is also what keeps a module on whole dots.
    /// </summary>
    public int Dpi { get; init; } = 300;

    /// <summary>
    /// The X-dimension of the GS1-128 symbol, in millimetres. Defaults to 0.495 mm, the nominal
    /// module of the GS1 General Specifications for a logistics label; the print target refuses
    /// anything under the 0.25 mm floor of ISO 15394:2009 §6.1.3.
    /// </summary>
    public double ModuleMillimetres { get; init; } = 0.495;

    /// <summary>
    /// The bar height in modules. Defaults to 64 modules, which at 0.495 mm is 31.7 mm: the 32 mm
    /// bar height a distribution scanner expects.
    /// </summary>
    public int BarHeightModules { get; init; } = 64;

    /// <summary>The recipient's name, printed in bold above the address. Optional.</summary>
    public string? Recipient { get; init; }

    /// <summary>
    /// The delivery address, one line per entry, printed in order. At least one line is required:
    /// a label with an SSCC and no address is a parcel nobody can deliver.
    /// </summary>
    public IReadOnlyList<string> AddressLines { get; init; } = [];

    /// <summary>The carrier's or the shipper's parcel reference, printed above the symbol.</summary>
    public string? Reference { get; init; }

    /// <summary>Validates the ranges this package owns, before anything is encoded.</summary>
    /// <exception cref="BarcodeException">A value is empty or outside its accepted range.</exception>
    internal void Validate()
    {
        _ = Format.WidthMillimetres();

        if (Dpi is < 72 or > 2400)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"Dpi must be between 72 and 2400 dots per inch, got {Dpi}");
        }

        if (!double.IsFinite(ModuleMillimetres) || ModuleMillimetres <= 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"ModuleMillimetres must be a positive number of millimetres, got {ModuleMillimetres}");
        }

        if (BarHeightModules is < 1 or > 1024)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"BarHeightModules must be between 1 and 1024 modules, got {BarHeightModules}");
        }

        if (AddressLines.Count == 0)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                "a shipping label needs at least one address line: an SSCC is not an address");
        }

        if (AddressLines.Count > ShippingLabel.MaximumAddressLines)
        {
            throw new BarcodeException(
                BarcodeErrorCode.InvalidArgument,
                $"the address block holds at most {ShippingLabel.MaximumAddressLines} lines, got {AddressLines.Count}");
        }

        foreach (var line in AddressLines)
        {
            if (line is null)
            {
                throw new BarcodeException(
                    BarcodeErrorCode.InvalidArgument, "an address line must not be null");
            }

            ShippingLabel.CheckLine(line, "address line", ShippingLabel.MaximumAddressCharacters);
        }

        if (Recipient is not null)
        {
            ShippingLabel.CheckLine(Recipient, "recipient", ShippingLabel.MaximumAddressCharacters);
        }

        if (Reference is not null)
        {
            ShippingLabel.CheckLine(Reference, "reference", ShippingLabel.MaximumReferenceCharacters);
        }
    }
}
