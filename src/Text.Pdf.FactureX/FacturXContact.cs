namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>
/// A contact at the seller (<c>BG-6</c>) or at the buyer (<c>BG-9</c>).
/// </summary>
/// <remarks>
/// The standard makes the point of contact, the phone number and the e-mail address three separate
/// optional terms; a document may carry any of them. <see cref="FacturXParty.Email"/> remains the
/// shorthand the reader filled before contacts existed, and is kept in step with
/// <see cref="Email"/> when a contact is present.
/// </remarks>
public sealed class FacturXContact
{
    /// <summary>The name of the contact, <c>BT-41</c> / <c>BT-56</c>.</summary>
    public string? Name { get; init; }

    /// <summary>The telephone number, <c>BT-42</c> / <c>BT-57</c>.</summary>
    public string? Phone { get; init; }

    /// <summary>The e-mail address, <c>BT-43</c> / <c>BT-58</c>.</summary>
    public string? Email { get; init; }
}
