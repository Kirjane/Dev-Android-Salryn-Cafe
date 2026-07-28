namespace Salryn.Api.Local.Contracts;

public sealed record HostOperatorCheckoutLineRequest(
    Guid ProductId,
    long QuantityX1000);

public sealed record HostOperatorCheckoutPaymentRequest(
    string Method,
    long AmountCents);

public sealed record HostOperatorCheckoutRequest(
    IReadOnlyList<HostOperatorCheckoutLineRequest> Lines,
    IReadOnlyList<HostOperatorCheckoutPaymentRequest> Payments,
    Guid? CustomerId = null,
    string? CustomerFullName = null,
    string? CustomerPhone = null,
    DateOnly? DueDate = null,
    string? Note = null);

public sealed record HostOperatorCheckoutResponse(
    string Status,
    string? ReceiptNumber,
    string? SaleId,
    long? TotalCents,
    long? ChangeCents,
    string Message,
    bool ReceiptSnapshotSaved = false,
    string FileReceiptStatus = "NotAttempted",
    string? FileReceiptPath = null,
    string? FileReceiptError = null);
