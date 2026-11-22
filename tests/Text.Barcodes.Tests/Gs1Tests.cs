using System;
using M3scalinum.Text.Barcodes;
using Xunit;

namespace M3scalinum.Text.Barcodes.Tests;

/// <summary>
/// The GS1 element string: an AI string parsed into named fields, and the two serialisations the
/// standard defines on top of those fields — the GS1-128 payload and the Digital Link URL.
/// </summary>
public sealed class Gs1Tests
{
    private const string Gtin = "09501101530003";

    [Fact]
    public void A_two_element_string_parses_into_named_fields()
    {
        var data = Gs1Data.Parse($"01{Gtin}10LOT-42");

        Assert.Equal(2, data.Elements.Count);
        Assert.Equal("01", data.Elements[0].Ai);
        Assert.Equal(Gtin, data.Elements[0].Value);
        Assert.Equal("GTIN", data.Elements[0].Title);
        Assert.Equal("10", data.Elements[1].Ai);
        Assert.Equal("LOT-42", data.Elements[1].Value);
        Assert.Equal("BATCH/LOT", data.Elements[1].Title);
    }

    [Fact]
    public void Three_elements_with_a_variable_one_need_a_separator()
    {
        var data = Gs1Data.Parse($"01{Gtin}\u001d10LOT-42\u001d17271231");

        Assert.Equal(3, data.Elements.Count);
        Assert.Equal("17", data.Elements[2].Ai);
        Assert.Equal("271231", data.Elements[2].Value);
        Assert.Equal($"(01){Gtin}(10)LOT-42(17)271231", data.HumanReadable);
    }

    [Fact]
    public void The_printed_form_parses_too()
    {
        var data = Gs1Data.Parse($"(01){Gtin}(10)BATCH 7");

        Assert.Equal("BATCH 7", data.Elements[1].Value);
        Assert.Equal($"01{Gtin}10BATCH 7", data.Raw);
    }

    [Fact]
    public void An_unknown_identifier_is_a_typed_error_and_names_itself()
    {
        var error = Assert.Throws<BarcodeGs1Exception>(() => Gs1Data.Parse("9999ABC"));

        Assert.Equal(BarcodeErrorCode.Gs1, error.Code);
        Assert.Contains("9999", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gtin_with_a_bad_check_digit_is_refused()
    {
        var error = Assert.Throws<BarcodeGs1Exception>(() => Gs1Data.Parse("0109501101530004"));

        Assert.Equal(BarcodeErrorCode.Gs1, error.Code);
        Assert.Contains("check digit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_of_the_wrong_length_for_its_identifier_is_refused()
    {
        var error = Assert.Throws<BarcodeGs1Exception>(() => Gs1Data.Parse("01123"));

        Assert.Contains("AI 01", error.Message, StringComparison.Ordinal);
        Assert.Contains("exactly 14", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_digital_link_puts_the_primary_key_in_the_path_and_the_rest_in_the_query()
    {
        var data = Gs1Data.Parse($"01{Gtin}\u001d10LOT 42");

        var url = data.ToDigitalLink("https://example.com/gs1");

        Assert.Equal($"https://example.com/gs1/01/{Gtin}?10=LOT%2042", url);
    }

    [Fact]
    public void A_digital_link_round_trips_through_its_own_url()
    {
        var data = Gs1Data.Parse($"01{Gtin}\u001d10LOT-42\u001d17271231");
        var url = data.ToDigitalLink("https://example.com");

        var parsed = Gs1Data.FromDigitalLink(url);

        Assert.Equal(data.Elements.Count, parsed.Elements.Count);
        for (var index = 0; index < data.Elements.Count; index++)
        {
            Assert.Equal(data.Elements[index].Ai, parsed.Elements[index].Ai);
            Assert.Equal(data.Elements[index].Value, parsed.Elements[index].Value);
        }

        Assert.Equal(data.Raw, parsed.Raw);
    }

    [Fact]
    public void A_digital_link_without_a_primary_key_is_refused()
    {
        var data = Gs1Data.Parse("10LOT-42");

        var error = Assert.Throws<BarcodeGs1Exception>(() => data.ToDigitalLink("https://example.com"));

        Assert.Contains("primary key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_url_is_not_a_digital_link()
    {
        Assert.Throws<BarcodeGs1Exception>(() => Gs1Data.FromDigitalLink($"/01/{Gtin}"));
    }

    [Fact]
    public void A_gtin_in_a_digital_link_is_check_digit_verified()
    {
        Assert.Throws<BarcodeGs1Exception>(
            () => Gs1Data.FromDigitalLink("https://example.com/01/09501101530004"));
    }

    [Fact]
    public void The_identifier_table_is_read_from_the_library_and_holds_the_documented_ones()
    {
        var identifiers = Barcode.ApplicationIdentifiers;

        Assert.NotEmpty(identifiers);

        foreach (var ai in new[] { "00", "01", "10", "17", "21", "30", "37", "400", "410", "8017" })
        {
            Assert.Contains(identifiers, identifier => identifier.Ai == ai);
        }

        var gtin = Assert.Single(identifiers, identifier => identifier.Ai == "01");
        Assert.Equal("GTIN", gtin.Title);
        Assert.Equal(14, gtin.MinimumLength);
        Assert.Equal(14, gtin.MaximumLength);
        Assert.True(gtin.Numeric);
        Assert.True(gtin.CheckDigit);
        Assert.True(gtin.PrimaryKey);
        Assert.True(gtin.IsFixedLength);

        var batch = Assert.Single(identifiers, identifier => identifier.Ai == "10");
        Assert.False(batch.IsFixedLength);
        Assert.Equal(20, batch.MaximumLength);
        Assert.False(batch.CheckDigit);
    }

    [Fact]
    public void A_parsed_string_renders_the_way_a_label_does()
    {
        var data = Gs1Data.Parse($"01{Gtin}");

        Assert.Equal($"(01){Gtin}", data.ToString());
        Assert.Equal($"(01){Gtin}", data.HumanReadable);
        Assert.Equal($"(01){Gtin}", data.Elements[0].ToString());
    }

    [Fact]
    public void A_null_payload_is_an_argument_error_before_the_native_call()
    {
        Assert.Throws<ArgumentNullException>(() => Gs1Data.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => Gs1Data.FromDigitalLink(null!));
        Assert.Throws<ArgumentNullException>(() => Barcode.EncodeGs1(null!));
    }
}
