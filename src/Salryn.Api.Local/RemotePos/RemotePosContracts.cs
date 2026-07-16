using Salryn.Api.Local.Bridge;

namespace Salryn.Api.Local.RemotePos;

// LAN-M16: header names for the host-owned remote POS command runtime. Terminal identity
// and handshake proof reuse the existing read-only bridge headers; the operator session
// token is carried in a dedicated opaque header and is never logged raw.
public static class RemotePosHeaders
{
    public const string TerminalId = RemoteReadOnlyBridgeHeaders.TerminalId;

    public const string HandshakeSessionId = RemoteReadOnlyBridgeHeaders.HandshakeSessionId;

    public const string RemoteOrigin = RemoteReadOnlyBridgeHeaders.RemoteOrigin;

    public const string OperatorSession = "X-Salryn-Remote-Pos-Session";
}

// LAN-M16: stable command names + categories for the remote POS command surface. Unknown
// commands are rejected. Categories separate reads from cart writes, checkout writes,
// shift writes, and manager/admin actions so the authorization matrix can fail closed.
public static class RemotePosCommands
{
    public const string SessionRead = "remote-pos.session.read";
    public const string Capabilities = "remote-pos.capabilities";
    public const string Login = "remote-pos.session.login";
    public const string Logout = "remote-pos.session.logout";

    public const string CartStart = "remote-pos.cart.start";
    public const string CartView = "remote-pos.cart.view";
    public const string CartAddLine = "remote-pos.cart.add-line";
    public const string CartAddLineByBarcode = "remote-pos.cart.add-line-by-barcode";
    public const string CartUpdateLine = "remote-pos.cart.update-line";
    public const string CartRemoveLine = "remote-pos.cart.remove-line";
    public const string CartClear = "remote-pos.cart.clear";

    public const string ShiftOpen = "remote-pos.shift.open";
    public const string ShiftClose = "remote-pos.shift.close";
    public const string ShiftRead = "remote-pos.shift.read";

    public const string CheckoutSubmit = "remote-pos.checkout.submit";
    public const string CheckoutStatus = "remote-pos.checkout.status";

    public const string ManagerOverride = "remote-pos.manager.override";
    public const string AdminStatus = "remote-pos.admin.status";
}

public enum RemotePosCommandCategory
{
    // Read categories require only a valid operator session.
    SessionRead = 1,
    CapabilitiesRead = 2,
    CartRead = 3,
    ShiftRead = 4,
    CheckoutStatusRead = 5,
    AdminStatusRead = 6,

    // Write categories require permission + idempotency where a durable write occurs.
    CartWrite = 20,
    ShiftWrite = 21,
    CheckoutWrite = 22,
    ManagerAction = 40,
    AdminAction = 41,
}

// ---- Request / response DTOs (positional records, mirror the read-only contract style). ----

public sealed record RemotePosLoginRequest(string Username, string Password);

public sealed record RemotePosSessionResponse(
    string SessionState,
    string? SessionId,
    string? SessionToken,
    string? TerminalId,
    string? HostId,
    string? StoreId,
    string? UserId,
    string? Username,
    string? DisplayName,
    string? RoleName,
    IReadOnlyList<string> Permissions,
    string? IssuedAtUtc,
    string? ExpiresAtUtc,
    bool RemotePosRuntimeEnabled);

public sealed record RemotePosCapabilityResponse(
    string Command,
    string Category,
    bool Allowed,
    string Decision,
    string Description);

public sealed record RemotePosCapabilitiesResponse(
    string RoleName,
    IReadOnlyList<RemotePosCapabilityResponse> Capabilities,
    IReadOnlyList<string> HostLocalOnlyActions,
    IReadOnlyList<string> Warnings);

public sealed record RemotePosCartLineResponse(
    string ProductId,
    string ProductName,
    string UnitName,
    long QuantityX1000,
    long UnitPriceCents,
    long LineTotalCents);

public sealed record RemotePosCartResponse(
    string CartId,
    string SessionId,
    IReadOnlyList<RemotePosCartLineResponse> Lines,
    long SubtotalCents,
    string Message);

public sealed record RemotePosCartAddLineRequest(Guid ProductId, long QuantityX1000);

/// <summary>PRE-BETA-F: keyboard-wedge barcode cart add (host validates barcode → product).</summary>
public sealed record RemotePosCartAddLineByBarcodeRequest(string Barcode, long QuantityX1000);

public sealed record RemotePosCartUpdateLineRequest(Guid ProductId, long QuantityX1000);

public sealed record RemotePosCartRemoveLineRequest(Guid ProductId);

public sealed record RemotePosShiftOpenRequest(long OpeningCashCents);

public sealed record RemotePosShiftResponse(
    string ShiftState,
    string? ShiftId,
    string? OpenedAtUtc,
    string Message);

public sealed record RemotePosCheckoutPaymentRequest(string Method, long AmountCents);

public sealed record RemotePosCheckoutSubmitRequest(
    string ClientRequestId,
    IReadOnlyList<RemotePosCheckoutPaymentRequest> Payments);

public sealed record RemotePosCheckoutResultResponse(
    string Status,
    string ClientRequestId,
    string? ReceiptNumber,
    string? SaleId,
    long? TotalCents,
    long? ChangeCents,
    bool Duplicate,
    string Message);

public sealed record RemotePosManagerOverrideRequest(string Kind, string? Reason);
