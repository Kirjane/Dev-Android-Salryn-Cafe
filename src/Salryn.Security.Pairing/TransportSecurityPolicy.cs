using System.Collections.Concurrent;
using System.Net;

namespace Salryn.Security.Pairing;

public enum TransportContext
{
    DesktopLoopbackWebView = 1,
    LanRemoteTerminal = 2,
    CentralSync = 3
}

public sealed record PeripheralPrinterBinding(
    Guid BindingId,
    PrinterStationRole StationRole,
    string DeviceAddress,
    string DisplayName,
    bool IsPaired)
{
    // Structural invariant: peripheral printer bindings NEVER confer device trust or operator RBAC.
    public bool CanGrantDeviceTrust => false;
    public bool CanGrantOperatorAuthority => false;
}

/// <summary>
/// Enforces ADR-I4 transport boundaries:
/// - Loopback HTTP allowed ONLY on 127.0.0.1/localhost:5399 for desktop WebView2.
/// - LAN remote terminals and Central sync MUST use HTTPS (no plaintext LAN HTTP).
/// </summary>
public static class TransportSecurityPolicy
{
    public static PairingResult ValidateEndpoint(Uri? endpoint, TransportContext context)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri)
        {
            return PairingResult.Fail(
                PairingErrorCodes.InvalidEndpointOrigin,
                "A valid absolute URI is required.",
                httpStatusCode: 400);
        }

        bool isLoopback = string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(endpoint.Host, out IPAddress? ip) && IPAddress.IsLoopback(ip));

        return context switch
        {
            TransportContext.DesktopLoopbackWebView =>
                string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && isLoopback
                && endpoint.Port == PairingConstants.DefaultLoopbackPort
                    ? PairingResult.Success()
                    : PairingResult.Fail(
                        PairingErrorCodes.InvalidEndpointOrigin,
                        $"Desktop WebView2 loopback endpoint must be http://127.0.0.1:{PairingConstants.DefaultLoopbackPort} or http://localhost:{PairingConstants.DefaultLoopbackPort}.",
                        httpStatusCode: 403),

            TransportContext.LanRemoteTerminal =>
                !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    ? PairingResult.Fail(
                        PairingErrorCodes.PlaintextTransportRejected,
                        "Plaintext HTTP is prohibited for LAN remote terminals; HTTPS is required.",
                        httpStatusCode: 403)
                    : isLoopback
                        ? PairingResult.Fail(
                            PairingErrorCodes.InvalidEndpointOrigin,
                            "LAN remote terminal endpoint must bind an explicit LAN address, not loopback.",
                            httpStatusCode: 403)
                        : PairingResult.Success(),

            TransportContext.CentralSync =>
                string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    ? PairingResult.Success()
                    : PairingResult.Fail(
                        PairingErrorCodes.PlaintextTransportRejected,
                        "Plaintext HTTP is prohibited for Central Sync; HTTPS (TLS 1.2+/1.3) is required.",
                        httpStatusCode: 403),

            _ => PairingResult.Fail(PairingErrorCodes.InvalidEndpointOrigin, "Unsupported transport context.", 400)
        };
    }
}

/// <summary>
/// Strictly isolated Bluetooth receipt/kitchen printer binding registry.
/// Pairing or unpairing a peripheral printer has zero effect on terminal enrollment or operator auth.
/// </summary>
public sealed class PeripheralPrinterRegistry
{
    private readonly ConcurrentDictionary<PrinterStationRole, PeripheralPrinterBinding> _bindings = new();

    public PeripheralPrinterBinding PairPrinter(PrinterStationRole stationRole, string deviceAddress, string displayName)
    {
        if (string.IsNullOrWhiteSpace(deviceAddress))
        {
            throw new ArgumentException("Bluetooth printer address is required.", nameof(deviceAddress));
        }

        PeripheralPrinterBinding binding = new(
            BindingId: Guid.NewGuid(),
            StationRole: stationRole,
            DeviceAddress: deviceAddress.Trim(),
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? $"{stationRole} Printer" : displayName.Trim(),
            IsPaired: true);

        _bindings[stationRole] = binding;
        return binding;
    }

    public bool UnpairPrinter(PrinterStationRole stationRole) => _bindings.TryRemove(stationRole, out _);

    public PeripheralPrinterBinding? GetBinding(PrinterStationRole stationRole) =>
        _bindings.TryGetValue(stationRole, out PeripheralPrinterBinding? binding) ? binding : null;
}
