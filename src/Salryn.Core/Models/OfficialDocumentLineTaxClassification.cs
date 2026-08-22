namespace Salryn.Core.Models;

// Engineering tax line classification for deterministic bucket calculation.
// Not a legal VAT policy selector by itself — constrained by taxpayer mode.
public enum OfficialDocumentLineTaxClassification
{
    Vatable = 1,
    VatExempt = 2,
    ZeroRated = 3,
    /// <summary>Non-VAT sales subject to percentage tax (SSPT).</summary>
    NonVat = 4,
    /// <summary>Non-VAT exempt sales.</summary>
    NonVatExempt = 5
}
