namespace Salryn.Core.Models;

// Engineering tax line classification for deterministic bucket calculation only.
// Not a legal VAT policy selector.
public enum OfficialDocumentLineTaxClassification
{
    Vatable = 1,
    VatExempt = 2,
    ZeroRated = 3,
    NonVat = 4
}
