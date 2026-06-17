using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Sales;

// Kirch: Minimal Sales History screen for June 20 MVP.
// Keep the grid simple; deeper filtering, voiding, and reprint policies belong in later phases.
public sealed class SalesHistoryForm : Form
{
    private readonly SalrynAppServices _services;

    private readonly TextBox _receiptSearchTextBox = new();
    private readonly Button _refreshButton = new();
    private readonly Button _viewReceiptButton = new();
    private readonly DataGridView _salesGrid = J20MvpUi.Grid();
    private readonly Label _statusLabel = new();

    public SalesHistoryForm(SalrynAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

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
            ColumnCount = 4,
            RowCount = 1,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10F));

        _receiptSearchTextBox.Dock = DockStyle.Fill;
        _receiptSearchTextBox.Font = new Font("Segoe UI", 10.5F);
        _receiptSearchTextBox.PlaceholderText = "Search receipt number...";

        J20MvpUi.ConfigurePrimaryButton(_refreshButton, "Search");
        J20MvpUi.ConfigureSecondaryButton(_viewReceiptButton, "View Receipt");

        toolbar.Controls.Add(_receiptSearchTextBox, 0, 0);
        toolbar.Controls.Add(_refreshButton, 1, 0);
        toolbar.Controls.Add(_viewReceiptButton, 2, 0);

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

        _salesGrid.SelectionChanged += (_, _) =>
        {
            _viewReceiptButton.Enabled = GetSelectedSaleId().HasValue;
        };
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
                Status: item.Status.ToString()))
            .ToList();

        if (_salesGrid.Columns["SaleId"] is not null)
        {
            _salesGrid.Columns["SaleId"]!.Visible = false;
        }

        _viewReceiptButton.Enabled = GetSelectedSaleId().HasValue;
        _statusLabel.Text = $"Loaded {result.Value.Items.Count} sale rows. Total matches: {result.Value.TotalCount}.";
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

    private Guid? GetSelectedSaleId()
    {
        return _salesGrid.CurrentRow?.DataBoundItem is SaleHistoryRow row
            ? row.SaleId
            : null;
    }

    private sealed record SaleHistoryRow(
        Guid SaleId,
        string ReceiptNo,
        string Date,
        string Cashier,
        string Total,
        string Paid,
        string Change,
        string Status);
}
