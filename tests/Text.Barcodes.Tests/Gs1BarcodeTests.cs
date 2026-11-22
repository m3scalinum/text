using System;
using M3scalinum.Text.Barcodes;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The symbologies this wave added: GS1-128 in both directions, the four the engine could always
/// write, and the four it can only read.
/// </summary>
public sealed class Gs1BarcodeTests
{
    private const string Gtin = "09501101530003";

    [Fact]
    public void A_gs1_128_round_trips_and_parses_back_into_its_elements()
    {
        var data = Gs1Data.Parse($"01{Gtin}\u001d10LOT-42");

        var image = Barcode.EncodeGs1(data);

        Assert.Equal(BarcodeSymbology.Gs1_128, image.Symbology);

        var decoded = Barcode.Decode(image.Png);

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(BarcodeSymbology.Gs1_128, decoded.Symbology);
        Assert.Equal("CODE_128", decoded.Format);

        var parsed = Gs1Data.Parse(decoded.Text);
        Assert.Equal(2, parsed.Elements.Count);
        Assert.Equal("01", parsed.Elements[0].Ai);
        Assert.Equal(Gtin, parsed.Elements[0].Value);
        Assert.Equal("LOT-42", parsed.Elements[1].Value);
    }

    [Fact]
    public void A_gs1_128_with_a_variable_identifier_before_another_keeps_its_separator()
    {
        // The batch number has no fixed length, so the only way the AI 17 that follows can be found
        // is the field separator the encoder puts there. Losing it would silently read the batch as
        // "LOT-42271231".
        var data = Gs1Data.Parse($"10LOT-42\u001d17271231");

        var decoded = Barcode.Decode(Barcode.EncodeGs1(data).Png);

        Assert.True(decoded.Success, decoded.Error);

        var parsed = Gs1Data.Parse(decoded.Text);
        Assert.Equal(2, parsed.Elements.Count);
        Assert.Equal("LOT-42", parsed.Elements[0].Value);
        Assert.Equal("271231", parsed.Elements[1].Value);
    }

    [Fact]
    public void A_plain_code_128_is_not_reported_as_gs1()
    {
        var decoded = Barcode.Decode(Barcode.Encode(BarcodeSymbology.Code128, "HELLO-128").Png);

        Assert.Equal(BarcodeSymbology.Code128, decoded.Symbology);
        Assert.Equal("HELLO-128", decoded.Text);
    }

    [Fact]
    public void An_unknown_identifier_never_becomes_a_symbol()
    {
        var error = Assert.Throws<BarcodeGs1Exception>(
            () => Barcode.Encode(BarcodeSymbology.Gs1_128, "9999ABC"));

        Assert.Contains("9999", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BarcodeSymbology.UpcA, "123456789012")]
    [InlineData(BarcodeSymbology.UpcE, "01234565")]
    [InlineData(BarcodeSymbology.Code93, "HELLO-93")]
    [InlineData(BarcodeSymbology.Telepen, "HELLO-TELEPEN")]
    public void A_symbology_rxing_always_offered_round_trips(BarcodeSymbology symbology, string payload)
    {
        var image = Barcode.Encode(symbology, payload);
        var decoded = Barcode.Decode(image.Png);

        Assert.True(decoded.Success, decoded.Error);
        Assert.Equal(payload, decoded.Text);
        Assert.Equal(symbology, decoded.Symbology);
        Assert.True(Barcode.CanEncode(symbology));
    }

    [Theory]
    [InlineData(BarcodeSymbology.DataBarOmnidirectional)]
    [InlineData(BarcodeSymbology.DataBarExpanded)]
    [InlineData(BarcodeSymbology.MaxiCode)]
    [InlineData(BarcodeSymbology.MicroQr)]
    public void A_read_only_symbology_says_so_rather_than_failing_inside_the_engine(
        BarcodeSymbology symbology)
    {
        Assert.False(Barcode.CanEncode(symbology));
        // The list is the promise that both directions exist; a read-only symbology is in it
        // because the decoder does handle it.
        Assert.Contains(symbology, Barcode.SupportedSymbologies);

        var error = Assert.Throws<BarcodeUnsupportedException>(
            () => Barcode.Encode(symbology, "0109501101530003"));

        Assert.Equal(BarcodeErrorCode.Unsupported, error.Code);
        Assert.Contains(symbology.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains("read only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_symbology_outside_the_set_still_fails_as_unsupported()
    {
        var error = Assert.Throws<BarcodeUnsupportedException>(
            () => Barcode.Encode((BarcodeSymbology)99, "HELLO"));

        Assert.Equal(BarcodeErrorCode.Unsupported, error.Code);
    }

    [Fact]
    public void A_gs1_128_refuses_an_ean13_length_payload_the_way_the_other_one_dimensional_codes_do()
    {
        // A GS1-128 is a Code 128 with structure: a payload that is not an element string is a GS1
        // error, not a Code 128 error, because the structure is what was asked for.
        var error = Assert.Throws<BarcodeGs1Exception>(
            () => Barcode.Encode(BarcodeSymbology.Gs1_128, "HELLO"));

        Assert.Equal(BarcodeErrorCode.Gs1, error.Code);
    }
}
