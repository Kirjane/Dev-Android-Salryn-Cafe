using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

internal static class PosCheckoutProductPickerBoundarySmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== POS CHECKOUT PRODUCT PICKER BOUNDARY SMOKE CHECKS =====");

        PosCheckoutProductPickerBoundary picker = new();

        ProductLookupReadModel trackedLookup = new(
            Guid.NewGuid(),
            "SKU-TRACKED",
            "  4801234567890  ",
            "  Tracked Product  ",
            "  pc  ",
            Money.FromCents(1500),
            Money.FromCents(900),
            QuantityX1000(10000),
            TrackStock: true,
            AllowDiscount: true,
            IsActive: true);

        PosCheckoutDraftLineInput defaultTrackedLine = RequireSuccess(
            picker.CreateLineInput(trackedLookup),
            "ProductPicker.DefaultTrackedLookup");

        VerifyLine(
            defaultTrackedLine,
            trackedLookup.Id,
            "Tracked Product",
            "4801234567890",
            "pc",
            quantityX1000: 1000,
            unitPriceCents: 1500,
            costPriceCents: 900,
            discountCents: 0,
            trackStock: true,
            "ProductPicker.DefaultTrackedLookup.Verify");

        PosCheckoutDraftLineInput customTrackedLine = RequireSuccess(
            picker.CreateLineInput(
                trackedLookup,
                quantityX1000: 2500,
                discountCents: 125),
            "ProductPicker.CustomTrackedLookup");

        VerifyLine(
            customTrackedLine,
            trackedLookup.Id,
            "Tracked Product",
            "4801234567890",
            "pc",
            quantityX1000: 2500,
            unitPriceCents: 1500,
            costPriceCents: 900,
            discountCents: 125,
            trackStock: true,
            "ProductPicker.CustomTrackedLookup.Verify");

        ProductLookupReadModel untrackedLookup = new(
            Guid.NewGuid(),
            null,
            null,
            "  Service Product  ",
            "  service  ",
            Money.FromCents(500),
            null,
            QuantityX1000(0),
            TrackStock: false,
            AllowDiscount: true,
            IsActive: true);

        PosCheckoutDraftLineInput untrackedLine = RequireSuccess(
            picker.CreateLineInput(untrackedLookup),
            "ProductPicker.UntrackedLookup");

        VerifyLine(
            untrackedLine,
            untrackedLookup.Id,
            "Service Product",
            null,
            "service",
            quantityX1000: 1000,
            unitPriceCents: 500,
            costPriceCents: null,
            discountCents: 0,
            trackStock: false,
            "ProductPicker.UntrackedLookup.Verify");

        ProductListItemReadModel listItem = new(
            Guid.NewGuid(),
            "SKU-LIST",
            "  99001122  ",
            "  List Product  ",
            "  box  ",
            "Category",
            "Brand",
            Money.FromCents(2500),
            Money.FromCents(1400),
            QuantityX1000(5000),
            QuantityX1000(1000),
            TrackStock: true,
            IsActive: true);

        PosCheckoutDraftLineInput listItemLine = RequireSuccess(
            picker.CreateLineInput(
                listItem,
                quantityX1000: 3000,
                discountCents: 250),
            "ProductPicker.ListItem");

        VerifyLine(
            listItemLine,
            listItem.Id,
            "List Product",
            "99001122",
            "box",
            quantityX1000: 3000,
            unitPriceCents: 2500,
            costPriceCents: 1400,
            discountCents: 250,
            trackStock: true,
            "ProductPicker.ListItem.Verify");

        ExpectFailure(
            picker.CreateLineInput(trackedLookup with { IsActive = false }),
            "ProductPicker.InactiveProduct",
            ErrorType.Conflict,
            "CheckoutProductPicker.InactiveProduct");

        ExpectFailure(
            picker.CreateLineInput(trackedLookup with { Id = Guid.Empty }),
            "ProductPicker.InvalidProductId",
            ErrorType.Validation,
            "CheckoutProductPicker.InvalidProductId");

        ExpectFailure(
            picker.CreateLineInput(trackedLookup with { Name = "   " }),
            "ProductPicker.ProductNameRequired",
            ErrorType.Validation,
            "CheckoutProductPicker.ProductNameRequired");

        ExpectFailure(
            picker.CreateLineInput(trackedLookup with { UnitName = "   " }),
            "ProductPicker.UnitNameRequired",
            ErrorType.Validation,
            "CheckoutProductPicker.UnitNameRequired");

        ExpectFailure(
            picker.CreateLineInput(trackedLookup, quantityX1000: 0),
            "ProductPicker.InvalidQuantity",
            ErrorType.Validation,
            "CheckoutProductPicker.InvalidQuantity");

        ExpectFailure(
            picker.CreateLineInput(trackedLookup with { SellingPrice = Money.FromCents(-1) }),
            "ProductPicker.NegativeSellingPrice",
            ErrorType.Validation,
            "CheckoutProductPicker.InvalidMoneyAmount");

        ExpectFailure(
            picker.CreateLineInput(trackedLookup with { CostPrice = Money.FromCents(-1) }),
            "ProductPicker.NegativeCostPrice",
            ErrorType.Validation,
            "CheckoutProductPicker.InvalidMoneyAmount");

        ExpectFailure(
            picker.CreateLineInput(trackedLookup, discountCents: -1),
            "ProductPicker.NegativeDiscount",
            ErrorType.Validation,
            "CheckoutProductPicker.InvalidMoneyAmount");

        ExpectFailure(
            picker.CreateLineInput(
                trackedLookup,
                quantityX1000: 1000,
                discountCents: 999999),
            "ProductPicker.LineDiscountExceedsGross",
            ErrorType.Validation,
            "CheckoutProductPicker.LineDiscountExceedsGross");

        Console.WriteLine("POS checkout product picker boundary smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void VerifyLine(
        PosCheckoutDraftLineInput line,
        Guid expectedProductId,
        string expectedName,
        string? expectedBarcode,
        string expectedUnitName,
        long quantityX1000,
        long unitPriceCents,
        long? costPriceCents,
        long discountCents,
        bool trackStock,
        string area)
    {
        if (line.ProductId != expectedProductId ||
            !string.Equals(line.ProductNameSnapshot, expectedName, StringComparison.Ordinal) ||
            !string.Equals(line.BarcodeSnapshot, expectedBarcode, StringComparison.Ordinal) ||
            !string.Equals(line.UnitNameSnapshot, expectedUnitName, StringComparison.Ordinal) ||
            line.QuantityX1000 != quantityX1000 ||
            line.UnitPriceCents != unitPriceCents ||
            line.CostPriceCents != costPriceCents ||
            line.DiscountCents != discountCents ||
            line.TrackStock != trackStock)
        {
            throw new InvalidOperationException(
                $"{area} product picker line mapping verification failed.");
        }
    }

    private static void ExpectFailure<T>(
        Result<T> result,
        string area,
        ErrorType expectedType,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"{area} expected failure but succeeded.");
        }

        if (result.ErrorDetail.Type != expectedType ||
            !string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{area} expected {expectedType}/{expectedCode}, got {result.ErrorDetail.Type}/{result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        Result<Quantity> result = Quantity.FromX1000(valueX1000);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Quantity helper failed: {result.Error}");
        }

        return result.Value;
    }
}
