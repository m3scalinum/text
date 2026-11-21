namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>A party on the invoice, seller or buyer.</summary>
public sealed class FacturXParty
{
    /// <summary>The trading name.</summary>
    public string? Name { get; init; }

    /// <summary>The legal registration identifier (SIREN in France), <c>BT-30</c> / <c>BT-47</c>.</summary>
    public string? LegalId { get; init; }

    /// <summary>
    /// The scheme the legal registration identifier belongs to, <c>BT-30-1</c> / <c>BT-47-1</c>.
    /// </summary>
    /// <remarks>
    /// <c>0002</c> is SIREN and <c>0009</c> is SIRET in the ISO 6523 code list the French CIUS
    /// uses; left empty when the identifier is not one of those.
    /// </remarks>
    public string? LegalIdScheme { get; init; }

    /// <summary>
    /// The identifier of the party as the counterpart knows it, <c>BT-29</c> — the SIRET of a
    /// French seller.
    /// </summary>
    public string? PartyId { get; init; }

    /// <summary>The scheme of <see cref="PartyId"/>, <c>BT-29-1</c>: <c>0009</c> for a SIRET.</summary>
    public string? PartyIdScheme { get; init; }

    /// <summary>
    /// The electronic routing address of the party, <c>BT-34</c> / <c>BT-49</c> — the identifier a
    /// French platform delivers to.
    /// </summary>
    public string? ElectronicAddress { get; init; }

    /// <summary>The scheme of <see cref="ElectronicAddress"/>: <c>0009</c>, <c>0002</c> or <c>0225</c>.</summary>
    public string? ElectronicAddressScheme { get; init; }

    /// <summary>The VAT identifier, without the country prefix guessing.</summary>
    public string? VatId { get; init; }

    /// <summary>The point of contact at this party, <c>BG-6</c> / <c>BG-9</c>.</summary>
    public FacturXContact? Contact { get; init; }

    /// <summary>Address line 1.</summary>
    public string? Line1 { get; init; }

    /// <summary>Address line 2.</summary>
    public string? Line2 { get; init; }

    /// <summary>Postal code.</summary>
    public string? Postcode { get; init; }

    /// <summary>City.</summary>
    public string? City { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? Country { get; init; }

    /// <summary>
    /// The contact e-mail, when the document carries one.
    /// </summary>
    /// <remarks>
    /// Kept for the documents read before <see cref="Contact"/> existed: the reader sets it from the
    /// contact, and the writers fall back to it when no contact is given.
    /// </remarks>
    public string? Email { get; init; }
}