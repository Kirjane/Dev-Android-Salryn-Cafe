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

        AndroidAppPaths paths = new(this);
        _db = new CafeMobileDatabase(paths.DatabasePath);
        _db.Initialize();

        AndroidKeyStoreSecretStore secretStore = new(paths.ProtectedSecretRoot);
        MobileOfflineLeaseGuard leaseGuard = new(secretStore);
        AndroidBluetoothPrinterTransport btTransport = new(this);
        _engine = new MobileCashierEngine(
            _db,
            leaseGuard,
            btTransport,
            clockSource: () => (SystemClock.ElapsedRealtime(), ReadBootCount()));

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
            Text = "SALRYN CAFE — CASHIER PREVIEW",
            TextSize = 20f
        };
        title.SetTextColor(Cream);
        title.SetTypeface(null, TypefaceStyle.Bold);

        TextView subtitle = new(this)
        {
            Text = "Setup is incomplete. Pairing and cashier actions are not available in this preview.",
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
            Text = "Cached Menu",
            TextSize = 16f
        };
        menuHeader.SetTextColor(DarkCopper);
        menuHeader.SetTypeface(null, TypefaceStyle.Bold);
        root.AddView(menuHeader);

        if (_engine.GetMenuItems().Count == 0)
        {
            TextView emptyMenu = new(this)
            {
                Text = "No menu downloaded. Connect through approved device setup before taking orders.",
                TextSize = 14f
            };
            emptyMenu.SetTextColor(Mocha);
            root.AddView(emptyMenu);
        }

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
            $"Cached menu: {menuCount} items\n" +
            $"Pending Sync Outbox: {outboxCount} envelopes\n" +
            $"Device setup: incomplete; cashier authorization unavailable\n" +
            $"Bluetooth Printer Transport: {(btGranted ? $"PERMITTED ({pairedPrinters} paired)" : "PERMISSION NEEDED")}";
    }

    private int ReadBootCount()
    {
        try
        {
            return global::Android.Provider.Settings.Global.GetInt(ContentResolver,
                global::Android.Provider.Settings.Global.BootCount, -1);
        }
        catch (Java.Lang.SecurityException)
        {
            // Unknown clock provenance must lock offline mutations, never invent a boot ID.
            return -1;
        }
    }
}
