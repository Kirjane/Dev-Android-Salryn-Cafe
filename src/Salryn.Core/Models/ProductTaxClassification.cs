using Salryn.Core.Common;

namespace Salryn.Core.Models;

public enum ProductTaxClassification
{
    Vatable = 1,
    VatExempt = 2,
    ZeroRated = 3,
    NonVat = 4
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
            _ => throw new ArgumentOutOfRangeException(nameof(classification), classification, "Product tax classification is unsupported.")
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
            ProductTaxClassification.NonVat => "Non-VAT",
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
            (ProductTaxClassification.NonVat, ToDisplayText(ProductTaxClassification.NonVat))
        };
    }
}
