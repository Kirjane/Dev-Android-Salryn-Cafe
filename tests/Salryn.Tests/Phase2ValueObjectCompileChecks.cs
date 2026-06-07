using Salryn.Core.Common;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class Phase2ValueObjectCompileChecks
{
    public static void Run()
    {
        Money price = Money.FromPesos(25.50m);
        Money payment = Money.FromCents(5000);
        Money change = payment - price;

        Quantity qty = Quantity.FromUnits(2.5m).Value;
        Barcode barcode = Barcode.Create(" 4801234567890 ").Value;
        ReceiptNumber receipt = ReceiptNumber.Create("SLRN", 2026, 1).Value;

        Result ok = Result.Success();
        Result fail = Result.Fail("Sample failure.");

        if (change.Cents != 2450)
        {
            throw new InvalidOperationException("Money calculation check failed.");
        }

        if (qty.ValueX1000 != 2500)
        {
            throw new InvalidOperationException("Quantity scaling check failed.");
        }

        if (barcode.Value != "4801234567890")
        {
            throw new InvalidOperationException("Barcode normalization check failed.");
        }

        if (receipt.Value != "SLRN-2026-000001")
        {
            throw new InvalidOperationException("Receipt number formatting check failed.");
        }

        if (!ok.IsSuccess || !fail.IsFailure)
        {
            throw new InvalidOperationException("Result state check failed.");
        }
    }
}
