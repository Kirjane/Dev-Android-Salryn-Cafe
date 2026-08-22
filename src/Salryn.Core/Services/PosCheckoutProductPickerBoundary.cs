using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;

namespace Salryn.Core.Services;

public sealed record PosCheckoutProductPick(
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long UnitPriceCents,
    long? CostPriceCents,
    bool TrackStock,
    ProductTaxClassification TaxClassification,
    bool IsActive);

public sealed class PosCheckoutProductPickerBoundary
{
    public Result<PosCheckoutDraftLineInput> CreateLineInput(
        ProductLookupReadModel product,
        long quantityX1000 = 1000,
        long discountCents = 0)
    {
        if (product is null)
        {
            return Result<PosCheckoutDraftLineInput>.Fail(Error.Validation(
                "CheckoutProductPicker.ProductRequired",
                "Checkout product is required."));
        }

        return CreateLineInput(
            new PosCheckoutProductPick(
                product.Id,
                product.Name,
                product.Barcode,
                product.UnitName,
                product.SellingPrice.Cents,
                product.CostPrice?.Cents,
                product.TrackStock,
                product.TaxClassification,
                product.IsActive),
            quantityX1000,
            discountCents);
    }

    public Result<PosCheckoutDraftLineInput> CreateLineInput(
        ProductListItemReadModel product,
        long quantityX1000 = 1000,
        long discountCents = 0)
    {
        if (product is null)
        {
            return Result<PosCheckoutDraftLineInput>.Fail(Error.Validation(
                "CheckoutProductPicker.ProductRequired",
                "Checkout product is required."));
        }

        return CreateLineInput(
            new PosCheckoutProductPick(
                product.Id,
                product.Name,
                product.Barcode,
                product.UnitName,
                product.SellingPrice.Cents,
                product.CostPrice?.Cents,
                product.TrackStock,
                product.TaxClassification,
                product.IsActive),
            quantityX1000,
            discountCents);
    }

    public Result<PosCheckoutDraftLineInput> CreateLineInput(
        PosCheckoutProductPick product,
        long quantityX1000 = 1000,
        long discountCents = 0)
    {
        Result validation = ValidateProductPick(
            product,
            quantityX1000,
            discountCents);

        if (validation.IsFailure)
        {
            return Result<PosCheckoutDraftLineInput>.Fail(validation.ErrorDetail);
        }

        try
        {
            long grossCents = CalculateLineGrossCents(
                product.UnitPriceCents,
                quantityX1000);

            if (discountCents > grossCents)
            {
                return Result<PosCheckoutDraftLineInput>.Fail(Error.Validation(
                    "CheckoutProductPicker.LineDiscountExceedsGross",
                    "Checkout product picker line discount cannot exceed line gross amount."));
            }

            PosCheckoutDraftLineInput lineInput = new(
                product.ProductId,
                product.ProductNameSnapshot.Trim(),
                string.IsNullOrWhiteSpace(product.BarcodeSnapshot)
                    ? null
                    : product.BarcodeSnapshot.Trim(),
                product.UnitNameSnapshot.Trim(),
                quantityX1000,
                product.UnitPriceCents,
                product.CostPriceCents,
                product.TrackStock,
                product.TaxClassification,
                discountCents);

            return Result<PosCheckoutDraftLineInput>.Success(lineInput);
        }
        catch (OverflowException)
        {
            return Result<PosCheckoutDraftLineInput>.Fail(Error.Validation(
                "CheckoutProductPicker.AmountOverflow",
                "Checkout product picker amount calculation is too large."));
        }
    }

    private static Result ValidateProductPick(
        PosCheckoutProductPick product,
        long quantityX1000,
        long discountCents)
    {
        if (product is null)
        {
            return Result.Fail(Error.Validation(
                "CheckoutProductPicker.ProductRequired",
                "Checkout product is required."));
        }

        if (product.ProductId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CheckoutProductPicker.InvalidProductId",
                "Checkout product ID is required."));
        }

        if (!product.IsActive)
        {
            return Result.Fail(Error.Conflict(
                "CheckoutProductPicker.InactiveProduct",
                "Inactive products cannot be selected for checkout."));
        }

        if (string.IsNullOrWhiteSpace(product.ProductNameSnapshot))
        {
            return Result.Fail(Error.Validation(
                "CheckoutProductPicker.ProductNameRequired",
                "Checkout product name is required."));
        }

        if (string.IsNullOrWhiteSpace(product.UnitNameSnapshot))
        {
            return Result.Fail(Error.Validation(
                "CheckoutProductPicker.UnitNameRequired",
                "Checkout product unit name is required."));
        }

        if (quantityX1000 <= 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutProductPicker.InvalidQuantity",
                "Checkout product quantity must be greater than zero."));
        }

        if (product.UnitPriceCents < 0 ||
            product.CostPriceCents < 0 ||
            discountCents < 0)
        {
            return Result.Fail(Error.Validation(
                "CheckoutProductPicker.InvalidMoneyAmount",
                "Checkout product money amounts cannot be negative."));
        }

        return Result.Success();
    }

    private static long CalculateLineGrossCents(
        long unitPriceCents,
        long quantityX1000)
    {
        decimal gross = unitPriceCents * (quantityX1000 / 1000m);

        return checked((long)Math.Round(
            gross,
            0,
            MidpointRounding.AwayFromZero));
    }
}
