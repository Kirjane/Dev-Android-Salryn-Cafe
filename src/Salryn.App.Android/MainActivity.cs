using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Salryn.App.Android.Platform;
using Salryn.Mobile.Foundation.Security;
using Salryn.Mobile.Runtime;

namespace Salryn.App.Android;

[Activity(
    Label = "Salryn Cafe",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : Activity
{
    // Canonical Salryn Cafe Brand Palette (assets/brand/PALETTE.md)
    private static readonly Color Espresso = Color.ParseColor("#3B1204");
    private static readonly Color Cream = Color.ParseColor("#FFF4E1");
    private static readonly Color Copper = Color.ParseColor("#CC7436");
    private static readonly Color DarkCopper = Color.ParseColor("#934719");
    private static readonly Color Latte = Color.ParseColor("#E9D5BB");
    private static readonly Color Mocha = Color.ParseColor("#735447");

    private CafeMobileDatabase? _db;
    private MobileCashierEngine? _engine;
    private TextView? _statusView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        AndroidAppPaths paths = AndroidAppPaths.FromContext(this);
        paths.EnsureDirectories();

        _db = new CafeMobileDatabase(paths.ProductionDatabasePath);
        _db.Initialize();

        AndroidKeyStoreSecretStore secretStore = new(paths.ProtectedSecretRoot);
        MobileOfflineLeaseGuard leaseGuard = new(secretStore);
        AndroidBluetoothPrinterTransport btTransport = new(this);
        _engine = new MobileCashierEngine(
            _db,
            leaseGuard,
            btTransport,
            clockSource: () => (SystemClock.ElapsedRealtime(), 1));

        // Seed default menu catalog if empty for first-run offline readiness
        if (_engine.GetMenuItems().Count == 0)
        {
            _engine.SeedMenuItems(
            [
                new MobileMenuItemDto(Guid.CreateVersion7(), "Iced Spanish Latte", "Espresso", 14500, true),
                new MobileMenuItemDto(Guid.CreateVersion7(), "Americano", "Espresso", 11000, true),
                new MobileMenuItemDto(Guid.CreateVersion7(), "Caramel Macchiato", "Signature", 16000, true),
                new MobileMenuItemDto(Guid.CreateVersion7(), "Matcha Latte", "Non-Coffee", 15500, true)
            ]);
        }

        ScrollView scroll = new(this);
        scroll.SetBackgroundColor(Cream);

        LinearLayout root = new(this)
        {
            Orientation = Orientation.Vertical
        };
        root.SetPadding(32, 36, 32, 36);

        // Header banner
        LinearLayout header = new(this)
        {
            Orientation = Orientation.Vertical
        };
        header.SetBackgroundColor(Espresso);
        header.SetPadding(28, 28, 28, 28);

        TextView title = new(this)
        {
            Text = "SALRYN CAFE — OFFLINE CASHIER",
            TextSize = 20f
        };
        title.SetTextColor(Cream);
        title.SetTypeface(null, TypefaceStyle.Bold);

        TextView subtitle = new(this)
        {
            Text = "Product: Salryn.Cafe | Tenders: Cash / AR / Partial Cash+AR | BT ESC/POS Ready",
            TextSize = 12f
        };
        subtitle.SetTextColor(Latte);

        header.AddView(title);
        header.AddView(subtitle);
        root.AddView(header);

        _statusView = new TextView(this)
        {
            TextSize = 14f
        };
        _statusView.SetTextColor(Espresso);
        _statusView.SetBackgroundColor(Latte);
        _statusView.SetPadding(24, 20, 24, 20);
        LinearLayout.LayoutParams statusLp = new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        statusLp.SetMargins(0, 20, 0, 20);
        _statusView.LayoutParameters = statusLp;
        root.AddView(_statusView);

        // Menu items preview
        TextView menuHeader = new(this)
        {
            Text = "Offline Menu Catalog (SQLite WAL)",
            TextSize = 16f
        };
        menuHeader.SetTextColor(DarkCopper);
        menuHeader.SetTypeface(null, TypefaceStyle.Bold);
        root.AddView(menuHeader);

        foreach (MobileMenuItemDto item in _engine.GetMenuItems())
        {
            TextView row = new(this)
            {
                Text = $"• {item.Name} ({item.Category}) — PHP {(item.PriceCents / 100m):0.00}",
                TextSize = 14f
            };
            row.SetTextColor(Mocha);
            row.SetPadding(8, 10, 8, 10);
            root.AddView(row);
        }

        // Bluetooth Printer Permissions button (isolated peripheral flow)
        Button btButton = new(this)
        {
            Text = "Check / Request Bluetooth Printer Permissions"
        };
        btButton.SetBackgroundColor(Copper);
        btButton.SetTextColor(Cream);
        btButton.SetMinimumHeight(110);
        btButton.Click += (_, _) =>
        {
            AndroidBluetoothPrinterTransport.RequestRequiredPermissions(this, 1001);
            RefreshStatus(btTransport);
        };
        LinearLayout.LayoutParams btnLp = new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        btnLp.SetMargins(0, 24, 0, 0);
        btButton.LayoutParameters = btnLp;
        root.AddView(btButton);

        RefreshStatus(btTransport);

        scroll.AddView(root);
        SetContentView(scroll);
    }

    private void RefreshStatus(AndroidBluetoothPrinterTransport btTransport)
    {
        if (_statusView is null || _engine is null) return;
        int menuCount = _engine.GetMenuItems().Count;
        int outboxCount = _engine.GetPendingOutboxCount();
        bool btGranted = AndroidBluetoothPrinterTransport.HasRequiredPermissions(this);
        int pairedPrinters = btGranted ? btTransport.ListPairedPrinters().Count : 0;

        _statusView.Text =
            $"Local SQLite WAL: READY ({menuCount} menu items)\n" +
            $"Pending Sync Outbox: {outboxCount} envelopes\n" +
            $"Device Trust: AndroidKeyStore AES-256-GCM + ECDSA P-256\n" +
            $"Bluetooth Printer Transport: {(btGranted ? $"PERMITTED ({pairedPrinters} paired)" : "PERMISSION NEEDED")}";
    }
}
