namespace Salryn.Mobile.Foundation.Printing;

public sealed record PrinterTransportResult(bool Accepted, string Code, string Message, bool OutcomeUnknown = false)
{
    public static PrinterTransportResult NotReady(string code, string message) =>
        new(false, code, message);

    // A started write can reach the printer before an RFCOMM error is observed.
    // Unknown outcome MUST NOT be treated as a clean failure for automatic retry.
    public static PrinterTransportResult Unknown(string code, string message) =>
        new(false, code, message, OutcomeUnknown: true);

    public static PrinterTransportResult AcceptedForTransport(string message) =>
        new(true, "PrinterTransport.Accepted", message);
}

public interface IPrinterTransport
{
    Task<PrinterTransportResult> SendAsync(
        string targetId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}
