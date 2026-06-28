using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.Features.Sales;

// Kirch file guide: completed sales and receipt lookup screen.
// UI location: opened from Shell > Sales History.
// Frontend note: restyle search/grid/detail buttons here; receipt data must come through SaleReadRepository via AppServices.

// Kirch: Minimal Sales History screen for June 20 MVP.
// Keep the grid simple; deeper filtering, voiding, and reprint policies belong in later phases.
public sealed class SalesHistoryForm : Form
{
    private readonly SalrynAppServices _services;
    private readonly CurrentAppSession? _session;

    // Kirch UI map: sales history controls.
    // UI side: receipt search input filters the grid; View Receipt opens the receipt preview modal.
    // Frontend safe zone: search placement, grid polish, empty/loading states, and action button styling.
    // Do not change: sale detail lookup must stay through SaleReadRepository via AppServices.
    private readonly TextBox _receiptSearchTextBox = new();
    private readonly Button _refreshButton = new();
    private readonly Button _viewReceiptButton = new();
    private readonly Button _voidSaleButton = new();
    private readonly DataGridView _salesGrid = J20MvpUi.Grid();
    private readonly Label _statusLabel = new();

    // Kirch B7: hover guidance for the sales history actions.
    private readonly ToolTip _toolTip = J20MvpUi.CreateToolTip();

    public SalesHistoryForm(SalrynAppServices services)
        : this(services, session: null)
    {
    }

    public SalesHistoryForm(
        SalrynAppServices services,
        CurrentAppSession? session)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _session = session;

        Text = "SALRYN Sales History";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1040, 660);
        Size = new Size(1160, 720);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadSalesAsync().ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 4);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Sales History",
            "Search receipts and review completed sales."), 0, 0);

        TableLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10F));

        _receiptSearchTextBox.Dock = DockStyle.Fill;
        _receiptSearchTextBox.Font = new Font("Segoe UI", 10.5F);
        _receiptSearchTextBox.PlaceholderText = "Search receipt number...";

        J20MvpUi.ConfigurePrimaryButton(_refreshButton, "Search");
        J20MvpUi.ConfigureSecondaryButton(_viewReceiptButton, "View Receipt");
        J20MvpUi.ConfigureSecondaryButton(_voidSaleButton, "Void Sale");

        _toolTip.SetToolTip(_receiptSearchTextBox, "Type a receipt number, then press Enter or Search. Leave blank to list recent sales.");
        _toolTip.SetToolTip(_refreshButton, "Search receipts (or list recent sales when the box is empty).");
        _toolTip.SetToolTip(_viewReceiptButton, "Open the selected sale's receipt preview.");
        _toolTip.SetToolTip(_voidSaleButton, "Void the selected completed sale (requires permission and a reason).");

        toolbar.Controls.Add(_receiptSearchTextBox, 0, 0);
        toolbar.Controls.Add(_refreshButton, 1, 0);
        toolbar.Controls.Add(_viewReceiptButton, 2, 0);
        toolbar.Controls.Add(_voidSaleButton, 3, 0);

        root.Controls.Add(toolbar, 0, 1);

        Panel gridCard = J20MvpUi.Card();
        _salesGrid.Dock = DockStyle.Fill;
        gridCard.Controls.Add(_salesGrid);
        root.Controls.Add(gridCard, 0, 2);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        root.Controls.Add(_statusLabel, 0, 3);
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await LoadSalesAsync().ConfigureAwait(true);

        _receiptSearchTextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            await LoadSalesAsync().ConfigureAwait(true);
        };

        _viewReceiptButton.Click += async (_, _) => await OpenSelectedReceiptAsync().ConfigureAwait(true);
        _voidSaleButton.Click += async (_, _) => await VoidSelectedSaleAsync().ConfigureAwait(true);

        _salesGrid.SelectionChanged += (_, _) => UpdateActionButtons();
    }

    private async Task LoadSalesAsync()
    {
        _statusLabel.Text = "Loading sales...";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;

        string? receiptNo = string.IsNullOrWhiteSpace(_receiptSearchTextBox.Text)
            ? null
            : _receiptSearchTextBox.Text.Trim();

        var result = await _services.SaleReadRepository.SearchSalesAsync(
            new SaleHistoryFilter(
                From: null,
                To: null,
                CashierUserId: null,
                Status: null,
                ReceiptNo: receiptNo,
                Page: PageRequest.FirstPage(100))).ConfigureAwait(true);

        if (result.IsFailure)
        {
            _salesGrid.DataSource = Array.Empty<SaleHistoryRow>();
            _statusLabel.Text = $"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}";
            _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
            return;
        }

        _salesGrid.DataSource = result.Value.Items
            .Select(item => new SaleHistoryRow(
                SaleId: item.Id,
                ReceiptNo: item.ReceiptNo,
                Date: item.SaleDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                Cashier: item.CashierDisplayName,
                Total: item.Total.ToString(),
                Paid: item.AmountPaid.ToString(),
                Change: item.Change.ToString(),
                Status: item.Status.ToString(),
                VoidReason: item.VoidReason ?? string.Empty))
            .ToList();

        if (_salesGrid.Columns["SaleId"] is not null)
        {
            _salesGrid.Columns["SaleId"]!.Visible = false;
        }

        UpdateActionButtons();
        _statusLabel.Text = result.Value.Items.Count == 0
            ? J20MvpUi.StateText.NothingToShow + " No sales match your search."
            : $"Loaded {result.Value.Items.Count} sale rows. Total matches: {result.Value.TotalCount}.";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
    }

    private async Task OpenSelectedReceiptAsync()
    {
        Guid? saleId = GetSelectedSaleId();

        if (!saleId.HasValue)
        {
            return;
        }

        var saleResult = await _services.SaleReadRepository.FindSaleByIdAsync(saleId.Value).ConfigureAwait(true);

        if (saleResult.IsFailure || saleResult.Value is null)
        {
            _statusLabel.Text = saleResult.IsFailure
                ? $"{saleResult.ErrorDetail.Code}: {saleResult.ErrorDetail.Message}"
                : "Selected sale was not found.";
            _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
            return;
        }

        StoreSettingsReadModel? storeSettings = null;
        var settingsResult = await _services.StoreSettingsReadRepository.GetCurrentAsync().ConfigureAwait(true);

        if (settingsResult.IsSuccess)
        {
            storeSettings = settingsResult.Value;
        }

        using ReceiptPreviewForm receiptForm = new(saleResult.Value, storeSettings);
        receiptForm.ShowDialog(this);
    }

    private async Task VoidSelectedSaleAsync()
    {
        if (_session is null || !_session.IsActive)
        {
            J20MvpUi.ShowBlockedAction(
                this,
                "Void Unavailable",
                "Voiding a sale requires an active user session. Please sign in again.");
            return;
        }

        if (!_session.Can(SalrynPermissions.SalesVoid))
        {
            J20MvpUi.ShowBlockedAction(
                this,
                "Permission Required",
                "Your account does not have permission to void sales. Ask an administrator for access.");
            return;
        }

        SaleHistoryRow? selectedSale = GetSelectedSale();

        if (selectedSale is null)
        {
            J20MvpUi.ShowBlockedAction(this, "No Sale Selected", "Select a completed sale in the list before voiding.");
            return;
        }

        if (!string.Equals(selectedSale.Status, SaleStatus.Completed.ToString(), StringComparison.Ordinal))
        {
            SetError("Only completed sales can be voided.");
            return;
        }

        using SalesVoidReasonForm reasonForm = new(selectedSale.ReceiptNo);

        if (reasonForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string reason = reasonForm.VoidReason;

        if (string.IsNullOrWhiteSpace(reason))
        {
            SetError("Void reason is required.");
            return;
        }

        DialogResult confirmResult = MessageBox.Show(
            this,
            "Void selected sale " + selectedSale.ReceiptNo + "? Stock reversal will use recorded sale stock movements.",
            "Confirm Sale Void",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmResult != DialogResult.Yes)
        {
            return;
        }

        _voidSaleButton.Enabled = false;
        _statusLabel.Text = "Voiding sale...";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;

        var saleResult = await _services.SaleReadRepository
            .FindSaleByIdAsync(selectedSale.SaleId)
            .ConfigureAwait(true);

        if (saleResult.IsFailure || saleResult.Value is null)
        {
            SetError(saleResult.IsFailure
                ? $"{saleResult.ErrorDetail.Code}: {saleResult.ErrorDetail.Message}"
                : "Selected sale was not found.");
            UpdateActionButtons();
            return;
        }

        if (saleResult.Value.Status != SaleStatus.Completed)
        {
            SetError("Selected sale is no longer completed and cannot be voided.");
            UpdateActionButtons();
            return;
        }

        var reversalResult = await BuildReversalMovementsAsync(
            saleResult.Value,
            _session.UserId,
            reason)
            .ConfigureAwait(true);

        if (reversalResult.IsFailure)
        {
            SetError($"{reversalResult.ErrorDetail.Code}: {reversalResult.ErrorDetail.Message}");
            UpdateActionButtons();
            return;
        }

        Result voidResult = await _services.SaleWriteRepository
            .MarkVoidedAsync(
                saleResult.Value.Id,
                _session.UserId,
                reason,
                reversalResult.Value)
            .ConfigureAwait(true);

        if (voidResult.IsFailure)
        {
            SetError($"{voidResult.ErrorDetail.Code}: {voidResult.ErrorDetail.Message}");
            UpdateActionButtons();
            return;
        }

        await LoadSalesAsync().ConfigureAwait(true);

        _statusLabel.Text = "Voided sale " + selectedSale.ReceiptNo + ".";
        _statusLabel.ForeColor = J20MvpUi.Colors.Success;
    }

    private async Task<Result<IReadOnlyList<StockMovement>>> BuildReversalMovementsAsync(
        SaleDetailReadModel sale,
        Guid userId,
        string reason)
    {
        var stockMovementResult = await _services.InventoryReadRepository
            .SearchStockMovementsAsync(new StockMovementFilter(
                ProductId: null,
                MovementType: StockMovementType.Sale,
                From: null,
                To: null,
                Page: PageRequest.FirstPage(1000),
                ReferenceType: "SALE",
                ReferenceId: sale.Id.ToString("D")))
            .ConfigureAwait(true);

        if (stockMovementResult.IsFailure)
        {
            return Result<IReadOnlyList<StockMovement>>.Fail(stockMovementResult.ErrorDetail);
        }

        if (stockMovementResult.Value.TotalCount > stockMovementResult.Value.Items.Count)
        {
            return Result<IReadOnlyList<StockMovement>>.Fail(Error.Failure(
                "Sales.VoidStockMovementPageExceeded",
                "Selected sale has more stock movements than the void workflow can safely reverse."));
        }

        List<StockMovement> reversalMovements = new();

        foreach (StockMovementReadModel movement in stockMovementResult.Value.Items
            .OrderBy(static item => item.CreatedAt)
            .ThenBy(static item => item.Id))
        {
            long quantityX1000 = Math.Abs(movement.QuantityChangeX1000);

            Result<Quantity> quantity = Quantity.FromX1000(quantityX1000);

            if (quantity.IsFailure || quantity.Value == Quantity.Zero)
            {
                return Result<IReadOnlyList<StockMovement>>.Fail(Error.Validation(
                    "Sales.InvalidVoidStockMovementQuantity",
                    "Recorded sale stock movement quantity cannot be reversed."));
            }

            reversalMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                ProductId = movement.ProductId,
                MovementType = StockMovementType.VoidSale,
                QuantityChange = quantity.Value,
                CostPrice = movement.CostPrice,
                ReferenceType = "SALE",
                ReferenceId = sale.Id,
                Reason = reason.Trim(),
                UserId = userId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        return Result<IReadOnlyList<StockMovement>>.Success(reversalMovements);
    }

    private Guid? GetSelectedSaleId()
    {
        return GetSelectedSale()?.SaleId;
    }

    private SaleHistoryRow? GetSelectedSale()
    {
        return _salesGrid.CurrentRow?.DataBoundItem as SaleHistoryRow;
    }

    private void UpdateActionButtons()
    {
        SaleHistoryRow? selectedSale = GetSelectedSale();

        _viewReceiptButton.Enabled = selectedSale is not null;
        _voidSaleButton.Enabled = selectedSale is not null &&
            _session is not null &&
            _session.IsActive &&
            _session.Can(SalrynPermissions.SalesVoid) &&
            string.Equals(selectedSale.Status, SaleStatus.Completed.ToString(), StringComparison.Ordinal);
    }

    private void SetError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
    }

    private sealed record SaleHistoryRow(
        Guid SaleId,
        string ReceiptNo,
        string Date,
        string Cashier,
        string Total,
        string Paid,
        string Change,
        string Status,
        string VoidReason);
}
