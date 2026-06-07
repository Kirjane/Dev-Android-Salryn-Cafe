using Salryn.Core.Models;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class Phase3ModelCompileChecks
{
    public static void Run()
    {
        Unit unit = new()
        {
            Name = "Piece",
            Abbreviation = "pc",
            AllowsDecimalQuantity = false
        };

        Product product = new()
        {
            Name = "Sample Product",
            Barcode = Barcode.Create("4801234567890").Value,
            UnitId = unit.Id,
            SellingPrice = Money.FromPesos(25.50m),
            StockOnHand = Quantity.FromUnits(10m).Value
        };

        ProductSnapshot snapshot = new()
        {
            ProductId = product.Id,
            BarcodeValue = product.Barcode?.Value,
            ProductName = product.Name,
            UnitName = unit.Name,
            SellingPrice = product.SellingPrice,
            TrackStock = product.TrackStock,
            AllowDiscount = product.AllowDiscount
        };

        CartLine line = new()
        {
            Product = snapshot,
            Quantity = Quantity.FromUnits(2m).Value,
            UnitPrice = Money.FromPesos(25.50m),
            LineTotal = Money.FromPesos(51.00m)
        };

        Cart cart = new()
        {
            CashierUserId = Guid.NewGuid(),
            Lines = new List<CartLine> { line }
        };

        Sale sale = new()
        {
            ReceiptNumber = "SLRN-2026-000001",
            CashierUserId = cart.CashierUserId,
            Subtotal = Money.FromPesos(51.00m),
            Total = Money.FromPesos(51.00m),
            AmountPaid = Money.FromPesos(100.00m),
            Change = Money.FromPesos(49.00m),
            Items = new List<SaleItem>(),
            Payments = new List<Payment>()
        };

        StockMovement movement = new()
        {
            ProductId = product.Id,
            MovementType = StockMovementType.StockIn,
            QuantityChange = Quantity.FromUnits(10m).Value,
            UserId = cart.CashierUserId
        };

        PrintJob printJob = new()
        {
            JobType = "RECEIPT",
            ReferenceType = "SALE",
            ReferenceId = sale.Id,
            PayloadText = "Sample receipt"
        };

        ErrorLog errorLog = new()
        {
            Area = "CompileCheck",
            Message = "Sample friendly error"
        };

        if (product.Name.Length == 0 || sale.ReceiptNumber.Length == 0)
        {
            throw new InvalidOperationException("Phase 3 model compile check failed.");
        }

        _ = movement;
        _ = printJob;
        _ = errorLog;
    }
}
