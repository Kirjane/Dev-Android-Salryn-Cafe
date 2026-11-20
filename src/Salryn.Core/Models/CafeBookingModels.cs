namespace Salryn.Core.Models;

public static class CafeBookingModes
{
    public const string Event = "Event";
    public const string CoffeeCart = "CoffeeCart";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Event, StringComparison.Ordinal) ||
        string.Equals(value, CoffeeCart, StringComparison.Ordinal);
}

public static class CafeBookingStatuses
{
    public const string Draft = "Draft";
    public const string Confirmed = "Confirmed";
    public const string ReadyForFulfillment = "ReadyForFulfillment";
    public const string Fulfilled = "Fulfilled";
    public const string Cancelled = "Cancelled";

    public static bool IsTerminal(string? value) =>
        string.Equals(value, Fulfilled, StringComparison.Ordinal) ||
        string.Equals(value, Cancelled, StringComparison.Ordinal);

    public static bool IsKnown(string? value) =>
        string.Equals(value, Draft, StringComparison.Ordinal) ||
        string.Equals(value, Confirmed, StringComparison.Ordinal) ||
        string.Equals(value, ReadyForFulfillment, StringComparison.Ordinal) ||
        IsTerminal(value);
}

public static class CafeBookingFulfillmentStates
{
    public const string NotStarted = "NotStarted";
    public const string OrderBound = "OrderBound";
    public const string SaleBound = "SaleBound";

    public static bool IsKnown(string? value) =>
        string.Equals(value, NotStarted, StringComparison.Ordinal) ||
        string.Equals(value, OrderBound, StringComparison.Ordinal) ||
        string.Equals(value, SaleBound, StringComparison.Ordinal);
}

public static class CafeBookingCashReferenceTypes
{
    public const string DepositReceipt = "BOOKING_DEPOSIT_RECEIPT";
    public const string DepositRefund = "BOOKING_DEPOSIT_REFUND";

    public static bool IsReserved(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();

        return string.Equals(
                   normalized,
                   DepositReceipt,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   DepositRefund,
                   StringComparison.OrdinalIgnoreCase);
    }
}
