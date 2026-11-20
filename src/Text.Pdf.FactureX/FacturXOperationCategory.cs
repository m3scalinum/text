namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>What the invoice sells, as the French mandate requires it to be stated.</summary>
public enum FacturXOperationCategory
{
    /// <summary>Livraison de biens.</summary>
    Goods,

    /// <summary>Prestation de services.</summary>
    Services,

    /// <summary>Les deux, en opérations distinctes.</summary>
    Mixed,
}