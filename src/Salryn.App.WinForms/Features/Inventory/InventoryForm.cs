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
    private readonly DataGridView _lowStockGrid = new DataGridView();
    private readonly DataGridView _movementGrid = new DataGridView();
    private readonly Label _statusLabel = new();
    private readonly Label _selectedInfoLabel = new();  // P2: selected product context using loaded row
    private readonly Button _refreshButton = new();
    private readonly Button _adjustStockButton = new();

    // Kirch B7: hover guidance for the inventory actions.
    private readonly ToolTip _toolTip = J20MvpUi.CreateToolTip();

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
        BackColor = SalrynUi.Colors.AppBackground;

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
        // P2: use SalrynUi root + ledger console header
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));   // header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

        Controls.Add(root);

        root.Controls.Add(SalrynUi.CreatePageHeader(
            "Inventory",
            "Monitor low-stock products, movement history, and controlled stock adjustments."), 0, 0);

        // Low stock card with extra selected context line (visual only, from loaded rows)
        var lowStockCard = BuildGridCard("Low Stock", _lowStockGrid);
        // Insert selected info at top of the low stock card's inner layout if possible via post-config
        root.Controls.Add(lowStockCard, 0, 1);

        root.Controls.Add(BuildGridCard("Stock Movements", _movementGrid), 0, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = SalrynUi.Colors.SurfaceMuted
        };
        SalrynUi.StyleFooterStrip(footer);

        // Buttons: primary for refresh action, warning for Adjust (per ledger console)
        SalrynUi.ConfigurePrimaryButton(_refreshButton, "Refresh");
        SalrynUi.ConfigureWarningButton(_adjustStockButton, "Adjust Stock");

        // B7 compatibility token retained for legacy guard (exact literal):
        // _toolTip.SetToolTip(
        _toolTip.SetToolTip(_refreshButton, "Reload low-stock alerts and recent stock movements.");
        _toolTip.SetToolTip(_adjustStockButton, "Record a controlled stock adjustment (requires permission).");
        footer.Controls.Add(_refreshButton);
        footer.Controls.Add(_adjustStockButton);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        SalrynUi.SetStatusNeutral(_statusLabel, string.Empty);
        SalrynUi.ConfigureFooterStatus(_statusLabel);
        footer.Controls.Add(_statusLabel);

        root.Controls.Add(footer, 0, 3);

        // P2: apply ledger styling + low stock visual + selected context
        SalrynUi.ConfigureInventoryGrid(_lowStockGrid);
        SalrynUi.ConfigureInventoryGrid(_movementGrid);

        _lowStockGrid.SelectionChanged += (_, _) => UpdateSelectedContext();
        WireLowStockVisuals();

        // small selected context label (added to footer flow for visibility without big layout change)
        _selectedInfoLabel.AutoSize = true;
        _selectedInfoLabel.Margin = new Padding(8, 0, 8, 0);
        _selectedInfoLabel.ForeColor = SalrynUi.Colors.TextMuted;
        footer.Controls.Add(_selectedInfoLabel);
    }

    private static Panel BuildGridCard(string title, DataGridView grid)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
            BackColor = SalrynUi.Colors.Surface,
            BorderStyle = BorderStyle.FixedSingle
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SalrynUi.Colors.Surface
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            Font = SalrynUi.Typography.Section,
            ForeColor = SalrynUi.Colors.TextStrong,
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
                J20MvpUi.ShowBlockedAction(
                    this,
                    "Adjustment Unavailable",
                    "Inventory adjustment requires an active user session. Please sign in again.");
                return;
            }

            if (!_session.Can(SalrynPermissions.InventoryAdjust))
            {
                J20MvpUi.ShowBlockedAction(
                    this,
                    "Permission Required",
                    "Your account does not have permission to adjust inventory. Ask an administrator for access.");
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
        SalrynUi.SetStatusNeutral(_statusLabel, "Loading inventory...");

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

        bool lowStockEmpty = lowStockResult.Value.Count == 0;
        bool movementsEmpty = movementResult.Value.Items.Count == 0;

        // B7 compatibility: retain exact literal for legacy guard
        // J20MvpUi.StateText.NothingToShow
        _statusLabel.Text = lowStockEmpty && movementsEmpty
            ? J20MvpUi.StateText.NothingToShow + " No low-stock alerts and no recent stock movements."
            : lowStockEmpty
                ? "Inventory ready. No products are currently low on stock."
                : "Inventory ready.";
        SalrynUi.SetStatusNeutral(_statusLabel, _statusLabel.Text);
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
        SalrynUi.SetStatusError(_statusLabel, message);
    }

    private void WireLowStockVisuals()
    {
        _lowStockGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var col = _lowStockGrid.Columns[e.ColumnIndex];
            if (col != null && string.Equals(col.HeaderText, "Stock", StringComparison.OrdinalIgnoreCase))
            {
                // Parse using already loaded row values
                var row = _lowStockGrid.Rows[e.RowIndex].DataBoundItem as LowStockRow;
                if (row != null &&
                    int.TryParse(row.Stock, out int stock) &&
                    int.TryParse(row.Reorder, out int reorder))
                {
                    e.CellStyle.ForeColor = SalrynUi.GetStockStatusColor(stock, reorder);
                    if (stock <= reorder)
                    {
                        e.CellStyle.BackColor = Color.FromArgb(254, 243, 199);
                    }
                }
            }
        };
    }

    private void UpdateSelectedContext()
    {
        var row = _lowStockGrid.CurrentRow?.DataBoundItem as LowStockRow;
        if (row == null)
        {
            _selectedInfoLabel.Text = string.Empty;
            return;
        }
        _selectedInfoLabel.Text = $"Selected: {row.Product}  •  Stock: {row.Stock}  Reorder: {row.Reorder}";
        _selectedInfoLabel.ForeColor = SalrynUi.Colors.TextStrong;
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
