using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Dashboard;

// Kirch file guide: dashboard overview screen.
// UI location: opened from Shell > Dashboard.
// Frontend note: safe to improve cards and spacing; keep data reads through AppServices.

// Kirch: This is the June 20 MVP dashboard skeleton.
// Keep it presentable and useful, but do not bypass AppServices or add LAN/transport logic here.
public sealed class DashboardForm : Form
{
    private readonly SalrynAppServices _services;

    private readonly Label _statusLabel = new();
    private readonly Label _todaySalesValueLabel = new();
    private readonly Label _transactionsValueLabel = new();
    private readonly Label _productsValueLabel = new();
    private readonly Label _lowStockValueLabel = new();
    private readonly Button _refreshButton = new();

    public DashboardForm(SalrynAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Text = "SALRYN Dashboard";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 560);
        Size = new Size(980, 620);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadAsync().ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 4);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Dashboard",
            "Sales, product, and inventory overview."), 0, 0);

        TableLayoutPanel cards = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        for (int i = 0; i < 4; i++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        }

        cards.Controls.Add(J20MvpUi.MetricCard("Today Sales", _todaySalesValueLabel, "Completed sales total"), 0, 0);
        cards.Controls.Add(J20MvpUi.MetricCard("Transactions", _transactionsValueLabel, "Completed today"), 1, 0);
        cards.Controls.Add(J20MvpUi.MetricCard("Products", _productsValueLabel, "Catalog records"), 2, 0);
        cards.Controls.Add(J20MvpUi.MetricCard("Low Stock", _lowStockValueLabel, "Needs attention"), 3, 0);

        root.Controls.Add(cards, 0, 1);

        Panel noteCard = J20MvpUi.Card();
        Label note = J20MvpUi.BodyText(
            "Track daily sales, transaction count, product records, and low-stock alerts from one screen." +
            "" +
            "");
        note.Dock = DockStyle.Fill;
        noteCard.Controls.Add(note);

        root.Controls.Add(noteCard, 0, 2);

        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        J20MvpUi.ConfigurePrimaryButton(_refreshButton, "Refresh");
        footer.Controls.Add(_refreshButton);
        root.Controls.Add(footer, 0, 3);

        _statusLabel.Dock = DockStyle.Bottom;
        _statusLabel.Height = 24;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        noteCard.Controls.Add(_statusLabel);
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        SetBusy("Loading dashboard...");

        DateTime today = DateTime.Today;
        DateTime tomorrow = today.AddDays(1);

        long todaySalesCents = 0;
        int todayTransactionCount = 0;
        int productCount = 0;
        int lowStockCount = 0;

        var salesResult = await _services.SaleReadRepository.SearchSalesAsync(
            new SaleHistoryFilter(
                From: new DateTimeOffset(today),
                To: new DateTimeOffset(tomorrow),
                CashierUserId: null,
                Status: null,
                ReceiptNo: null,
                Page: PageRequest.FirstPage(200))).ConfigureAwait(true);

        if (salesResult.IsSuccess)
        {
            todayTransactionCount = salesResult.Value.TotalCount;
            todaySalesCents = salesResult.Value.Items.Sum(item => item.Total.Cents);
        }

        var productsResult = await _services.ProductReadRepository.SearchAsync(
            new ProductSearchFilter(
                SearchText: null,
                CategoryId: null,
                BrandId: null,
                IsActive: null,
                IncludeInactive: true,
                Page: PageRequest.FirstPage(1))).ConfigureAwait(true);

        if (productsResult.IsSuccess)
        {
            productCount = productsResult.Value.TotalCount;
        }

        var lowStockResult = await _services.InventoryReadRepository.GetLowStockProductsAsync().ConfigureAwait(true);

        if (lowStockResult.IsSuccess)
        {
            lowStockCount = lowStockResult.Value.Count;
        }

        _todaySalesValueLabel.Text = MoneyText(todaySalesCents);
        _transactionsValueLabel.Text = todayTransactionCount.ToString("N0");
        _productsValueLabel.Text = productCount.ToString("N0");
        _lowStockValueLabel.Text = lowStockCount.ToString("N0");

        if (salesResult.IsFailure || productsResult.IsFailure || lowStockResult.IsFailure)
        {
            _statusLabel.Text = "Dashboard loaded with one or more repository warnings.";
            _statusLabel.ForeColor = J20MvpUi.Colors.Warning;
            return;
        }

        _statusLabel.Text = $"Dashboard refreshed {DateTime.Now:yyyy-MM-dd HH:mm}.";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
    }

    private void SetBusy(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
    }

    private static string MoneyText(long cents)
    {
        return "PHP " + (cents / 100m).ToString("N2");
    }
}
