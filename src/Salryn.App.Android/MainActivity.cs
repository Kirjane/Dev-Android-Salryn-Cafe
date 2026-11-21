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
using Salryn.Core.Services;
using Salryn.Mobile.Foundation.Cashier;
using Salryn.Mobile.Foundation.Printing;
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
    private AndroidCentralConnection? _central;
    private LinearLayout? _root;
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
            _intents = new(secrets);
            _central = new(secrets, () => new(DateTimeOffset.UtcNow, SystemClock.ElapsedRealtime(), ReadBootCount()));
            LoginPage();
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
        Button("Device setup and synchronization", SetupPageAsync);
        PrinterButton();
    }

    private async Task SetupPageAsync()
    {
        Page("Device setup and synchronization"); Status();
        Label("Use the HTTPS origin, tenant/store IDs and server public key supplied independently by your administrator. Device approval is separate from operator sign in.");
        AndroidCentralConfiguration? existing = await _central!.ReadConfigurationAsync();
        EditText endpoint = Input("Approved Central HTTPS origin"), tenant = Input("Tenant ID"), store = Input("Store ID");
        EditText pinnedKey = Input("Independently approved server public key (SPKI base64)");
        endpoint.Text = existing?.HttpsUrl; tenant.Text = existing?.TenantId.ToString("D"); store.Text = existing?.StoreId.ToString("D"); pinnedKey.Text = existing?.PinnedServerKey;
        EditText joinCode = Input("One-time join code"), name = Input("Device name");
        if (existing?.DeviceId is { } deviceId) Label("Enrolled device: " + deviceId.ToString("D"));
        Button("Request device enrollment", async () =>
        {
            var config = new AndroidCentralConfiguration(endpoint.Text?.Trim() ?? "", Guid.Parse(tenant.Text ?? ""), Guid.Parse(store.Text ?? ""), pinnedKey.Text?.Trim() ?? "");
            Guid enrolled = await _central.EnrollAsync(config, joinCode.Text?.Trim() ?? "", name.Text?.Trim() ?? "");
            joinCode.Text = "";
            Message("Enrollment retained for device " + enrolled.ToString("D") + ". Ask the administrator to approve it, then bootstrap.");
        });
        Button("Connect and bootstrap approved device", async () => Message(await _central.BootstrapAsync(_runtime!)));
        Label("Reconnect requires the currently signed-in operator's Central credentials. Pending actions retain their original actor and operation IDs.");
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
        });
        Button("Back to operator sign in", () => { LoginPage(); return Task.CompletedTask; });
    }

    private async Task CashierPageAsync()
    {
        Page("Salryn Cafe · Cashier"); Label("Operator: " + _cashier!.CurrentOperator?.DisplayName); Status();
        Button("Sign out", () => { _cashier.SignOut(); _order = null; _lines.Clear(); _menu = null; _serviceMode = CafeOrderServiceModes.Takeout; _serviceReference = string.Empty; LoginPage(); return Task.CompletedTask; });
        Button("Refresh menu and orders", CashierPageAsync);
        Button("Audited Refunds (C5)", RefundsPageAsync);
        Button("Device setup and synchronization", SetupPageAsync);
        var pending = await _intents!.ReadAsync();
        if (pending is not null)
        {
            Label($"Pending action: {pending.Kind} · Op {pending.OperationId:D}", true);
            if (!string.IsNullOrWhiteSpace(pending.LastErrorMessage))
                Label($"Last rejection ({pending.AttemptCount} attempts): [{pending.LastErrorCode}] {pending.LastErrorMessage}");
            else
                Label("An earlier action needs confirmation. Retry its saved request before creating another action.");
            Button("Retry saved action", () => DispatchAsync(pending));
            EditText dismissReason = Input("Reason to dismiss rejected action (audit logged)");
            Button("Dismiss rejected action (audit logged)", async () =>
            {
                if (string.IsNullOrWhiteSpace(dismissReason.Text)) { Message("Enter a reason before dismissing a rejected action."); return; }
                await _intents.ResolveRejectedAsync(pending.OperationId, _cashier.CurrentOperator!.UserId, dismissReason.Text!, DateTimeOffset.UtcNow);
                Message($"Rejected action {pending.OperationId:D} dismissed and recorded in resolution history.");
                await CashierPageAsync();
            });
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
        Button("Open / recover my shift", () => DispatchAsync(Intent("ShiftOpen",
            shiftOpen: new(Guid.NewGuid(), Cents(opening.Text), DateTimeOffset.UtcNow))));
        EditText counted = Input("Physical counted cash (PHP)", MoneyInput);
        Button("Close my shift with physical count", async () =>
        {
            var session = await _runtime!.CashierSessions.FindOpenSessionByUserIdAsync(_cashier.CurrentOperator!.UserId);
            if (session.IsFailure || session.Value is null) { Message("No open drawer was found for this operator."); return; }
            await DispatchAsync(Intent("ShiftClose",
                shiftClose: new(Guid.NewGuid(), session.Value.Id, Cents(counted.Text), DateTimeOffset.UtcNow)));
        });
        Label("Published menu", true);
        foreach (var item in _menu.Items.OrderBy(x => x.DisplayOrder))
            if (item.IsTemporarilyUnavailable) Label(item.Name + " · unavailable");
            else Button(item.Name + " · PHP " + Money(item.SellingPriceCents), () => { ItemDialog(item); return Task.CompletedTask; });
        Label(_order is null ? $"New {_serviceMode} order" : $"Current order ({_serviceMode}) · " + _order.Order.State, true);
        EditText serviceRef = Input("Table / Buzzer / Order Ref");
        serviceRef.Text = _serviceReference;
        if (_order is null || _order.Order.State == CafeOrderStates.Open)
        {
            string activeMode = _serviceMode;
            Button($"Service mode: {activeMode} (tap for {(activeMode == CafeOrderServiceModes.DineIn ? CafeOrderServiceModes.Takeout : CafeOrderServiceModes.DineIn)})",
                () =>
                {
                    _serviceReference = serviceRef.Text?.Trim() ?? string.Empty;
                    _serviceMode = activeMode == CafeOrderServiceModes.DineIn ? CafeOrderServiceModes.Takeout : CafeOrderServiceModes.DineIn;
                    return CashierPageAsync();
                });
        }
        foreach (var line in _lines) Label($"{line.QuantityX1000 / 1000m:0.###} × {_menu.Items.FirstOrDefault(x => x.ProductId == line.ProductId)?.Name ?? "Saved item"}");
        if (_order is not null) Label("Confirmed total: PHP " + Money(_order.CurrentRevision.Lines.Sum(x => x.LineTotalCents)));
        if (_order is null || _order.Order.State == CafeOrderStates.Open)
            Button("Save order", () =>
            {
                _serviceReference = serviceRef.Text?.Trim() ?? string.Empty;
                return DispatchAsync(Intent("SaveOrder", draft: new(Guid.NewGuid(), _order?.Order.OrderId ?? _draftOrderId,
                    _order?.Order.OrderVersion ?? 0, DateTimeOffset.UtcNow, _serviceMode,
                    string.IsNullOrWhiteSpace(_serviceReference) ? null : _serviceReference, _lines.ToArray())));
            });
        if (_order?.Order.State == CafeOrderStates.Open) Button("Send to preparation", () => DispatchAsync(Intent("SubmitPreparation", action: OrderAction())));
        if (_order?.Order.State == CafeOrderStates.Submitted) Button("Mark preparing", () => DispatchAsync(Intent("PreparationStatus", action: OrderAction(), target: CafeOrderStates.Preparing)));
        if (_order?.Order.State == CafeOrderStates.Preparing) Button("Mark ready", () => DispatchAsync(Intent("PreparationStatus", action: OrderAction(), target: CafeOrderStates.Ready)));
        if (_order?.Order.State == CafeOrderStates.Ready) Button("Checkout · Cash / Accounts Receivable", CheckoutDialogAsync);
        if (_order is not null && (_order.Order.State == CafeOrderStates.Submitted || _order.Order.State == CafeOrderStates.Preparing))
            Button("Print kitchen ticket", () => PrintKitchenTicketAsync(_order));
        Button("Start another order", async () => { _order = null; _lines.Clear(); _draftOrderId = Guid.NewGuid(); _serviceMode = CafeOrderServiceModes.Takeout; _serviceReference = string.Empty; await CashierPageAsync(); });
        if (_lastSaleId is not null)
        {
            Label($"Last sale · receipt {_lastReceiptNumber}", true);
            if (!string.IsNullOrWhiteSpace(_lastPrintStatus)) Label("Print status: " + _lastPrintStatus);
            Button(_lastPrintJobId is null ? "Print customer receipt" : "Retry customer receipt print",
                () => PrintReceiptAsync(_lastSaleId.Value));
        }
        Label("Saved orders", true);
        var orders = await _cashier.OrdersAsync();
        if (orders.IsFailure) Label(orders.ErrorDetail.Message);
        else foreach (var order in orders.Value.Where(x => !CafeOrderStates.IsTerminal(x.State)).OrderBy(x => x.CreatedAtUtc))
            Button($"{order.ServiceReference ?? order.ServiceMode} · {order.State} · {order.CreatedAtUtc.ToLocalTime():g}", async () =>
            { var found = await _cashier.FindOrderAsync(order.OrderId); if (found.IsFailure) { Message(found.ErrorDetail.Message); return; } Load(found.Value); await CashierPageAsync(); });
        Label("Active deliveries", true);
        var deliveries = await _cashier.ListDeliveriesAsync();
        if (deliveries.IsFailure) Label(deliveries.ErrorDetail.Message);
        else foreach (var delivery in deliveries.Value.Where(x => !CafeDeliveryStatuses.IsTerminal(x.Status)).OrderBy(x => x.CreatedAtUtc))
        {
            string nextStatus = delivery.Status == CafeDeliveryStatuses.Pending ? CafeDeliveryStatuses.Dispatched : CafeDeliveryStatuses.Delivered;
            Button($"Delivery {delivery.DeliveryId.ToString("N")[..6]} · {delivery.Status} → {nextStatus}", async () =>
            {
                var advanced = await _cashier.AdvanceDeliveryAsync(new(Guid.NewGuid(), delivery.DeliveryId, delivery.Version,
                    DateTimeOffset.UtcNow, nextStatus, null));
                Message(advanced.IsSuccess ? $"Delivery updated to {advanced.Value.Status}." : advanced.ErrorDetail.Message);
                await CashierPageAsync();
            });
        }
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
        string? target = null, MobileCheckoutIntent? checkout = null, MobileShiftOpenIntent? shiftOpen = null,
        MobileShiftCloseIntent? shiftClose = null, MobileRefundRequestIntent? refundRequest = null,
        MobileRefundResolutionIntent? refundResolution = null, MobileRefundCommitIntent? refundCommit = null) =>
        new(kind, draft, action, target, checkout, _cashier!.CurrentOperator!.UserId, shiftOpen, shiftClose,
            refundRequest, refundResolution, refundCommit);

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
                orderId = saved.Value.OrderId; break;
            case "SubmitPreparation" when intent.Action is not null:
                var submitted = await _cashier.SubmitPreparationAsync(intent.Action);
                if (submitted.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, submitted.ErrorDetail.Code, submitted.ErrorDetail.Message);
                    Message(submitted.ErrorDetail.Message + " Saved request retained."); return;
                }
                orderId = submitted.Value.OrderId; break;
            case "PreparationStatus" when intent.Action is not null && intent.TargetState is not null:
                var prepared = await _cashier.SetPreparationStatusAsync(intent.Action, intent.TargetState);
                if (prepared.IsFailure)
                {
                    await _intents.RecordAttemptFailureAsync(intent.OperationId, prepared.ErrorDetail.Code, prepared.ErrorDetail.Message);
                    Message(prepared.ErrorDetail.Message + " Saved request retained."); return;
                }
                orderId = prepared.Value.OrderId; break;
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
        Page("Salryn Cafe · Audited Refunds (C5)"); Status();
        Button("Back to Cashier", CashierPageAsync);
        var pending = await _intents!.ReadAsync();
        if (pending is not null)
        {
            Label($"Pending action: {pending.Kind} · Op {pending.OperationId:D}", true);
            if (!string.IsNullOrWhiteSpace(pending.LastErrorMessage))
                Label($"Last rejection ({pending.AttemptCount} attempts): [{pending.LastErrorCode}] {pending.LastErrorMessage}");
            Button("Retry saved action", () => DispatchAsync(pending));
            EditText dismissReason = Input("Reason to dismiss rejected action (audit logged)");
            Button("Dismiss rejected action (audit logged)", async () =>
            {
                if (string.IsNullOrWhiteSpace(dismissReason.Text)) { Message("Enter a reason before dismissing a rejected action."); return; }
                await _intents.ResolveRejectedAsync(pending.OperationId, _cashier!.CurrentOperator!.UserId, dismissReason.Text!, DateTimeOffset.UtcNow);
                Message($"Rejected action {pending.OperationId:D} dismissed and recorded in resolution history.");
                await RefundsPageAsync();
            });
        }

        Label("Refundable Completed Sales", true);
        var sales = await _cashier!.ListRefundableSalesAsync();
        if (sales.IsFailure)
        {
            Label(sales.ErrorDetail.Message);
        }
        else if (sales.Value.Count == 0)
        {
            Label("No refundable completed sales found.");
        }
        else
        {
            foreach (var sale in sales.Value)
            {
                Button($"{sale.ReceiptNumber} · PHP {Money(sale.TotalCents)} (Cash {Money(sale.CashNetCents)} / AR {Money(sale.ArCents)})",
                    () => { RequestRefundDialog(sale); return Task.CompletedTask; });
            }
        }

        Label("Existing Refund Requests", true);
        var refunds = await _cashier.ListRefundsAsync();
        if (refunds.IsFailure) { Label(refunds.ErrorDetail.Message); return; }
        if (refunds.Value.Count == 0) Label("No refund requests recorded.");
        foreach (var refund in refunds.Value)
        {
            Label($"{refund.ReceiptNumber ?? refund.SaleId.ToString("N")[..8]} · {refund.State} · PHP {(refund.TotalRefundCents is { } t ? Money(t) : "0.00")} · {refund.Reason}");
            if (refund.State == "Requested")
            {
                Button($"Approve refund {refund.ReceiptNumber}", () => DispatchAsync(Intent("RefundResolution",
                    refundResolution: new(Guid.NewGuid(), refund.RefundId, refund.SaleId, refund.Version, DateTimeOffset.UtcNow, true, "Approved on Android POS"))));
                Button($"Reject refund {refund.ReceiptNumber}", () => DispatchAsync(Intent("RefundResolution",
                    refundResolution: new(Guid.NewGuid(), refund.RefundId, refund.SaleId, refund.Version, DateTimeOffset.UtcNow, false, "Rejected on Android POS"))));
            }
            else if (refund.State == "Approved")
            {
                Button($"Commit refund {refund.ReceiptNumber} (Cash / AR)", () => { CommitRefundDialog(refund); return Task.CompletedTask; });
            }
        }
    }

    private void RequestRefundDialog(MobileRefundableSaleSummary sale)
    {
        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
        Spinner itemSpinner = new(this) { ContentDescription = "Sale line" };
        itemSpinner.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            sale.Items.Select(i => $"{i.ProductName} (Rem {i.RemainingQuantityX1000 / 1000m:0.###})").ToArray());
        Spinner dispSpinner = new(this) { ContentDescription = "Stock disposition" };
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
                await DispatchAsync(Intent("RefundRequest", refundRequest: new(reqOpId, refundId, sale.SaleId, 0, DateTimeOffset.UtcNow,
                    cleanReason, [new(lineId, item.SaleItemId, checked((long)(qty * 1000)), disposition, null)])));
            }))!.Show();
    }

    private void CommitRefundDialog(MobileRefundSummary refund)
    {
        LinearLayout form = new(this) { Orientation = Orientation.Vertical };
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
                await DispatchAsync(Intent("RefundCommit", refundCommit: new(commitOpId, refund.RefundId, refund.SaleId, refund.Version,
                    session.Value.Id, DateTimeOffset.UtcNow, settlements)));
            }))!.Show();
    }

    private EditText InputText(LinearLayout parent, string hint, InputTypes type, string initialValue)
    {
        parent.AddView(new TextView(this) { Text = hint, TextSize = 15 });
        EditText edit = new(this) { Hint = hint, InputType = type, ContentDescription = hint, Text = initialValue };
        edit.SetTextColor(Espresso); edit.SetMinHeight(Dp(48)); parent.AddView(edit);
        return edit;
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
        catch (InvalidOperationException error) { Message(error.Message); }
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
