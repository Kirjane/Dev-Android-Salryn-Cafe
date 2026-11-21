using System.Globalization;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using Salryn.App.Android.Platform;
using Salryn.Core.Models;
using Salryn.Mobile.Foundation.Cashier;
using Salryn.Mobile.Runtime;

namespace Salryn.App.Android;

[Activity(Label = "Salryn Cafe", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : Activity
{
    private static readonly Color Espresso = Color.ParseColor("#3B1204"), Cream = Color.ParseColor("#FFF4E1"), DarkCopper = Color.ParseColor("#934719");
    private MobileProductionComposition? _runtime;
    private MobileCashierController? _cashier;
    private MobileCashierIntentStore? _intents;
    private LinearLayout? _root;
    private TextView? _status;
    private readonly List<Button> _buttons = [];
    private readonly List<CafeOrderLineMutation> _lines = [];
    private MenuPublicationSnapshot? _menu;
    private CafeOrderDetail? _order;
    private Guid _draftOrderId = Guid.NewGuid();
    private bool _busy;
    private string _message = "Sign in with your synchronized operator account. Device approval is separate.";
    private const InputTypes MoneyInput = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Page("Salryn Cafe"); Label("Opening Cafe data…");
        try
        {
            AndroidAppPaths paths = new(this);
            _runtime = MobileProductionComposition.Create(paths.RootPath);
            await Task.Run(() => _runtime.InitializeDatabaseAsync());
            if (!_runtime.InspectReadiness().IsReady) throw new InvalidOperationException("Canonical schema is not ready.");
            AndroidKeyStoreSecretStore secrets = new(paths.ProtectedSecretRoot);
            _cashier = new(_runtime, new(new ProtectedMobileEnrollmentReader(secrets), secrets,
                () => new(DateTimeOffset.UtcNow, SystemClock.ElapsedRealtime(), ReadBootCount())));
            _intents = new(secrets); LoginPage();
        }
        catch (Exception error)
        {
            global::Android.Util.Log.Error("SalrynCafe", "Startup blocked: " + error.GetType().Name);
            Page("Cafe setup needs attention");
            Label("Existing data is preserved. Ask the administrator to review device setup and recovery before taking orders.");
        }
    }

    private void LoginPage()
    {
        Page("Salryn Cafe · Operator sign in"); Status();
        EditText user = Input("Username"), password = Input("Password", InputTypes.ClassText | InputTypes.TextVariationPassword);
        Button("Sign in", async () =>
        {
            var result = await _cashier!.LoginAsync(user.Text ?? "", password.Text ?? ""); password.Text = "";
            if (result.IsFailure) { Message(result.ErrorDetail.Message); return; }
            _message = "Signed in. Checking device approval and published menu…"; await CashierPageAsync();
        });
        PrinterButton();
    }

    private async Task CashierPageAsync()
    {
        Page("Salryn Cafe · Cashier"); Label("Operator: " + _cashier!.CurrentOperator?.DisplayName); Status();
        Button("Sign out", () => { _cashier.SignOut(); _order = null; _lines.Clear(); _menu = null; LoginPage(); return Task.CompletedTask; });
        Button("Refresh menu and orders", CashierPageAsync);
        var pending = await _intents!.ReadAsync();
        if (pending is not null)
        {
            Label("An earlier action needs confirmation. Retry its saved request before creating another action.");
            Button("Retry saved action", () => DispatchAsync(pending));
        }
        var menu = await _cashier.MenuAsync();
        if (menu.IsFailure)
        {
            Label(menu.ErrorDetail.Message);
            Label("Cashier actions remain locked until approved pairing, synchronization and a valid offline lease are available.");
            PrinterButton(); return;
        }
        _menu = menu.Value;
        Label("Drawer", true);
        EditText opening = Input("Opening cash (PHP)", MoneyInput);
        Button("Open / recover my shift", async () =>
        { var result = await _cashier.OpenShiftAsync(Cents(opening.Text)); Message(result.IsSuccess ? "Your cashier shift is open." : result.ErrorDetail.Message); });
        EditText counted = Input("Physical counted cash (PHP)", MoneyInput);
        Button("Close my shift with physical count", async () =>
        {
            var session = await _runtime!.CashierSessions.FindOpenSessionByUserIdAsync(_cashier.CurrentOperator!.UserId);
            if (session.IsFailure || session.Value is null) { Message("No open drawer was found for this operator."); return; }
            var result = await _cashier.CloseShiftAsync(session.Value.Id, Cents(counted.Text));
            Message(result.IsSuccess ? "Physical count recorded and shift closed." : result.ErrorDetail.Message);
        });
        Label("Published menu", true);
        foreach (var item in _menu.Items.OrderBy(x => x.DisplayOrder))
            if (item.IsTemporarilyUnavailable) Label(item.Name + " · unavailable");
            else Button(item.Name + " · PHP " + Money(item.SellingPriceCents), () => { ItemDialog(item); return Task.CompletedTask; });
        Label(_order is null ? "New takeout order" : "Current order · " + _order.Order.State, true);
        foreach (var line in _lines) Label($"{line.QuantityX1000 / 1000m:0.###} × {_menu.Items.FirstOrDefault(x => x.ProductId == line.ProductId)?.Name ?? "Saved item"}");
        if (_order is not null) Label("Confirmed total: PHP " + Money(_order.CurrentRevision.Lines.Sum(x => x.LineTotalCents)));
        if (_order is null || _order.Order.State == CafeOrderStates.Open)
            Button("Save order", () => DispatchAsync(Intent("SaveOrder", draft: new(Guid.NewGuid(), _order?.Order.OrderId ?? _draftOrderId,
                _order?.Order.OrderVersion ?? 0, DateTimeOffset.UtcNow, _order?.Order.ServiceMode ?? CafeOrderServiceModes.Takeout,
                _order?.Order.ServiceReference, _lines.ToArray()))));
        if (_order?.Order.State == CafeOrderStates.Open) Button("Send to preparation", () => DispatchAsync(Intent("SubmitPreparation", action: OrderAction())));
        if (_order?.Order.State == CafeOrderStates.Submitted) Button("Mark preparing", () => DispatchAsync(Intent("PreparationStatus", action: OrderAction(), target: CafeOrderStates.Preparing)));
        if (_order?.Order.State == CafeOrderStates.Preparing) Button("Mark ready", () => DispatchAsync(Intent("PreparationStatus", action: OrderAction(), target: CafeOrderStates.Ready)));
        if (_order?.Order.State == CafeOrderStates.Ready) Button("Checkout · Cash / Accounts Receivable", CheckoutDialogAsync);
        Button("Start another takeout order", async () => { _order = null; _lines.Clear(); _draftOrderId = Guid.NewGuid(); await CashierPageAsync(); });
        Label("Saved orders", true);
        var orders = await _cashier.OrdersAsync();
        if (orders.IsFailure) Label(orders.ErrorDetail.Message);
        else foreach (var order in orders.Value.Where(x => !CafeOrderStates.IsTerminal(x.State)).OrderBy(x => x.CreatedAtUtc))
            Button($"{order.ServiceReference ?? order.ServiceMode} · {order.State} · {order.CreatedAtUtc.ToLocalTime():g}", async () =>
            { var found = await _cashier.FindOrderAsync(order.OrderId); if (found.IsFailure) { Message(found.ErrorDetail.Message); return; } Load(found.Value); await CashierPageAsync(); });
        PrinterButton();
    }

    private void ItemDialog(MenuPublicationItemSnapshot item)
    {
        if (_order is not null && _order.Order.State != CafeOrderStates.Open) { Message("Submitted orders are frozen. Start another order to add items."); return; }
        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
        EditText quantity = new(this) { Hint = "Quantity", ContentDescription = "Quantity", Text = "1", InputType = MoneyInput };
        EditText note = new(this) { Hint = "Preparation note", ContentDescription = "Preparation note" }; form.AddView(quantity); form.AddView(note);
        var variants = _menu!.Variants.Where(x => x.ProductId == item.ProductId && !x.IsTemporarilyUnavailable).OrderBy(x => x.DisplayOrder).ToArray();
        Spinner variant = new(this) { ContentDescription = "Menu variant" };
        variant.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            new[] { "Standard" }.Concat(variants.Select(x => x.Name + " · PHP " + Money(x.SellingPriceCents))).ToArray()); form.AddView(variant);
        LinearLayout modifiers = new(this) { Orientation = Orientation.Vertical }; form.AddView(modifiers);
        List<(CheckBox Box, string Id)> selections = [];
        void ShowModifiers()
        {
            modifiers.RemoveAllViews(); selections.Clear();
            string? selected = variant.SelectedItemPosition > 0 ? variants[variant.SelectedItemPosition - 1].VariantId : null;
            var groups = _menu.ItemModifierGroups.Where(x => x.OwnerId == item.ProductId.ToString("D")).Select(x => x.GroupId)
                .Concat(_menu.VariantModifierGroups.Where(x => x.OwnerId == selected).Select(x => x.GroupId)).Distinct();
            foreach (string id in groups)
            {
                var group = _menu.ModifierGroups.First(x => x.GroupId == id);
                modifiers.AddView(new TextView(this) { Text = group.Name + (group.IsRequired ? " · required" : ""), TextSize = 16 });
                foreach (var option in _menu.ModifierOptions.Where(x => x.GroupId == id).OrderBy(x => x.DisplayOrder))
                { CheckBox box = new(this) { Text = option.Name + " · PHP " + Money(option.PriceDeltaCents) }; box.SetMinHeight(Dp(48)); modifiers.AddView(box); selections.Add((box, option.OptionId)); }
            }
        }
        variant.ItemSelected += (sender, args) => ShowModifiers(); ShowModifiers();
        ScrollView scroll = new(this); scroll.AddView(form);
        new AlertDialog.Builder(this)!.SetTitle(item.Name)!.SetView(scroll)!.SetNegativeButton("Cancel", (sender, args) => { })!
            .SetPositiveButton("Add to order", (sender, args) =>
            {
                _ = RunUiAsync(async () =>
                {
                    decimal units = decimal.Parse(quantity.Text ?? "", CultureInfo.InvariantCulture);
                    if (units <= 0 || units * 1000 != decimal.Truncate(units * 1000)) { Message("Use a positive quantity with up to three decimal places."); return; }
                    _lines.Add(new(Guid.NewGuid(), item.ProductId, variant.SelectedItemPosition > 0 ? variants[variant.SelectedItemPosition - 1].VariantId : null,
                        selections.Where(x => x.Box.Checked).Select(x => x.Id).ToArray(), checked((long)(units * 1000)), note.Text, _lines.Count));
                    await CashierPageAsync();
                });
            })!.Show();
    }

    private async Task CheckoutDialogAsync()
    {
        var customers = await _cashier!.CustomersAsync(null);
        IReadOnlyList<MobileCustomerOption> options = customers.IsSuccess ? customers.Value : [];
        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
        EditText cash = new(this) { Hint = "Cash received (PHP)", ContentDescription = "Cash received", InputType = MoneyInput };
        EditText ar = new(this) { Hint = "Accounts Receivable (PHP)", ContentDescription = "Accounts Receivable amount", Text = "0", InputType = MoneyInput };
        Spinner customer = new(this) { ContentDescription = "Credit customer" };
        customer.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            new[] { "Select credit customer" }.Concat(options.Select(x => x.Name)).ToArray()); form.AddView(cash); form.AddView(ar); form.AddView(customer);
        new AlertDialog.Builder(this)!.SetTitle("Confirm tender")!.SetView(form)!.SetNegativeButton("Cancel", (sender, args) => { })!
            .SetPositiveButton("Post sale", (sender, args) =>
            {
                _ = RunUiAsync(async () =>
                {
                    long cashCents = Cents(cash.Text), arCents = Cents(ar.Text);
                    Guid? customerId = customer.SelectedItemPosition > 0 ? options[customer.SelectedItemPosition - 1].CustomerId : null;
                    if (arCents > 0 && customerId is null) { Message("Select an authorized credit customer."); return; }
                    var result = await _cashier.CreateCheckoutIntentAsync(Guid.NewGuid(), _order!.Order.OrderId, _order.Order.OrderVersion,
                        DateTimeOffset.UtcNow, cashCents, arCents, customerId);
                    if (result.IsFailure) { Message(result.ErrorDetail.Message); return; }
                    await DispatchAsync(Intent("Checkout", checkout: result.Value));
                });
            })!.Show();
    }

    private MobilePendingCashierIntent Intent(string kind, MobileOrderDraft? draft = null, MobileOrderAction? action = null,
        string? target = null, MobileCheckoutIntent? checkout = null) => new(kind, draft, action, target, checkout, _cashier!.CurrentOperator!.UserId);
    private async Task DispatchAsync(MobilePendingCashierIntent intent)
    {
        if (_cashier!.CurrentOperator?.UserId != intent.ActorUserId) { Message("Sign in as the original operator to reconcile the saved action."); return; }
        await _intents!.SaveBeforeDispatchAsync(intent);
        Guid orderId;
        switch (intent.Kind)
        {
            case "SaveOrder" when intent.Draft is not null:
                var saved = await _cashier.SaveOrderAsync(intent.Draft);
                if (saved.IsFailure) { Message(saved.ErrorDetail.Message + " Saved request retained."); return; } orderId = saved.Value.OrderId; break;
            case "SubmitPreparation" when intent.Action is not null:
                var submitted = await _cashier.SubmitPreparationAsync(intent.Action);
                if (submitted.IsFailure) { Message(submitted.ErrorDetail.Message + " Saved request retained."); return; } orderId = submitted.Value.OrderId; break;
            case "PreparationStatus" when intent.Action is not null && intent.TargetState is not null:
                var prepared = await _cashier.SetPreparationStatusAsync(intent.Action, intent.TargetState);
                if (prepared.IsFailure) { Message(prepared.ErrorDetail.Message + " Saved request retained."); return; } orderId = prepared.Value.OrderId; break;
            case "Checkout" when intent.Checkout is not null:
                var sale = await _cashier.CompleteAsync(intent.Checkout);
                if (sale.IsFailure) { Message(sale.ErrorDetail.Message + " Saved checkout retained; do not create another sale."); return; }
                orderId = sale.Value.OrderId; _message = "Sale committed · receipt " + sale.Value.ReceiptNumber + ". Printing and central application require separate confirmation."; break;
            default: throw new InvalidOperationException("Unsupported saved action. Preserve it for recovery.");
        }
        await _intents.ClearConfirmedAsync(intent.OperationId);
        var found = await _cashier.FindOrderAsync(orderId); if (found.IsSuccess) Load(found.Value);
        await CashierPageAsync();
    }
    private void Load(CafeOrderDetail order)
    {
        _order = order; _lines.Clear(); _lines.AddRange(order.CurrentRevision.Lines.Select(x => new CafeOrderLineMutation(x.OrderLineId,
            x.ProductId, x.VariantId, x.ModifierSelections.Select(m => m.OptionId).ToArray(), x.QuantityX1000, x.Note, x.DisplayOrder)));
    }
    private MobileOrderAction OrderAction() => new(Guid.NewGuid(), _order!.Order.OrderId, _order.Order.OrderVersion, DateTimeOffset.UtcNow);
    private void Page(string title)
    {
        _buttons.Clear(); _status = null; ScrollView scroll = new(this); scroll.SetBackgroundColor(Cream);
        _root = new(this) { Orientation = Orientation.Vertical }; _root.SetPadding(Dp(20), Dp(24), Dp(20), Dp(24));
        scroll.AddView(_root); SetContentView(scroll); Label(title, true);
    }
    private TextView Label(string text, bool heading = false)
    {
        TextView label = new(this) { Text = text, TextSize = heading ? 21 : 16 }; label.SetTextColor(Espresso); label.SetPadding(0, Dp(8), 0, Dp(8));
        if (heading) label.SetTypeface(null, TypefaceStyle.Bold); _root!.AddView(label); return label;
    }
    private void Status() { _status = Label(_message); _status.AccessibilityLiveRegion = AccessibilityLiveRegion.Polite; }
    private EditText Input(string label, InputTypes type = InputTypes.ClassText)
    {
        Label(label); EditText input = new(this) { Hint = label, InputType = type, ContentDescription = label }; input.SetTextColor(Espresso);
        input.SetMinHeight(Dp(48)); _root!.AddView(input); return input;
    }
    private void Button(string label, Func<Task> action)
    {
        Button button = new(this) { Text = label, ContentDescription = label, Enabled = !_busy }; button.SetTextColor(Cream);
        button.SetBackgroundColor(DarkCopper); button.SetMinHeight(Dp(48));
        LinearLayout.LayoutParams lp = new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent); lp.SetMargins(0, Dp(6), 0, Dp(6));
        button.LayoutParameters = lp; button.Click += async (sender, args) => await RunUiAsync(action); _buttons.Add(button); _root!.AddView(button);
    }
    private async Task RunUiAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true; foreach (var button in _buttons) button.Enabled = false;
        try { await action(); }
        catch (Exception error) when (error is FormatException or OverflowException) { Message("Enter a nonnegative amount with at most two decimal places."); }
        catch (Exception error) { global::Android.Util.Log.Warn("SalrynCafe", "Unconfirmed action: " + error.GetType().Name); Message("Action outcome is unconfirmed. Saved requests are preserved; retry the saved action after checking authorization."); }
        finally { _busy = false; foreach (var button in _buttons) button.Enabled = true; }
    }
    private void Message(string text) { _message = text; if (_status is not null) _status.Text = text; }
    private void PrinterButton() => Button("Bluetooth printer permissions", () => { AndroidBluetoothPrinterTransport.RequestRequiredPermissions(this, 1001); Message("Printer permission is separate from device approval. No print success is claimed."); return Task.CompletedTask; });
    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
    private static string Money(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    private static long Cents(string? text)
    {
        decimal amount = string.IsNullOrWhiteSpace(text) ? 0 : decimal.Parse(text, CultureInfo.InvariantCulture);
        if (amount < 0 || amount * 100 != decimal.Truncate(amount * 100)) throw new FormatException(); return checked((long)(amount * 100));
    }
    private int ReadBootCount()
    {
        try { return global::Android.Provider.Settings.Global.GetInt(ContentResolver, global::Android.Provider.Settings.Global.BootCount, -1); }
        catch (Java.Lang.SecurityException) { return -1; }
    }
}
