using Salryn.Core.Common;

namespace Salryn.Core.Models;

public enum ProductTaxClassification
{
    Vatable = 1,
    VatExempt = 2,
    ZeroRated = 3,
    /// <summary>Non-VAT / SSPT (sales subject to percentage tax).</summary>
    NonVat = 4,
    /// <summary>Non-VAT exempt sales.</summary>
    NonVatExempt = 5
}

public static class ProductTaxClassificationMapper
{
    public static OfficialDocumentLineTaxClassification ToOfficialLineClassification(
        ProductTaxClassification classification)
    {
        return classification switch
        {
            ProductTaxClassification.Vatable => OfficialDocumentLineTaxClassification.Vatable,
            ProductTaxClassification.VatExempt => OfficialDocumentLineTaxClassification.VatExempt,
            ProductTaxClassification.ZeroRated => OfficialDocumentLineTaxClassification.ZeroRated,
            ProductTaxClassification.NonVat => OfficialDocumentLineTaxClassification.NonVat,
            ProductTaxClassification.NonVatExempt => OfficialDocumentLineTaxClassification.NonVatExempt,
            _ => throw new ArgumentOutOfRangeException(
                nameof(classification),
                classification,
                "Product tax classification is unsupported.")
        };
    }

    public static Result<ProductTaxClassification> Parse(int storedValue)
    {
        if (!Enum.IsDefined(typeof(ProductTaxClassification), storedValue))
        {
            return Result<ProductTaxClassification>.Fail(Error.Validation(
                "Products.InvalidTaxClassification",
                "Product tax classification is invalid."));
        }

        return Result<ProductTaxClassification>.Success((ProductTaxClassification)storedValue);
    }

    public static string ToDisplayText(ProductTaxClassification classification)
    {
        return classification switch
        {
            ProductTaxClassification.Vatable => "VATable",
            ProductTaxClassification.VatExempt => "VAT-exempt",
            ProductTaxClassification.ZeroRated => "Zero-rated",
            ProductTaxClassification.NonVat => "SSPT (non-VAT taxable)",
            ProductTaxClassification.NonVatExempt => "Non-VAT exempt",
            _ => classification.ToString()
        };
    }

    public static IReadOnlyList<(ProductTaxClassification Value, string DisplayText)> GetDisplayOptions()
    {
        return new[]
        {
            (ProductTaxClassification.Vatable, ToDisplayText(ProductTaxClassification.Vatable)),
            (ProductTaxClassification.VatExempt, ToDisplayText(ProductTaxClassification.VatExempt)),
            (ProductTaxClassification.ZeroRated, ToDisplayText(ProductTaxClassification.ZeroRated)),
            (ProductTaxClassification.NonVat, ToDisplayText(ProductTaxClassification.NonVat)),
            (ProductTaxClassification.NonVatExempt, ToDisplayText(ProductTaxClassification.NonVatExempt))
        };
    }
}
