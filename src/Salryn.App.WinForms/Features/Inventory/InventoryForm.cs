using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;

namespace Salryn.App.WinForms.Features.Inventory;

// Kirch file guide: inventory visibility screen.
// UI location: opened from Shell > Inventory.
// Frontend note: grids are for low stock and movement history; stock write actions should use AppServices/presenter flow.

// Kirch: Minimal inventory skeleton for June 20 MVP.
// This screen proves low-stock and movement history visibility before deeper stock workflows are polished.
public sealed class InventoryForm : Form
{
    private readonly SalrynAppServices _services;
    private readonly CurrentAppSession? _session;

    // Kirch UI map: inventory grids.
    // UI side: lowStockGrid is the attention list; movementGrid is the audit/history list.
    // Frontend safe zone: columns, row height, empty states, and split layout.
    // Do not change: reads must stay through InventoryReadRepository via AppServices.
    private readonly DataGridView _lowStockGrid = J20MvpUi.Grid();
    private readonly DataGridView _movementGrid = J20MvpUi.Grid();
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _adjustStockButton = new();

    public InventoryForm(SalrynAppServices services)
        : this(services, session: null)
    {
    }

    public InventoryForm(
        SalrynAppServices services,
        CurrentAppSession? session)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _session = session;

        Text = "SALRYN Inventory";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1080, 680);
        Size = new Size(1180, 740);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
        UpdateActionButtons();
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Inventory",
            "Monitor low-stock products, movement history, and controlled stock adjustments."), 0, 0);

        root.Controls.Add(BuildGridCard("Low Stock", _lowStockGrid), 0, 1);
        root.Controls.Add(BuildGridCard("Stock Movements", _movementGrid), 0, 2);

        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        J20MvpUi.ConfigurePrimaryButton(_refreshButton, "Refresh");
        J20MvpUi.ConfigureSecondaryButton(_adjustStockButton, "Adjust Stock");
        footer.Controls.Add(_refreshButton);
        footer.Controls.Add(_adjustStockButton);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        footer.Controls.Add(_statusLabel);

        root.Controls.Add(footer, 0, 3);
    }

    private static Panel BuildGridCard(string title, DataGridView grid)
    {
        Panel card = J20MvpUi.Card();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Label label = new()
        {
            Dock = DockStyle.Fill,
            Text = title,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = J20MvpUi.Colors.TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        };

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(grid, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await LoadAsync().ConfigureAwait(true);

        _adjustStockButton.Click += async (_, _) =>
        {
            if (_session is null || !_session.IsActive)
            {
                SetError("Inventory adjustment requires an active user session.");
                return;
            }

            if (!_session.Can(SalrynPermissions.InventoryAdjust))
            {
                SetError("Current user does not have permission to adjust inventory.");
                return;
            }

            using InventoryAdjustmentForm adjustmentForm = new(
                _services.ProductReadRepository,
                _services.InventoryWriteRepository,
                _session.UserId);

            DialogResult result = adjustmentForm.ShowDialog(this);

            if (result != DialogResult.OK)
            {
                return;
            }

            await LoadAsync().ConfigureAwait(true);
            _statusLabel.Text = "Inventory adjustment saved.";
            _statusLabel.ForeColor = J20MvpUi.Colors.Success;
        };
    }

    private async Task LoadAsync()
    {
        _statusLabel.Text = "Loading inventory...";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;

        var lowStockResult = await _services.InventoryReadRepository.GetLowStockProductsAsync().ConfigureAwait(true);
        var movementResult = await _services.InventoryReadRepository.SearchStockMovementsAsync(
            new StockMovementFilter(
                ProductId: null,
                MovementType: null,
                From: null,
                To: null,
                Page: PageRequest.FirstPage(100))).ConfigureAwait(true);

        if (lowStockResult.IsSuccess)
        {
            _lowStockGrid.DataSource = lowStockResult.Value
                .Select(item => new LowStockRow(
                    Barcode: item.Barcode ?? "--",
                    Product: item.ProductName,
                    Unit: item.UnitName,
                    Stock: item.StockOnHand.ToString(),
                    Reorder: item.ReorderLevel.ToString()))
                .ToList();
        }

        if (movementResult.IsSuccess)
        {
            _movementGrid.DataSource = movementResult.Value.Items
                .Select(item => new MovementRow(
                    Date: item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    Product: item.ProductName,
                    Type: item.MovementType.ToString(),
                    Quantity: item.QuantityChangeUnits.ToString("0.###"),
                    Reason: item.Reason ?? "--",
                    User: item.UserDisplayName))
                .ToList();
        }

        if (lowStockResult.IsFailure || movementResult.IsFailure)
        {
            _statusLabel.Text = "Inventory loaded with repository warnings.";
            _statusLabel.ForeColor = J20MvpUi.Colors.Warning;
            return;
        }

        _statusLabel.Text = "Inventory ready.";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        _adjustStockButton.Enabled = _session is not null &&
            _session.IsActive &&
            _session.Can(SalrynPermissions.InventoryAdjust);
    }

    private void SetError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
    }

    private sealed record LowStockRow(
        string Barcode,
        string Product,
        string Unit,
        string Stock,
        string Reorder);

    private sealed record MovementRow(
        string Date,
        string Product,
        string Type,
        string Quantity,
        string Reason,
        string User);
}
