namespace Salryn.Api.Local.Contracts;

public sealed record ReceiptPrintResponse(
    bool Succeeded,
    string Status,
    string? SaleId,
    string? ReceiptNumber,
    string Source,
    string AdapterName,
    string Message,
    string? ErrorMessage,
    string? PrintedAtUtc);

public sealed record ReceiptPrintEnvelope(
    string Code,
    string Message,
    ReceiptPrintResponse Print);
