using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Java.Util;
using Salryn.Mobile.Foundation.Printing;

namespace Salryn.App.Android.Platform;

/// <summary>
/// Real Android Bluetooth Classic RFCOMM/SPP ESC/POS peripheral printer transport.
/// Strictly isolated from device-trust pairing, operator RBAC, and sync credentials.
/// </summary>
public sealed class AndroidBluetoothPrinterTransport : IBluetoothPrinterTransport
{
    private static readonly UUID SppServiceUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;
    private readonly Context _context;

    public AndroidBluetoothPrinterTransport(Context context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public static bool HasRequiredPermissions(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return true;
        }

        return context.CheckSelfPermission(Manifest.Permission.BluetoothConnect) == Permission.Granted
            && context.CheckSelfPermission(Manifest.Permission.BluetoothScan) == Permission.Granted;
    }

    public static void RequestRequiredPermissions(Activity activity, int requestCode)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || HasRequiredPermissions(activity))
        {
            return;
        }

        activity.RequestPermissions(
            [Manifest.Permission.BluetoothConnect, Manifest.Permission.BluetoothScan],
            requestCode);
    }

    public IReadOnlyList<PairedBluetoothPrinterInfo> ListPairedPrinters()
    {
        if (!HasRequiredPermissions(_context))
        {
            return Array.Empty<PairedBluetoothPrinterInfo>();
        }

        BluetoothAdapter? adapter = GetAdapter();
        if (adapter is null || !adapter.IsEnabled || adapter.BondedDevices is null)
        {
            return Array.Empty<PairedBluetoothPrinterInfo>();
        }

        List<PairedBluetoothPrinterInfo> printers = [];
        foreach (BluetoothDevice device in adapter.BondedDevices)
        {
            if (!string.IsNullOrWhiteSpace(device.Address))
            {
                printers.Add(new PairedBluetoothPrinterInfo(device.Address, device.Name ?? device.Address));
            }
        }
        return printers;
    }

    public async Task<PrinterTransportResult> SendAsync(
        string targetId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasRequiredPermissions(_context))
        {
            return PrinterTransportResult.NotReady(
                "AndroidBluetooth.PermissionRequired",
                "Bluetooth scan/connect permission is required before printing.");
        }

        if (string.IsNullOrWhiteSpace(targetId))
        {
            return PrinterTransportResult.NotReady(
                "AndroidBluetooth.TargetRequired",
                "A paired Bluetooth printer MAC address is required.");
        }

        if (payload.IsEmpty)
        {
            return PrinterTransportResult.NotReady(
                "AndroidBluetooth.PayloadRequired",
                "ESC/POS printer payload cannot be empty.");
        }

        BluetoothAdapter? adapter = GetAdapter();
        if (adapter is null || !adapter.IsEnabled)
        {
            return PrinterTransportResult.NotReady(
                "AndroidBluetooth.AdapterUnavailable",
                "Bluetooth adapter is unavailable or disabled on this device.");
        }

        return await Task.Run(() =>
        {
            bool writeStarted = false;
            try
            {
                BluetoothDevice? device = adapter.GetRemoteDevice(targetId.Trim());
                if (device is null)
                {
                    return PrinterTransportResult.NotReady("AndroidBluetooth.DeviceNotFound", $"Bluetooth device '{targetId}' not found.");
                }

                using BluetoothSocket? socket = device.CreateRfcommSocketToServiceRecord(SppServiceUuid);
                if (socket is null)
                {
                    return PrinterTransportResult.NotReady("AndroidBluetooth.SocketUnavailable", "Could not allocate RFCOMM SPP socket.");
                }

                socket.Connect();
                using Stream? outStream = socket.OutputStream;
                if (outStream is null)
                {
                    return PrinterTransportResult.NotReady("AndroidBluetooth.StreamUnavailable", "RFCOMM output stream unavailable.");
                }

                byte[] bytes = payload.ToArray();
                writeStarted = true;
                outStream.Write(bytes, 0, bytes.Length);
                outStream.Flush();
                return PrinterTransportResult.AcceptedForTransport($"Delivered {bytes.Length} ESC/POS bytes via Bluetooth SPP to {targetId}.");
            }
            catch (Exception ex)
            {
                if (writeStarted)
                {
                    return PrinterTransportResult.Unknown(
                        "AndroidBluetooth.OutcomeUnknown",
                        "Bluetooth write started but delivery cannot be confirmed; manual inspection is required before reprinting.");
                }

                return PrinterTransportResult.NotReady("AndroidBluetooth.TransportError", ex.GetType().Name);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private BluetoothAdapter? GetAdapter()
    {
        BluetoothManager? manager = _context.GetSystemService(Context.BluetoothService) as BluetoothManager;
        return manager?.Adapter;
    }
}
