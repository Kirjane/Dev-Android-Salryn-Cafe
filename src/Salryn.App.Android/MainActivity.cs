using System.Globalization;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using Salryn.App.Android.Platform;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Mobile.Foundation.Cashier;
using Salryn.Mobile.Foundation.Printing;
using Salryn.Mobile.Runtime;

namespace Salryn.App.Android;

[Activity(Label = "Salryn Cafe", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : Activity
{
    private static readonly Color Espresso = Color.ParseColor("#3B1204");
    private static readonly Color Cream = Color.ParseColor("#FFF4E1");
    private static readonly Color CardSurface = Color.ParseColor("#FFFBF3");
    private static readonly Color Latte = Color.ParseColor("#E9D5BB");
    private static readonly Color Copper = Color.ParseColor("#CC7436");
    private static readonly Color DarkCopper = Color.ParseColor("#934719");
    private static readonly Color Mocha = Color.ParseColor("#735447");
    private static readonly Color AlertBg = Color.ParseColor("#FDE8E8");
    private static readonly Color AlertBorder = Color.ParseColor("#B91C1C");
    private static readonly Color AlertText = Color.ParseColor("#8A1C1C");
    private static readonly Color OkBg = Color.ParseColor("#E6F4EA");
    private static readonly Color OkBorder = Color.ParseColor("#15803D");
    private static readonly Color OkText = Color.ParseColor("#14532D");

    private MobileProductionComposition? _runtime;
    private MobileCashierController? _cashier;
    private MobileCashierIntentStore? _intents;
    private AndroidCentralConnection? _central;
    private LinearLayout? _root;
    private LinearLayout? _currentCard;
    private TextView? _status;
    private readonly List<Button> _buttons = [];
    private readonly List<CafeOrderLineMutation> _lines = [];
    private MenuPublicationSnapshot? _menu;
    private CafeOrderDetail? _order;
    private Guid _draftOrderId = Guid.NewGuid();
    private string _serviceMode = CafeOrderServiceModes.Takeout;
    private string _serviceReference = string.Empty;
    private Guid? _lastSaleId;
    private string? _lastReceiptNumber;
    private Guid? _lastPrintJobId;
    private string? _lastPrintStatus;
    private bool _busy;
    private string _message = "Sign in with your synchronized operator account. Device approval is separate.";
    private const InputTypes MoneyInput = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Page("Salryn Cafe", "Starting local POS runtime…");
        Card("Startup");
        Label("Opening Cafe data…");
        try
        {
            AndroidAppPaths paths = new(this);
            _runtime = MobileProductionComposition.Create(paths.RootPath);
            await Task.Run(() => _runtime.InitializeDatabaseAsync());
            if (!_runtime.InspectReadiness().IsReady) throw new InvalidOperationException("Canonical schema is not ready.");
            AndroidKeyStoreSecretStore secrets = new(paths.ProtectedSecretRoot);
            _cashier = new(_runtime, new(new ProtectedMobileEnrollmentReader(secrets), secrets,
                () => new(DateTimeOffset.UtcNow, SystemClock.ElapsedRealtime(), ReadBootCount())));
            _intents = new(secrets);
            _central = new(secrets, () => new(DateTimeOffset.UtcNow, SystemClock.ElapsedRealtime(), ReadBootCount()));
            await LoginPageAsync();
        }
        catch (Exception error)
        {
            global::Android.Util.Log.Error("SalrynCafe", "Startup blocked: " + error.GetType().Name);
            Page("Salryn Cafe", "Setup needs attention");
            Card("Startup blocked", isAlert: true);
            Label("Existing data is preserved. Ask the administrator to review device setup and recovery before taking orders.");
        }
    }

    private async Task LoginPageAsync()
    {
        AndroidDeviceStatusSummary? deviceStatus = _central is not null && _runtime is not null
            ? await _central.ReadDeviceStatusAsync(_runtime)
            : null;
        string subtitle = BuildDeviceSubtitle(deviceStatus);
        Page("Salryn Cafe · Operator sign in", subtitle);
        Status();
        Card("Operator sign in");
        EditText user = Input("Username"), password = Input("Password", InputTypes.ClassText | InputTypes.TextVariationPassword);
        Button("Sign in", async () =>
        {
            var result = await _cashier!.LoginAsync(user.Text ?? "", password.Text ?? "");
            password.Text = "";
            if (result.IsFailure) { Message(result.ErrorDetail.Message); return; }
            _message = "Signed in. Checking device approval and published menu…";
            await CashierPageAsync();
        });
        Card("Device & peripherals");
        Button("Device setup and synchronization", SetupPageAsync, secondary: true);
        PrinterButton();
    }

    private async Task SetupPageAsync()
    {
        AndroidDeviceStatusSummary deviceStatus = await _central!.ReadDeviceStatusAsync(_runtime!);
        Page("Device setup and synchronization", BuildDeviceSubtitle(deviceStatus));
        Status();
        if (deviceStatus.IsRevokedFenced)
        {
            Card("Device revoked and fenced", isAlert: true);
            Label("This device has been revoked by Central. Offline mutations remain locked and retained outbox evidence is preserved.");
        }

        Card("Central authority & enrollment");
        Label("Use the HTTPS origin, tenant/store IDs and server public key supplied independently by your administrator. Device approval is separate from operator sign in.");
        AndroidCentralConfiguration? existing = await _central.ReadConfigurationAsync();
        EditText endpoint = Input("Approved Central HTTPS origin"), tenant = Input("Tenant ID"), store = Input("Store ID");
        EditText pinnedKey = Input("Independently approved server public key (SPKI base64)");
        endpoint.Text = existing?.HttpsUrl; tenant.Text = existing?.TenantId.ToString("D"); store.Text = existing?.StoreId.ToString("D"); pinnedKey.Text = existing?.PinnedServerKey;
        EditText joinCode = Input("One-time join code"), name = Input("Device name");
        if (existing?.DeviceId is { } deviceId) Label("Enrolled device: " + deviceId.ToString("D"), muted: true);
        Button("Request device enrollment", async () =>
        {
            if (!Guid.TryParse(tenant.Text?.Trim(), out Guid tenantId) || !Guid.TryParse(store.Text?.Trim(), out Guid storeId))
            {
                Message("Enter valid Tenant ID and Store ID GUIDs.");
                return;
            }
            var config = new AndroidCentralConfiguration(endpoint.Text?.Trim() ?? "", tenantId, storeId, pinnedKey.Text?.Trim() ?? "");
            Guid enrolled = await _central.EnrollAsync(config, joinCode.Text?.Trim() ?? "", name.Text?.Trim() ?? "");
            joinCode.Text = "";
            Message("Enrollment retained for device " + enrolled.ToString("D") + ". Ask the administrator to approve it, then bootstrap.");
        });
        Button("Connect and bootstrap approved device", async () =>
        {
            Message(await _central.BootstrapAsync(_runtime!));
            await SetupPageAsync();
        });

        Card("Operator offline enrollment & synchronization");
        Label($"Pending local outbox operations: {deviceStatus.PendingOutboxCount}. Reconnect requires the currently signed-in operator's Central credentials.");
        EditText username = Input("Central operator username"), password = Input("Central operator password", InputTypes.ClassText | InputTypes.TextVariationPassword);
        Button("Enroll operator for offline sign in", async () =>
        {
            string enteredPassword = password.Text ?? ""; password.Text = "";
            Message(await _central.EnrollOperatorAsync(_runtime!, username.Text ?? "", enteredPassword));
        });
        Button("Reconnect and synchronize pending operations", async () =>
        {
            if (_cashier!.CurrentOperator is not { } actor) { Message("Sign in locally before synchronizing that operator's pending actions."); return; }
            string enteredPassword = password.Text ?? ""; password.Text = "";
            Message(await _central.ReconnectAsync(_runtime!, username.Text ?? "", enteredPassword, actor.UserId));
            await SetupPageAsync();
        });
        Button(_cashier!.CurrentOperator is not null ? "Back to Cashier" : "Back to operator sign in",
            async () =>
            {
                if (_cashier.CurrentOperator is not null) await CashierPageAsync();
                else await LoginPageAsync();
            }, secondary: true);
        if (_cashier.CurrentOperator is not null)
            Button("Back to operator sign in", LoginPageAsync, secondary: true);
    }

    private async Task CashierPageAsync()
    {
        AndroidDeviceStatusSummary deviceStatus = await _central!.ReadDeviceStatusAsync(_runtime!);
        var openShiftResult = await _runtime!.CashierSessions.FindOpenSessionByUserIdAsync(_cashier!.CurrentOperator!.UserId);
        var openShift = openShiftResult.IsSuccess ? openShiftResult.Value : null;
        string headerMeta = $"Operator: {_cashier.CurrentOperator.DisplayName} · {(openShift is not null ? "Shift OPEN" : "Shift CLOSED")} · {BuildDeviceSubtitle(deviceStatus)}";
        Page("Salryn Cafe · Cashier", headerMeta);
        Status();

        Card("Navigation");
        ButtonRow(
            ("Refresh menu and orders", CashierPageAsync, false),
            ("Audited Refunds (C5)", RefundsPageAsync, true));
        ButtonRow(
            ("Device setup and synchronization", SetupPageAsync, true),
            ("Sign out", async () =>
            {
                _cashier.SignOut(); _order = null; _lines.Clear(); _menu = null;
                _serviceMode = CafeOrderServiceModes.Takeout; _serviceReference = string.Empty;
                await LoginPageAsync();
            }, true));

        await RenderPendingIntentFenceAsync(CashierPageAsync);

        var menu = await _cashier.MenuAsync();
        if (menu.IsFailure)
        {
            Card("Cashier locked", isAlert: true);
            Label(menu.ErrorDetail.Message);
            Label("Cashier actions remain locked until approved pairing, synchronization and a valid offline lease are available.");
            PrinterButton();
            return;
        }
        _menu = menu.Value;

        Card("Cashier drawer / shift", isOk: openShift is not null);
        Label(openShift is not null
            ? $"Open shift active (opening float PHP {Money(openShift.OpeningCash.Cents)})."
            : "No open drawer shift for this operator. Open a shift before checkout or cash refunds.", muted: openShift is null);
        EditText opening = Input("Opening cash (PHP)", MoneyInput);
        Button("Open / recover my shift", () => DispatchAsync(CreateIntent("ShiftOpen",
            shiftOpen: new(Guid.NewGuid(), Cents(opening.Text), DateTimeOffset.UtcNow))));
        EditText counted = Input("Physical counted cash (PHP)", MoneyInput);
        Button("Close my shift with physical count", async () =>
        {
            var session = await _runtime.CashierSessions.FindOpenSessionByUserIdAsync(_cashier.CurrentOperator!.UserId);
            if (session.IsFailure || session.Value is null) { Message("No open drawer was found for this operator."); return; }
            await DispatchAsync(CreateIntent("ShiftClose",
                shiftClose: new(Guid.NewGuid(), session.Value.Id, Cents(counted.Text), DateTimeOffset.UtcNow)));
        }, secondary: true);

        Card("Published menu");
        foreach (var item in _menu.Items.OrderBy(x => x.DisplayOrder))
            if (item.IsTemporarilyUnavailable) Label(item.Name + " · unavailable", muted: true);
            else Button(item.Name + " · PHP " + Money(item.SellingPriceCents), () => { ItemDialog(item); return Task.CompletedTask; });

        Card(_order is null ? $"Current order · New {_serviceMode}" : $"Current order ({_order.Order.ServiceMode}) · {_order.Order.State}");
        EditText serviceRef = Input("Table / Buzzer / Order Ref");
        serviceRef.Text = _serviceReference;
        bool editable = _order is null || _order.Order.State == CafeOrderStates.Open;
        if (editable)
        {
            string activeMode = _serviceMode;
            Button($"Service mode: {activeMode} (tap for {(activeMode == CafeOrderServiceModes.DineIn ? CafeOrderServiceModes.Takeout : CafeOrderServiceModes.DineIn)})",
                () =>
                {
                    _serviceReference = serviceRef.Text?.Trim() ?? string.Empty;
                    _serviceMode = activeMode == CafeOrderServiceModes.DineIn ? CafeOrderServiceModes.Takeout : CafeOrderServiceModes.DineIn;
                    return CashierPageAsync();
                }, secondary: true);
        }
        if (_lines.Count == 0)
        {
            Label("Tap any published menu item above to add lines to this order.", muted: true);
        }
        else
        {
            for (int idx = 0; idx < _lines.Count; idx++)
            {
                var line = _lines[idx];
                int removeIndex = idx;
                string itemName = _menu.Items.FirstOrDefault(x => x.ProductId == line.ProductId)?.Name ?? "Saved item";
                string variantSuffix = line.VariantId is not null
                    ? " (" + (_menu.Variants.FirstOrDefault(v => v.VariantId == line.VariantId)?.Name ?? "Variant") + ")"
                    : string.Empty;
                string lineDesc = $"{line.QuantityX1000 / 1000m:0.###} × {itemName}{variantSuffix}";
                if (!string.IsNullOrWhiteSpace(line.Note)) lineDesc += $" · Note: {line.Note}";
                Label(lineDesc);
                if (editable)
                {
                    Button($"Remove line {idx + 1}: {itemName}", async () =>
                    {
                        _serviceReference = serviceRef.Text?.Trim() ?? string.Empty;
                        if (removeIndex >= 0 && removeIndex < _lines.Count) _lines.RemoveAt(removeIndex);
                        await CashierPageAsync();
                    }, secondary: true);
                }
            }
            if (_order is null)
            {
                Label("Estimated draft subtotal: PHP " + Money(EstimateDraftSubtotalCents()), muted: true);
            }
        }
        if (_order is not null)
            Label("Confirmed total: PHP " + Money(_order.CurrentRevision.Lines.Sum(x => x.LineTotalCents)), heading: true);

        if (editable)
            Button("Save order", () =>
            {
                _serviceReference = serviceRef.Text?.Trim() ?? string.Empty;
                return DispatchAsync(CreateIntent("SaveOrder", draft: new(Guid.NewGuid(), _order?.Order.OrderId ?? _draftOrderId,
                    _order?.Order.OrderVersion ?? 0, DateTimeOffset.UtcNow, _serviceMode,
                    string.IsNullOrWhiteSpace(_serviceReference) ? null : _serviceReference, _lines.ToArray())));
            });
        if (_order?.Order.State == CafeOrderStates.Open)
        {
            Button("Send to preparation", () => DispatchAsync(CreateIntent("SubmitPreparation", action: OrderAction())));
            Button("Park order", () => DispatchAsync(CreateIntent("ParkOrder", action: OrderAction())), secondary: true);
        }
        if (_order?.Order.State == CafeOrderStates.Parked)
        {
            Button("Resume order", () => DispatchAsync(CreateIntent("ResumeOrder", action: OrderAction())));
        }
        if (_order?.Order.State == CafeOrderStates.Submitted) Button("Mark preparing", () => DispatchAsync(CreateIntent("PreparationStatus", action: OrderAction(), target: CafeOrderStates.Preparing)));
        if (_order?.Order.State == CafeOrderStates.Preparing) Button("Mark ready", () => DispatchAsync(CreateIntent("PreparationStatus", action: OrderAction(), target: CafeOrderStates.Ready)));
        if (_order?.Order.State == CafeOrderStates.Ready) Button("Checkout · Cash / Accounts Receivable", CheckoutDialogAsync);
        if (_order is not null && (_order.Order.State == CafeOrderStates.Submitted || _order.Order.State == CafeOrderStates.Preparing))
            Button("Print kitchen ticket", () => PrintKitchenTicketAsync(_order), secondary: true);
        if (_order is not null && !CafeOrderStates.IsTerminal(_order.Order.State) && HasPermission(SalrynPermissions.OrdersCancel))
            Button("Cancel order", () => { CancelOrderDialog(_order); return Task.CompletedTask; }, secondary: true);
        Button("Start another order", async () => { _order = null; _lines.Clear(); _draftOrderId = Guid.NewGuid(); _serviceMode = CafeOrderServiceModes.Takeout; _serviceReference = string.Empty; await CashierPageAsync(); }, secondary: true);

        if (_lastSaleId is not null)
        {
            Card($"Last sale · receipt {_lastReceiptNumber}", isOk: true);
            if (!string.IsNullOrWhiteSpace(_lastPrintStatus)) Label("Print status: " + _lastPrintStatus);
            Button(_lastPrintJobId is null ? "Print customer receipt" : "Retry customer receipt print",
                () => PrintReceiptAsync(_lastSaleId.Value));
        }

        Card("Saved orders");
        var orders = await _cashier.OrdersAsync();
        if (orders.IsFailure) Label(orders.ErrorDetail.Message);
        else
        {
            var activeOrders = orders.Value.Where(x => !CafeOrderStates.IsTerminal(x.State)).OrderBy(x => x.CreatedAtUtc).ToArray();
            if (activeOrders.Length == 0) Label("No open, parked, or in-progress orders.", muted: true);
            foreach (var order in activeOrders)
                Button($"{order.ServiceReference ?? order.ServiceMode} · {order.State} · {order.CreatedAtUtc.ToLocalTime():g}", async () =>
                {
                    var found = await _cashier.FindOrderAsync(order.OrderId);
                    if (found.IsFailure) { Message(found.ErrorDetail.Message); return; }
                    Load(found.Value);
                    await CashierPageAsync();
                }, secondary: true);
        }

        Card("Active deliveries");
        var deliveries = await _cashier.ListDeliveriesAsync();
        if (deliveries.IsFailure) Label(deliveries.ErrorDetail.Message);
        else
        {
            var activeDeliveries = deliveries.Value.Where(x => !CafeDeliveryStatuses.IsTerminal(x.Status)).OrderBy(x => x.CreatedAtUtc).ToArray();
            if (activeDeliveries.Length == 0) Label("No active deliveries.", muted: true);
            foreach (var delivery in activeDeliveries)
            {
                string nextStatus = delivery.Status == CafeDeliveryStatuses.Pending ? CafeDeliveryStatuses.Dispatched : CafeDeliveryStatuses.Delivered;
                Button($"Delivery {delivery.DeliveryId.ToString("N")[..6]} · {delivery.Status} → {nextStatus}", () =>
                    DispatchAsync(CreateIntent("DeliveryAdvance", deliveryAdvance: new(
                        Guid.NewGuid(), delivery.DeliveryId, delivery.Version, DateTimeOffset.UtcNow, nextStatus, null))));
            }
        }

        Card("Bluetooth thermal printer");
        PrinterButton();
    }

    private async Task RenderPendingIntentFenceAsync(Func<Task> refreshPage)
    {
        var pending = await _intents!.ReadAsync();
        if (pending is null) return;
        Card($"Pending action: {pending.Kind} · Op {pending.OperationId:D}", isAlert: true);
        if (!string.IsNullOrWhiteSpace(pending.LastErrorMessage))
            Label($"Last rejection ({pending.AttemptCount} attempts): [{pending.LastErrorCode}] {pending.LastErrorMessage}");
        else
            Label("An earlier action needs confirmation. Retry its saved request before creating another action.");
        Button("Retry saved action", () => DispatchAsync(pending));
        EditText dismissReason = Input("Reason to dismiss rejected action (audit logged)");
        Button("Dismiss rejected action (audit logged)", async () =>
        {
            if (string.IsNullOrWhiteSpace(dismissReason.Text)) { Message("Enter a reason before dismissing a rejected action."); return; }
            await _intents.ResolveRejectedAsync(pending.OperationId, _cashier!.CurrentOperator!.UserId, dismissReason.Text!, DateTimeOffset.UtcNow);
            Message($"Rejected action {pending.OperationId:D} dismissed and recorded in resolution history.");
            await refreshPage();
        }, secondary: true);
    }

    private void CancelOrderDialog(CafeOrderDetail order)
    {
        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        EditText reasonInput = InputText(form, "Cancellation reason", InputTypes.ClassText, "");
        Guid cancelOpId = Guid.NewGuid();
        new AlertDialog.Builder(this)!.SetTitle($"Cancel order ({order.Order.State})")!.SetView(form)!
            .SetNegativeButton("Keep order", (_, _) => { })!
            .SetPositiveButton("Confirm cancel", (_, _) => _ = RunUiAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(reasonInput.Text)) { Message("Enter a cancellation reason."); return; }
                await DispatchAsync(CreateIntent("CancelOrder", cancelOrder: new(
                    cancelOpId, order.Order.OrderId, order.Order.OrderVersion, DateTimeOffset.UtcNow, reasonInput.Text.Trim())));
            }))!.Show();
    }

    private void ItemDialog(MenuPublicationItemSnapshot item)
    {
        if (_order is not null && _order.Order.State != CafeOrderStates.Open) { Message("Submitted orders are frozen. Start another order to add items."); return; }
        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        EditText quantity = new(this) { Hint = "Quantity", ContentDescription = "Quantity", Text = "1", InputType = MoneyInput };
        quantity.SetMinHeight(Dp(48));
        EditText note = new(this) { Hint = "Preparation note", ContentDescription = "Preparation note" };
        note.SetMinHeight(Dp(48));
        form.AddView(quantity); form.AddView(note);
        var variants = _menu!.Variants.Where(x => x.ProductId == item.ProductId && !x.IsTemporarilyUnavailable).OrderBy(x => x.DisplayOrder).ToArray();
        Spinner variant = new(this) { ContentDescription = "Menu variant" };
        variant.SetMinimumHeight(Dp(48));
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
        long orderSubtotalCents = _order!.CurrentRevision.Lines.Sum(x => x.LineTotalCents);
        long deliveryFeeCents = 0;
        var deliveryRes = await _cashier.FindDeliveryByOrderAsync(_order.Order.OrderId);
        if (deliveryRes.IsSuccess && deliveryRes.Value is { Delivery.Status: CafeDeliveryStatuses.Pending } pendingDelivery)
        {
            deliveryFeeCents = pendingDelivery.CurrentRevision.DeliveryFeeCents;
        }
        long totalDueCents = orderSubtotalCents + deliveryFeeCents;

        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        string summaryText = deliveryFeeCents > 0
            ? $"Order: PHP {Money(orderSubtotalCents)} + Delivery fee: PHP {Money(deliveryFeeCents)} = Total due: PHP {Money(totalDueCents)}"
            : $"Total due: PHP {Money(totalDueCents)} (Cash / Accounts Receivable only)";
        TextView totalBanner = new(this) { Text = summaryText, TextSize = 16 };
        totalBanner.SetTextColor(Espresso);
        totalBanner.SetTypeface(null, TypefaceStyle.Bold);
        totalBanner.SetPadding(0, Dp(4), 0, Dp(8));
        form.AddView(totalBanner);

        EditText cash = new(this) { Hint = "Cash received (PHP)", ContentDescription = "Cash received", Text = Money(totalDueCents), InputType = MoneyInput };
        cash.SetMinHeight(Dp(48));
        EditText ar = new(this) { Hint = "Accounts Receivable (PHP)", ContentDescription = "Accounts Receivable amount", Text = "0", InputType = MoneyInput };
        ar.SetMinHeight(Dp(48));
        Spinner customer = new(this) { ContentDescription = "Credit customer" };
        customer.SetMinimumHeight(Dp(48));
        customer.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            new[] { "Select credit customer" }.Concat(options.Select(x => x.Name)).ToArray());
        form.AddView(cash); form.AddView(ar); form.AddView(customer);

        ScrollView scroll = new(this); scroll.AddView(form);
        new AlertDialog.Builder(this)!.SetTitle("Confirm tender")!.SetView(scroll)!.SetNegativeButton("Cancel", (sender, args) => { })!
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
                    await DispatchAsync(CreateIntent("Checkout", checkout: result.Value));
                });
            })!.Show();
    }

    private MobilePendingCashierIntent CreateIntent(string kind, MobileOrderDraft? draft = null, MobileOrderAction? action = null,
        string? target = null, MobileCheckoutIntent? checkout = null, MobileShiftOpenIntent? shiftOpen = null,
        MobileShiftCloseIntent? shiftClose = null, MobileRefundRequestIntent? refundRequest = null,
        MobileRefundResolutionIntent? refundResolution = null, MobileRefundCommitIntent? refundCommit = null,
        MobileOrderCancelAction? cancelOrder = null, MobileDeliveryAdvanceIntent? deliveryAdvance = null) =>
        new(kind, draft, action, target, checkout, _cashier!.CurrentOperator!.UserId, shiftOpen, shiftClose,
            refundRequest, refundResolution, refundCommit, CancelOrder: cancelOrder, DeliveryAdvance: deliveryAdvance);

    private async Task DispatchAsync(MobilePendingCashierIntent intent)
    {
        if (_cashier!.CurrentOperator?.UserId != intent.ActorUserId) { Message("Sign in as the original operator to reconcile the saved action."); return; }
        await _intents!.SaveBeforeDispatchAsync(intent);
        Guid? orderId = null;
        bool openRefunds = false;
        switch (intent.Kind)
        {
            case "ShiftOpen" when intent.ShiftOpen is not null:
                var opened = await _cashier.OpenShiftAsync(intent.ShiftOpen);
                if (opened.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, opened.ErrorDetail.Code, opened.ErrorDetail.Message);
                    Message(opened.ErrorDetail.Message + " Saved request retained."); return;
                }
                _message = "Your cashier shift is open."; break;
            case "ShiftClose" when intent.ShiftClose is not null:
                var closed = await _cashier.CloseShiftAsync(intent.ShiftClose);
                if (closed.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, closed.ErrorDetail.Code, closed.ErrorDetail.Message);
                    Message(closed.ErrorDetail.Message + " Saved request retained."); return;
                }
                _message = "Physical count recorded and shift closed."; break;
            case "SaveOrder" when intent.Draft is not null:
                var saved = await _cashier.SaveOrderAsync(intent.Draft);
                if (saved.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, saved.ErrorDetail.Code, saved.ErrorDetail.Message);
                    Message(saved.ErrorDetail.Message + " Saved request retained."); return;
                }
                orderId = saved.Value.OrderId;
                _message = "Order saved."; break;
            case "ParkOrder" when intent.Action is not null:
                var parked = await _cashier.ParkOrderAsync(intent.Action);
                if (parked.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, parked.ErrorDetail.Code, parked.ErrorDetail.Message);
                    Message(parked.ErrorDetail.Message + " Saved request retained."); return;
                }
                orderId = parked.Value.OrderId;
                _message = "Order parked."; break;
            case "ResumeOrder" when intent.Action is not null:
                var resumed = await _cashier.ResumeOrderAsync(intent.Action);
                if (resumed.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, resumed.ErrorDetail.Code, resumed.ErrorDetail.Message);
                    Message(resumed.ErrorDetail.Message + " Saved request retained."); return;
                }
                orderId = resumed.Value.OrderId;
                _message = "Order resumed."; break;
            case "CancelOrder" when intent.CancelOrder is not null:
                var cancelled = await _cashier.CancelOrderAsync(intent.CancelOrder);
                if (cancelled.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, cancelled.ErrorDetail.Code, cancelled.ErrorDetail.Message);
                    Message(cancelled.ErrorDetail.Message + " Saved request retained."); return;
                }
                _order = null; _lines.Clear(); _draftOrderId = Guid.NewGuid();
                _serviceMode = CafeOrderServiceModes.Takeout; _serviceReference = string.Empty;
                _message = "Order cancelled."; break;
            case "SubmitPreparation" when intent.Action is not null:
                var submitted = await _cashier.SubmitPreparationAsync(intent.Action);
                if (submitted.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, submitted.ErrorDetail.Code, submitted.ErrorDetail.Message);
                    Message(submitted.ErrorDetail.Message + " Saved request retained."); return;
                }
                orderId = submitted.Value.OrderId;
                _message = "Order sent to preparation."; break;
            case "PreparationStatus" when intent.Action is not null && intent.TargetState is not null:
                var prepared = await _cashier.SetPreparationStatusAsync(intent.Action, intent.TargetState);
                if (prepared.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, prepared.ErrorDetail.Code, prepared.ErrorDetail.Message);
                    Message(prepared.ErrorDetail.Message + " Saved request retained."); return;
                }
                orderId = prepared.Value.OrderId;
                _message = $"Order marked {intent.TargetState}."; break;
            case "Checkout" when intent.Checkout is not null:
                var sale = await _cashier.CompleteAsync(intent.Checkout);
                if (sale.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, sale.ErrorDetail.Code, sale.ErrorDetail.Message);
                    Message(sale.ErrorDetail.Message + " Saved checkout retained; do not create another sale."); return;
                }
                orderId = sale.Value.OrderId;
                _lastSaleId = sale.Value.SaleId;
                _lastReceiptNumber = sale.Value.ReceiptNumber;
                _lastPrintJobId = null;
                _lastPrintStatus = "Not yet printed";
                _message = "Sale committed · receipt " + sale.Value.ReceiptNumber + ". Printing and central application require separate confirmation."; break;
            case "DeliveryAdvance" when intent.DeliveryAdvance is not null:
                var advanced = await _cashier.AdvanceDeliveryAsync(intent.DeliveryAdvance);
                if (advanced.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, advanced.ErrorDetail.Code, advanced.ErrorDetail.Message);
                    Message(advanced.ErrorDetail.Message + " Saved delivery update retained."); return;
                }
                _message = $"Delivery updated to {advanced.Value.Status}."; break;
            case "RefundRequest" when intent.RefundRequest is not null:
                var refReq = await _cashier.RequestRefundAsync(intent.RefundRequest);
                if (refReq.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, refReq.ErrorDetail.Code, refReq.ErrorDetail.Message);
                    Message(refReq.ErrorDetail.Message + " Saved refund request retained."); return;
                }
                openRefunds = true;
                _message = $"Refund {refReq.Value.State} created."; break;
            case "RefundResolution" when intent.RefundResolution is not null:
                var refRes = await _cashier.ResolveRefundAsync(intent.RefundResolution);
                if (refRes.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, refRes.ErrorDetail.Code, refRes.ErrorDetail.Message);
                    Message(refRes.ErrorDetail.Message + " Saved refund resolution retained."); return;
                }
                openRefunds = true;
                _message = $"Refund {refRes.Value.State} (v{refRes.Value.Version})."; break;
            case "RefundCommit" when intent.RefundCommit is not null:
                var refCommit = await _cashier.CommitRefundAsync(intent.RefundCommit);
                if (refCommit.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, refCommit.ErrorDetail.Code, refCommit.ErrorDetail.Message);
                    Message(refCommit.ErrorDetail.Message + " Saved refund commit retained."); return;
                }
                openRefunds = true;
                _message = $"Refund committed · PHP {Money(refCommit.Value.TotalRefundCents)}."; break;
            default: throw new InvalidOperationException("Unsupported saved action. Preserve it for recovery.");
        }
        await _intents.ClearConfirmedAsync(intent.OperationId);
        if (openRefunds) { await RefundsPageAsync(); return; }
        if (orderId is { } id)
        {
            var found = await _cashier.FindOrderAsync(id);
            if (found.IsSuccess) Load(found.Value);
        }
        await CashierPageAsync();
    }

    private async Task PrintReceiptAsync(Guid saleId)
    {
        var stations = await _cashier!.ListPrinterStationsAsync();
        if (stations.IsFailure) { Message(stations.ErrorDetail.Message); return; }
        var station = stations.Value.FirstOrDefault(x => x.IsActive && x.StationKind == CafePrinterStationKind.CustomerReceipt);
        if (station is null) { _lastPrintStatus = "No active receipt printer station configured."; Message(_lastPrintStatus); return; }

        Guid jobId;
        if (_lastPrintJobId is { } existingJobId)
        {
            jobId = existingJobId;
        }
        else
        {
            var created = await _cashier.CreateReceiptPrintJobAsync(new(Guid.NewGuid(), Guid.NewGuid(), station.StationId,
                saleId, CafePrintCopyKind.Original, null, DateTimeOffset.UtcNow));
            if (created.IsFailure) { Message(created.ErrorDetail.Message); return; }
            jobId = created.Value.Job.JobId;
            _lastPrintJobId = jobId;
        }

        IReceiptPrinterAdapter adapter = string.IsNullOrWhiteSpace(station.RouteKey)
            ? new DisabledReceiptPrinterAdapter("Printer route key is not configured.")
            : new MobilePrinterTransportAdapter(new AndroidBluetoothPrinterTransport(this), station.RouteKey, station.Name);
        var attempt = await _cashier.ExecutePrintAttemptAsync(jobId, adapter);
        if (attempt.IsFailure) { _lastPrintStatus = attempt.ErrorDetail.Message; Message(_lastPrintStatus); return; }
        _lastPrintStatus = $"{attempt.Value.Attempt.Status} (attempt #{attempt.Value.Attempt.AttemptNumber}): {attempt.Value.Attempt.ResultMessage}";
        if (attempt.Value.Attempt.Status == CafePrintAttemptStatus.Succeeded) _lastPrintJobId = null;
        Message(_lastPrintStatus);
        await CashierPageAsync();
    }

    private async Task PrintKitchenTicketAsync(CafeOrderDetail order)
    {
        var stations = await _cashier!.ListPrinterStationsAsync();
        if (stations.IsFailure) { Message(stations.ErrorDetail.Message); return; }
        var station = stations.Value.FirstOrDefault(x => x.IsActive && x.StationKind == CafePrinterStationKind.Kitchen);
        if (station is null) { Message("No active kitchen printer station configured."); return; }

        var created = await _cashier.CreateKitchenPrintJobAsync(new(Guid.NewGuid(), Guid.NewGuid(), station.StationId,
            order.Order.OrderId, order.CurrentRevision.RevisionId, CafePrintCopyKind.Original, null, DateTimeOffset.UtcNow));
        if (created.IsFailure) { Message(created.ErrorDetail.Message); return; }

        IReceiptPrinterAdapter adapter = string.IsNullOrWhiteSpace(station.RouteKey)
            ? new DisabledReceiptPrinterAdapter("Kitchen printer route key is not configured.")
            : new MobilePrinterTransportAdapter(new AndroidBluetoothPrinterTransport(this), station.RouteKey, station.Name);
        var attempt = await _cashier.ExecutePrintAttemptAsync(created.Value.Job.JobId, adapter);
        Message(attempt.IsSuccess
            ? $"Kitchen ticket {attempt.Value.Attempt.Status} (attempt #{attempt.Value.Attempt.AttemptNumber}): {attempt.Value.Attempt.ResultMessage}"
            : attempt.ErrorDetail.Message);
    }

    private void Load(CafeOrderDetail order)
    {
        _order = order; _serviceMode = order.Order.ServiceMode; _serviceReference = order.Order.ServiceReference ?? string.Empty;
        _lines.Clear(); _lines.AddRange(order.CurrentRevision.Lines.Select(x => new CafeOrderLineMutation(x.OrderLineId,
            x.ProductId, x.VariantId, x.ModifierSelections.Select(m => m.OptionId).ToArray(), x.QuantityX1000, x.Note, x.DisplayOrder)));
    }

    private async Task RefundsPageAsync()
    {
        Page("Salryn Cafe · Audited Refunds (C5)", $"Operator: {_cashier!.CurrentOperator?.DisplayName}");
        Status();
        Button("Back to Cashier", CashierPageAsync, secondary: true);
        await RenderPendingIntentFenceAsync(RefundsPageAsync);

        bool canApprove = HasPermission(SalrynPermissions.SalesRefundApprove);
        bool canRequest = HasPermission(SalrynPermissions.SalesRefundRequest);

        Card("Refundable Completed Sales");
        var sales = await _cashier.ListRefundableSalesAsync();
        if (sales.IsFailure)
        {
            Label(sales.ErrorDetail.Message);
        }
        else if (sales.Value.Count == 0)
        {
            Label("No refundable completed sales found.", muted: true);
        }
        else if (!canRequest)
        {
            Label("Your account has refund view/approval access; refund requests require Sales.RefundRequest.", muted: true);
        }
        else
        {
            foreach (var sale in sales.Value)
            {
                Button($"{sale.ReceiptNumber} · PHP {Money(sale.TotalCents)} (Cash {Money(sale.CashNetCents)} / AR {Money(sale.ArCents)})",
                    () => { RequestRefundDialog(sale); return Task.CompletedTask; });
            }
        }

        Card("Existing Refund Requests");
        var refunds = await _cashier.ListRefundsAsync();
        if (refunds.IsFailure) { Label(refunds.ErrorDetail.Message); return; }
        if (refunds.Value.Count == 0) Label("No refund requests recorded.", muted: true);
        foreach (var refund in refunds.Value)
        {
            Label($"{refund.ReceiptNumber ?? refund.SaleId.ToString("N")[..8]} · {refund.State} · PHP {(refund.TotalRefundCents is { } t ? Money(t) : "0.00")} · {refund.Reason}");
            if (refund.State == "Requested")
            {
                if (canApprove)
                {
                    Button($"Approve refund {refund.ReceiptNumber}", () => { ResolveRefundDialog(refund, approve: true); return Task.CompletedTask; });
                    Button($"Reject refund {refund.ReceiptNumber}", () => { ResolveRefundDialog(refund, approve: false); return Task.CompletedTask; }, secondary: true);
                }
                else
                {
                    Label("Awaiting authorized manager approval (Sales.RefundApprove).", muted: true);
                }
            }
            else if (refund.State == "Approved")
            {
                if (canApprove)
                {
                    Button($"Commit refund {refund.ReceiptNumber} (Cash / AR)", () => { CommitRefundDialog(refund); return Task.CompletedTask; });
                }
                else
                {
                    Label("Approved — awaiting authorized commit (Sales.RefundApprove).", muted: true);
                }
            }
        }
    }

    private void ResolveRefundDialog(MobileRefundSummary refund, bool approve)
    {
        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        string defaultNote = approve ? "Approved on Android POS" : "Rejected on Android POS";
        EditText reasonInput = InputText(form, approve ? "Approval note" : "Rejection reason", InputTypes.ClassText, defaultNote);
        Guid resolveOpId = Guid.NewGuid();
        new AlertDialog.Builder(this)!.SetTitle($"{(approve ? "Approve" : "Reject")} Refund · {refund.ReceiptNumber}")!.SetView(form)!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .SetPositiveButton(approve ? "Confirm Approval" : "Confirm Rejection", (_, _) => _ = RunUiAsync(async () =>
            {
                string note = string.IsNullOrWhiteSpace(reasonInput.Text) ? defaultNote : reasonInput.Text.Trim();
                await DispatchAsync(CreateIntent("RefundResolution", refundResolution: new(
                    resolveOpId, refund.RefundId, refund.SaleId, refund.Version, DateTimeOffset.UtcNow, approve, note)));
            }))!.Show();
    }

    private void RequestRefundDialog(MobileRefundableSaleSummary sale)
    {
        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        Spinner itemSpinner = new(this) { ContentDescription = "Sale line" };
        itemSpinner.SetMinimumHeight(Dp(48));
        itemSpinner.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            sale.Items.Select(i => $"{i.ProductName} (Rem {i.RemainingQuantityX1000 / 1000m:0.###})").ToArray());
        Spinner dispSpinner = new(this) { ContentDescription = "Stock disposition" };
        dispSpinner.SetMinimumHeight(Dp(48));
        string[] dispositions = [CafeRefundStockDispositions.Restock, CafeRefundStockDispositions.DamagedNonRestockable, CafeRefundStockDispositions.ServiceNoStock];
        dispSpinner.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, dispositions);
        if (sale.Items.Count > 0) dispSpinner.SetSelection(sale.Items[0].TrackStock ? 0 : 2);
        itemSpinner.ItemSelected += (_, args) =>
        {
            if (args.Position >= 0 && args.Position < sale.Items.Count)
                dispSpinner.SetSelection(sale.Items[args.Position].TrackStock ? 0 : 2);
        };
        EditText qtyInput = InputText(form, "Refund quantity", MoneyInput, "1");
        EditText reasonInput = InputText(form, "Refund reason", InputTypes.ClassText, "");
        form.AddView(itemSpinner); form.AddView(dispSpinner);
        Guid reqOpId = Guid.NewGuid(), refundId = Guid.NewGuid(), lineId = Guid.NewGuid();
        ScrollView scroll = new(this); scroll.AddView(form);
        new AlertDialog.Builder(this)!.SetTitle($"Request Refund · {sale.ReceiptNumber}")!.SetView(scroll)!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .SetPositiveButton("Request Refund", (_, _) => _ = RunUiAsync(async () =>
            {
                var item = sale.Items[Math.Max(0, itemSpinner.SelectedItemPosition)];
                decimal qty = decimal.Parse(qtyInput.Text ?? "", CultureInfo.InvariantCulture);
                if (qty <= 0 || qty * 1000 != decimal.Truncate(qty * 1000)) { Message("Use a positive refund quantity."); return; }
                string disposition = dispositions[Math.Max(0, dispSpinner.SelectedItemPosition)];
                string cleanReason = string.IsNullOrWhiteSpace(reasonInput.Text) ? "Customer refund" : reasonInput.Text.Trim();
                await DispatchAsync(CreateIntent("RefundRequest", refundRequest: new(reqOpId, refundId, sale.SaleId, 0, DateTimeOffset.UtcNow,
                    cleanReason, [new(lineId, item.SaleItemId, checked((long)(qty * 1000)), disposition, null)])));
            }))!.Show();
    }

    private void CommitRefundDialog(MobileRefundSummary refund)
    {
        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        string defaultAmount = refund.TotalRefundCents is { } total ? Money(total) : "0.00";
        EditText cashInput = InputText(form, "Cash Refund (PHP)", MoneyInput, defaultAmount);
        EditText arInput = InputText(form, "Accounts Receivable Reversal (PHP)", MoneyInput, "0.00");
        Guid commitOpId = Guid.NewGuid();
        ScrollView scroll = new(this); scroll.AddView(form);
        new AlertDialog.Builder(this)!.SetTitle($"Commit Refund · {refund.ReceiptNumber}")!.SetView(scroll)!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .SetPositiveButton("Commit Cash / AR", (_, _) => _ = RunUiAsync(async () =>
            {
                if (_cashier?.CurrentOperator is not { } actor) { Message("Sign in with an authorized operator account first."); return; }
                var session = await _runtime!.CashierSessions.FindOpenSessionByUserIdAsync(actor.UserId);
                if (session.IsFailure || session.Value is null) { Message("Open a cashier shift before committing a refund."); return; }
                long cashCents = Cents(cashInput.Text), arCents = Cents(arInput.Text);
                List<CafeRefundSettlementMutation> settlements = [];
                if (cashCents > 0) settlements.Add(new(Guid.NewGuid(), CafeRefundSettlementMethods.Cash, cashCents));
                if (arCents > 0) settlements.Add(new(Guid.NewGuid(), CafeRefundSettlementMethods.AccountsReceivable, arCents));
                await DispatchAsync(CreateIntent("RefundCommit", refundCommit: new(commitOpId, refund.RefundId, refund.SaleId, refund.Version,
                    session.Value.Id, DateTimeOffset.UtcNow, settlements)));
            }))!.Show();
    }

    private long EstimateDraftSubtotalCents()
    {
        if (_menu is null) return 0;
        long total = 0;
        foreach (var line in _lines)
        {
            long unitCents = line.VariantId is not null
                ? (_menu.Variants.FirstOrDefault(v => v.VariantId == line.VariantId)?.SellingPriceCents ?? 0)
                : (_menu.Items.FirstOrDefault(i => i.ProductId == line.ProductId)?.SellingPriceCents ?? 0);
            long modCents = line.ModifierOptionIds.Sum(id =>
                _menu.ModifierOptions.FirstOrDefault(m => m.OptionId == id)?.PriceDeltaCents ?? 0);
            total += (long)Math.Round((unitCents + modCents) * (line.QuantityX1000 / 1000m), MidpointRounding.AwayFromZero);
        }
        return total;
    }

    private bool HasPermission(string permission) =>
        _cashier?.CurrentOperator?.Permissions.Contains(permission, StringComparer.Ordinal) == true;

    private static string BuildDeviceSubtitle(AndroidDeviceStatusSummary? status)
    {
        if (status is null) return "Initializing device state";
        if (status.IsRevokedFenced) return $"REVOKED FENCED · Pending sync: {status.PendingOutboxCount}";
        if (status.DeviceId is null) return "Device unenrolled";
        string lease = status.LeaseExpiresAtUtc is { } exp
            ? (exp <= DateTimeOffset.UtcNow ? "Lease EXPIRED" : $"Lease valid until {exp.ToLocalTime():t}")
            : "No offline lease";
        return $"{lease} · Pending sync: {status.PendingOutboxCount}";
    }

    private EditText InputText(LinearLayout parent, string hint, InputTypes type, string initialValue)
    {
        TextView caption = new(this) { Text = hint, TextSize = 15 };
        caption.SetTextColor(Espresso);
        parent.AddView(caption);
        EditText edit = new(this) { Hint = hint, InputType = type, ContentDescription = hint, Text = initialValue };
        edit.SetTextColor(Espresso); edit.SetMinHeight(Dp(48)); parent.AddView(edit);
        return edit;
    }

    private MobileOrderAction OrderAction() => new(Guid.NewGuid(), _order!.Order.OrderId, _order.Order.OrderVersion, DateTimeOffset.UtcNow);

    private void Page(string title, string? subtitle = null)
    {
        _buttons.Clear(); _status = null; _currentCard = null;
        ScrollView scroll = new(this); scroll.SetBackgroundColor(Cream);
        LinearLayout outer = new(this) { Orientation = Orientation.Vertical };

        LinearLayout header = new(this) { Orientation = Orientation.Vertical };
        header.SetBackgroundColor(Espresso);
        header.SetPadding(Dp(20), Dp(20), Dp(20), Dp(16));
        TextView titleView = new(this) { Text = title, TextSize = 22 };
        titleView.SetTextColor(Cream);
        titleView.SetTypeface(null, TypefaceStyle.Bold);
        header.AddView(titleView);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            TextView subView = new(this) { Text = subtitle, TextSize = 13 };
            subView.SetTextColor(Latte);
            subView.SetPadding(0, Dp(4), 0, 0);
            header.AddView(subView);
        }
        outer.AddView(header);

        _root = new(this) { Orientation = Orientation.Vertical };
        _root.SetPadding(Dp(16), Dp(12), Dp(16), Dp(28));
        outer.AddView(_root);

        scroll.AddView(outer);
        SetContentView(scroll);
    }

    private void Card(string heading, bool isAlert = false, bool isOk = false)
    {
        LinearLayout card = new(this) { Orientation = Orientation.Vertical };
        GradientDrawable bg = new();
        bg.SetCornerRadius(Dp(12));
        bg.SetColor(isAlert ? AlertBg : isOk ? OkBg : CardSurface);
        bg.SetStroke(Dp(1), isAlert ? AlertBorder : isOk ? OkBorder : Latte);
        card.Background = bg;
        card.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        LinearLayout.LayoutParams lp = new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.SetMargins(0, Dp(6), 0, Dp(6));
        card.LayoutParameters = lp;
        _root!.AddView(card);
        _currentCard = card;

        TextView titleView = new(this) { Text = heading, TextSize = 18 };
        titleView.SetTextColor(isAlert ? AlertText : isOk ? OkText : Espresso);
        titleView.SetTypeface(null, TypefaceStyle.Bold);
        titleView.SetPadding(0, 0, 0, Dp(6));
        card.AddView(titleView);
    }

    private LinearLayout ActiveContainer => _currentCard ?? _root!;

    private TextView Label(string text, bool heading = false, bool muted = false)
    {
        TextView label = new(this) { Text = text, TextSize = heading ? 19 : 15 };
        label.SetTextColor(muted ? Mocha : Espresso);
        label.SetPadding(0, Dp(4), 0, Dp(4));
        if (heading) label.SetTypeface(null, TypefaceStyle.Bold);
        ActiveContainer.AddView(label);
        return label;
    }

    private void Status()
    {
        LinearLayout banner = new(this) { Orientation = Orientation.Vertical };
        GradientDrawable bg = new();
        bg.SetCornerRadius(Dp(10));
        bg.SetColor(Latte);
        bg.SetStroke(Dp(1), Copper);
        banner.Background = bg;
        banner.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        LinearLayout.LayoutParams lp = new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.SetMargins(0, Dp(4), 0, Dp(6));
        banner.LayoutParameters = lp;
        _status = new(this) { Text = _message, TextSize = 15 };
        _status.SetTextColor(Espresso);
        _status.AccessibilityLiveRegion = AccessibilityLiveRegion.Polite;
        banner.AddView(_status);
        _root!.AddView(banner);
    }

    private EditText Input(string label, InputTypes type = InputTypes.ClassText)
    {
        Label(label);
        EditText input = new(this) { Hint = label, InputType = type, ContentDescription = label };
        input.SetTextColor(Espresso);
        input.SetHintTextColor(Mocha);
        input.SetMinHeight(Dp(48));
        ActiveContainer.AddView(input);
        return input;
    }

    private void ButtonRow(params (string Label, Func<Task> Action, bool Secondary)[] items)
    {
        LinearLayout row = new(this) { Orientation = Orientation.Horizontal };
        LinearLayout.LayoutParams rowLp = new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        rowLp.SetMargins(0, Dp(4), 0, Dp(4));
        row.LayoutParameters = rowLp;
        for (int i = 0; i < items.Length; i++)
        {
            var (label, action, secondary) = items[i];
            Button btn = CreateStyledButton(label, action, secondary);
            LinearLayout.LayoutParams lp = new(0, ViewGroup.LayoutParams.WrapContent, 1f);
            lp.SetMargins(i == 0 ? 0 : Dp(4), 0, i == items.Length - 1 ? 0 : Dp(4), 0);
            btn.LayoutParameters = lp;
            row.AddView(btn);
        }
        ActiveContainer.AddView(row);
    }

    private void Button(string label, Func<Task> action, bool secondary = false)
    {
        Button button = CreateStyledButton(label, action, secondary);
        LinearLayout.LayoutParams lp = new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.SetMargins(0, Dp(5), 0, Dp(5));
        button.LayoutParameters = lp;
        ActiveContainer.AddView(button);
    }

    private Button CreateStyledButton(string label, Func<Task> action, bool secondary)
    {
        Button button = new(this) { Text = label, ContentDescription = label, Enabled = !_busy };
        button.SetAllCaps(false);
        button.SetTextColor(Cream);
        GradientDrawable bg = new();
        bg.SetCornerRadius(Dp(10));
        bg.SetColor(secondary ? Mocha : DarkCopper);
        button.Background = bg;
        button.SetMinHeight(Dp(48));
        button.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        button.Click += async (sender, args) => await RunUiAsync(action);
        _buttons.Add(button);
        return button;
    }

    private async Task RunUiAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true; foreach (var button in _buttons) button.Enabled = false;
        try { await action(); }
        catch (InvalidOperationException error) { Message(error.Message); }
        catch (Exception error) when (error is FormatException or OverflowException) { Message("Enter a nonnegative amount with at most two decimal places."); }
        catch (Exception error) { global::Android.Util.Log.Warn("SalrynCafe", "Unconfirmed action: " + error.GetType().Name); Message("Action outcome is unconfirmed. Saved requests are preserved; retry the saved action after checking authorization."); }
        finally { _busy = false; foreach (var button in _buttons) button.Enabled = true; }
    }

    private void Message(string text) { _message = text; if (_status is not null) _status.Text = text; }
    private void PrinterButton() => Button("Bluetooth printer permissions", () => { AndroidBluetoothPrinterTransport.RequestRequiredPermissions(this, 1001); Message("Printer permission is separate from device approval. No print success is claimed."); return Task.CompletedTask; }, secondary: true);
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
