using Salryn.Core.Common;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class Phase4ResultErrorCompileChecks
{
    public static void Run()
    {
        Result ok = Result.Success();

        Result fail = Result.Fail(Error.Validation(
            "Product.NameRequired",
            "Product name is required."));

        Result<Barcode> barcodeResult = Barcode.Create("4801234567890");

        Result<ReceiptNumber> receiptResult = ReceiptNumber.Create("SLRN", 2026, 1);

        if (!ok.IsSuccess)
        {
            throw new InvalidOperationException("Success result check failed.");
        }

        if (!fail.IsFailure)
        {
            throw new InvalidOperationException("Failure result check failed.");
        }

        if (fail.ErrorDetail.Type != ErrorType.Validation)
        {
            throw new InvalidOperationException("Error type check failed.");
        }

        if (fail.Error != "Product name is required.")
        {
            throw new InvalidOperationException("Backward-compatible Error string check failed.");
        }

        if (!barcodeResult.IsSuccess || !receiptResult.IsSuccess)
        {
            throw new InvalidOperationException("Generic result check failed.");
        }
    }
}
