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

// Kirch: Sales History supports filter, paging, receipt preview, and void workflow.
// Kirch UI map: date/status/receipt filters drive the grid; View Receipt opens ReceiptPreviewForm; Void Sale keeps the A3 workflow.

public sealed class SalesHistoryForm : Form
{
    private static readonly int[] AllowedPageSizes = { 25, 50, 100, 200 };

    private readonly SalrynAppServices _services;
    private readonly CurrentAppSession? _session;

    private readonly DateTimePicker _fromDatePicker = new();
    private readonly DateTimePicker _toDatePicker = new();
    private readonly ComboBox _statusFilterComboBox = new();
    private readonly TextBox _receiptSearchTextBox = new();
    private readonly ComboBox _pageSizeComboBox = new();
    private readonly Button _applyButton = new();
    private readonly Button _refreshButton = new();
    private readonly Button _previousPageButton = new();
    private readonly Button _nextPageButton = new();
    private readonly Button _viewReceiptButton = new();
    private readonly Button _voidSaleButton = new();
    private readonly DataGridView _salesGrid = J20MvpUi.Grid();
    private readonly Label _statusLabel = new();
    private readonly ToolTip _toolTip = J20MvpUi.CreateToolTip();

    private int _pageNumber = 1;
    private PagedResult<SaleHistoryItemReadModel>? _salesPage;

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
        MinimumSize = new Size(1120, 720);
        Size = new Size(1240, 780);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadSalesPageAsync(resetPage: true).ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 5);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Sales History",
            "Filter, page, and review completed or voided sales. Receipt preview and voiding stay on the selected row."), 0, 0);

        root.Controls.Add(BuildFilterPanel(), 0, 1);
        root.Controls.Add(BuildActionPagingPanel(), 0, 2);

        Panel gridCard = J20MvpUi.Card();
        _salesGrid.Dock = DockStyle.Fill;
        gridCard.Controls.Add(_salesGrid);
        root.Controls.Add(gridCard, 0, 3);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        root.Controls.Add(_statusLabel, 0, 4);
    }

    private Control BuildFilterPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 9,
            RowCount = 1,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));

        ConfigureDatePicker(_fromDatePicker);
        ConfigureDatePicker(_toDatePicker);

        _statusFilterComboBox.Dock = DockStyle.Fill;
        _statusFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusFilterComboBox.Items.AddRange(new object[] { "All", "Completed", "Voided" });
        _statusFilterComboBox.SelectedIndex = 0;

        _receiptSearchTextBox.Dock = DockStyle.Fill;
        _receiptSearchTextBox.Font = new Font("Segoe UI", 10F);
        _receiptSearchTextBox.PlaceholderText = "Search receipt number...";

        _pageSizeComboBox.Dock = DockStyle.Fill;
        _pageSizeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _pageSizeComboBox.Items.AddRange(AllowedPageSizes.Select(static size => (object)size).ToArray());
        _pageSizeComboBox.SelectedItem = 50;

        J20MvpUi.ConfigurePrimaryButton(_applyButton, "Apply");

        _toolTip.SetToolTip(_fromDatePicker, "Optional start date. Tick the box to enable this filter.");
        _toolTip.SetToolTip(_toDatePicker, "Optional end date. Tick the box to enable this filter.");
        _toolTip.SetToolTip(_statusFilterComboBox, "Filter by sale status.");
        _toolTip.SetToolTip(_receiptSearchTextBox, "Type a receipt number, then click Apply. Leave blank to list sales.");
        _toolTip.SetToolTip(_pageSizeComboBox, "How many rows to show per page.");
        _toolTip.SetToolTip(_applyButton, "Apply filters and reload from page 1.");

        panel.Controls.Add(CreateFieldLabel("From"), 0, 0);
        panel.Controls.Add(_fromDatePicker, 1, 0);
        panel.Controls.Add(CreateFieldLabel("To"), 2, 0);
        panel.Controls.Add(_toDatePicker, 3, 0);
        panel.Controls.Add(CreateFieldLabel("Status"), 4, 0);
        panel.Controls.Add(_statusFilterComboBox, 5, 0);
        panel.Controls.Add(_receiptSearchTextBox, 6, 0);
        panel.Controls.Add(_pageSizeComboBox, 7, 0);
        panel.Controls.Add(_applyButton, 8, 0);

        return panel;
    }

    private Control BuildActionPagingPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = J20MvpUi.Colors.AppBackground,
            Padding = new Padding(0, 8, 0, 0)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        J20MvpUi.ConfigureSecondaryButton(_viewReceiptButton, "View Receipt");
        J20MvpUi.ConfigureSecondaryButton(_voidSaleButton, "Void Sale");

        _toolTip.SetToolTip(_viewReceiptButton, "Open the selected sale's receipt preview.");
        _toolTip.SetToolTip(_voidSaleButton, "Void the selected completed sale (requires permission and a reason).");

        actions.Controls.Add(_viewReceiptButton);
        actions.Controls.Add(_voidSaleButton);

        FlowLayoutPanel paging = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        J20MvpUi.ConfigureSecondaryButton(_nextPageButton, "Next Page");
        J20MvpUi.ConfigureSecondaryButton(_previousPageButton, "Previous Page");
        J20MvpUi.ConfigureSecondaryButton(_refreshButton, "Refresh");

        _toolTip.SetToolTip(_nextPageButton, "Go to the next page of sales.");
        _toolTip.SetToolTip(_previousPageButton, "Go to the previous page of sales.");
        _toolTip.SetToolTip(_refreshButton, "Reload the current page with the current filters.");

        paging.Controls.Add(_nextPageButton);
        paging.Controls.Add(_previousPageButton);
        paging.Controls.Add(_refreshButton);

        panel.Controls.Add(actions, 0, 0);
        panel.Controls.Add(paging, 1, 0);

        return panel;
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = J20MvpUi.Colors.TextStrong
        };
    }

    private static void ConfigureDatePicker(DateTimePicker picker)
    {
        picker.Dock = DockStyle.Fill;
        picker.Format = DateTimePickerFormat.Short;
        picker.ShowCheckBox = true;
        picker.Checked = false;
        picker.Value = DateTime.Today;
    }

    private void WireEvents()
    {
        _applyButton.Click += async (_, _) => await LoadSalesPageAsync(resetPage: true).ConfigureAwait(true);
        _refreshButton.Click += async (_, _) => await LoadSalesPageAsync(resetPage: false).ConfigureAwait(true);
        _previousPageButton.Click += async (_, _) => await ChangePageAsync(-1).ConfigureAwait(true);
        _nextPageButton.Click += async (_, _) => await ChangePageAsync(1).ConfigureAwait(true);
        _pageSizeComboBox.SelectedIndexChanged += async (_, _) => await LoadSalesPageAsync(resetPage: true).ConfigureAwait(true);

        _receiptSearchTextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            await LoadSalesPageAsync(resetPage: true).ConfigureAwait(true);
        };

        _viewReceiptButton.Click += async (_, _) => await OpenSelectedReceiptAsync().ConfigureAwait(true);
        _voidSaleButton.Click += async (_, _) => await VoidSelectedSaleAsync().ConfigureAwait(true);

        _salesGrid.SelectionChanged += (_, _) => UpdateActionButtons();
    }

    private async Task ChangePageAsync(int delta)
    {
        _pageNumber = Math.Max(1, _pageNumber + delta);
        await LoadSalesPageAsync(resetPage: false).ConfigureAwait(true);
    }

    private async Task LoadSalesPageAsync(bool resetPage)
    {
        if (resetPage)
        {
            _pageNumber = 1;
        }

        SetBusy("Loading sales...");

        try
        {
            SaleHistoryFilter filter = BuildFilter(_pageNumber);
            var result = await _services.SaleReadRepository.SearchSalesAsync(filter).ConfigureAwait(true);

            if (result.IsFailure)
            {
                _salesGrid.DataSource = Array.Empty<SaleHistoryRow>();
                _salesPage = null;
                SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
                return;
            }

            _salesPage = result.Value;
            _pageNumber = result.Value.PageNumber;

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
            SetStatusForPage();
        }
        finally
        {
            SetReady();
        }
    }

    private SaleHistoryFilter BuildFilter(int pageNumber)
    {
        string? receiptNo = string.IsNullOrWhiteSpace(_receiptSearchTextBox.Text)
            ? null
            : _receiptSearchTextBox.Text.Trim();

        return new SaleHistoryFilter(
            From: GetFromDate(),
            To: GetToDate(),
            CashierUserId: null,
            Status: GetSelectedStatus(),
            ReceiptNo: receiptNo,
            Page: new PageRequest(pageNumber, GetSelectedPageSize()));
    }

    private int GetSelectedPageSize()
    {
        if (_pageSizeComboBox.SelectedItem is int pageSize &&
            AllowedPageSizes.Contains(pageSize))
        {
            return pageSize;
        }

        return 50;
    }

    private SaleStatus? GetSelectedStatus()
    {
        return _statusFilterComboBox.SelectedItem switch
        {
            "Completed" => SaleStatus.Completed,
            "Voided" => SaleStatus.Voided,
            _ => null
        };
    }

    private DateTimeOffset? GetFromDate()
    {
        if (!_fromDatePicker.Checked)
        {
            return null;
        }

        DateTime localDate = _fromDatePicker.Value.Date;
        return new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
    }

    private DateTimeOffset? GetToDate()
    {
        if (!_toDatePicker.Checked)
        {
            return null;
        }

        DateTime localEnd = _toDatePicker.Value.Date.AddDays(1).AddTicks(-1);
        return new DateTimeOffset(localEnd, TimeZoneInfo.Local.GetUtcOffset(localEnd));
    }

    private void SetStatusForPage()
    {
        if (_salesPage is null)
        {
            return;
        }

        if (_salesPage.Items.Count == 0)
        {
            _statusLabel.Text = J20MvpUi.StateText.NothingToShow + " No sales match your filters.";
            _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
            return;
        }

        _statusLabel.Text =
            $"Sales page {_salesPage.PageNumber} of {Math.Max(1, _salesPage.TotalPages)}; " +
            $"loaded {_salesPage.Items.Count} of {_salesPage.TotalCount} row(s).";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
    }

    private void SetBusy(string message)
    {
        _applyButton.Enabled = false;
        _refreshButton.Enabled = false;
        _previousPageButton.Enabled = false;
        _nextPageButton.Enabled = false;
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
    }

    private void SetReady()
    {
        _applyButton.Enabled = true;
        _refreshButton.Enabled = true;
        UpdatePagingButtons();
    }

    private void UpdatePagingButtons()
    {
        _previousPageButton.Enabled = _salesPage?.HasPreviousPage == true;
        _nextPageButton.Enabled = _salesPage?.HasNextPage == true;
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

        await LoadSalesPageAsync(resetPage: false).ConfigureAwait(true);

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
        UpdatePagingButtons();
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
