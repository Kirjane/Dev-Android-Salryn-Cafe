using Salryn.Core.Common;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.Features.Checkout;

public interface ICheckoutReceiptNumberBoundary
{
    Result<string> CreateReceiptNumber(CheckoutSessionContext? sessionContext);
}

public sealed class SystemCheckoutReceiptNumberBoundary : ICheckoutReceiptNumberBoundary
{
    public Result<string> CreateReceiptNumber(CheckoutSessionContext? sessionContext)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        string sessionPart = sessionContext is null
            ? "NOSHIFT"
            : NormalizeSessionPart(sessionContext.ShortCashierSessionId);

        string randomPart = Guid.NewGuid()
            .ToString("N")
            .Substring(0, 6)
            .ToUpperInvariant();

        string receiptNumber =
            $"POS-{now:yyyyMMdd-HHmmssfff}-{sessionPart}-{randomPart}";

        Result<ReceiptNumber> validation = ReceiptNumber.FromExisting(receiptNumber);

        if (validation.IsFailure)
        {
            return Result<string>.Fail(validation.ErrorDetail);
        }

        return Result<string>.Success(validation.Value.Value);
    }

    private static string NormalizeSessionPart(string value)
    {
        string normalized = new string(
            value
                .Where(char.IsLetterOrDigit)
                .ToArray())
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "SESSION";
        }

        return normalized.Length <= 8
            ? normalized
            : normalized.Substring(0, 8);
    }
}
