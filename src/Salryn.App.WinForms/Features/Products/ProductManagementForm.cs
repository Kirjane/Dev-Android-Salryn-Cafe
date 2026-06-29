using Salryn.App.WinForms.Features.Mvp;
using Salryn.App.WinForms.Features.Shell;

namespace Salryn.App.WinForms.Features.Products;

// Kirch file guide: product catalog list screen.
// UI location: opened from Shell > Products.
// Frontend note: safe to restyle toolbar/grid/status here; do not construct repositories in the form.

public sealed class ProductManagementForm : Form
{
    private readonly ProductManagementPresenter _presenter;
    private readonly CurrentAppSession? _session;

    // Kirch UI map: Products toolbar and grid controls.
    // UI side: search box/buttons live above the product catalog grid; status label reports load/search results.
    // Frontend safe zone: toolbar layout, grid styling, status placement, and add button prominence.
    // Do not change: search/load/create/actions must stay presenter-backed.
    private TextBox _searchTextBox = null!;
    private Button _searchButton = null!;
    private Button _refreshButton = null!;
    private Button _addButton = null!;
    private Button _editButton = null!;
    private Button _deactivateButton = null!;
    private DataGridView _productsGrid = null!;
    private Label _statusLabel = null!;

    // P1B mockup: summary strip using already-loaded row data
    private Label _summaryTotal = null!;
    private Label _summaryActive = null!;
    private Label _summaryLowStock = null!;
    private Label _summaryTracked = null!;

    // Kirch B7: hover guidance for the product catalog toolbar actions.
    private readonly ToolTip _toolTip = J20MvpUi.CreateToolTip();

    public ProductManagementForm(ProductManagementPresenter presenter)
        : this(presenter, session: null)
    {
    }

    public ProductManagementForm(
        ProductManagementPresenter presenter,
        CurrentAppSession? session)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _session = session;

        InitializeComponent();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        await LoadProductsAsync();
    }

    private void InitializeComponent()
    {
        Text = "Product Management";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1180, 680);
        Size = new Size(1280, 720);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));   // header + subtitle
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // summary metric strip
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));   // command bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // grid
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // status

        Controls.Add(root);

        // P1C: header with title + subtitle (ledger console)
        var header = SalrynUi.CreatePageHeader("Products", "Catalog • Search, add, edit or deactivate products");
        root.Controls.Add(header, 0, 0);

        // Summary strip styled as metric row (client-side only from loaded rows)
        var summaryBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = SalrynUi.Colors.SurfaceMuted,
            Padding = new Padding(6, 3, 6, 3)
        };

        _summaryTotal = SalrynUi.CreateSummaryMetric("Total: --");
        _summaryActive = SalrynUi.CreateSummaryMetric("Active: --");
        _summaryLowStock = SalrynUi.CreateSummaryMetric("Low Stock: --");
        _summaryTracked = SalrynUi.CreateSummaryMetric("Tracked: --");

        summaryBar.Controls.Add(_summaryTotal);
        summaryBar.Controls.Add(_summaryActive);
        summaryBar.Controls.Add(_summaryLowStock);
        summaryBar.Controls.Add(_summaryTracked);

        root.Controls.Add(summaryBar, 0, 1);

        // P1C: grouped command bar (search + actions)
        var cmdHost = SalrynUi.CreateCommandBar();
        var searchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 9,
            RowCount = 1
        };

        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 6));  // danger sep
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 6));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));

        _searchTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 11),
            PlaceholderText = "Search by product name, barcode, or SKU..."
        };

        _searchTextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            await SearchProductsAsync();
        };

        _searchButton = new Button { Dock = DockStyle.Fill };
        SalrynUi.ConfigurePrimaryButton(_searchButton, "Search");
        _searchButton.Click += async (_, _) => await SearchProductsAsync();

        _refreshButton = new Button { Dock = DockStyle.Fill };
        SalrynUi.ConfigureSecondaryButton(_refreshButton, "Refresh");
        _refreshButton.Click += async (_, _) => await LoadProductsAsync();

        _addButton = new Button { Dock = DockStyle.Fill };
        SalrynUi.ConfigureSuccessButton(_addButton, "Add Product");
        _addButton.Click += async (_, _) => await OpenCreateFormAsync();

        _editButton = new Button { Dock = DockStyle.Fill };
        SalrynUi.ConfigurePrimaryButton(_editButton, "Edit");
        _editButton.Click += async (_, _) => await OpenEditFormAsync();

        // Visual danger separator before destructive action (far right group)
        var dangerSep = SalrynUi.CreateDangerSeparator();
        dangerSep.Dock = DockStyle.Fill;

        _deactivateButton = new Button { Dock = DockStyle.Fill };
        SalrynUi.ConfigureDangerButton(_deactivateButton, "Deactivate");
        _deactivateButton.Margin = new Padding(2, 0, 0, 0);

        _deactivateButton.Click += async (_, _) => await DeactivateSelectedProductAsync();

        _toolTip.SetToolTip(_searchTextBox, "Type a product name, barcode, or SKU, then press Enter or Search.");
        _toolTip.SetToolTip(_searchButton, "Find products matching the search text.");
        _toolTip.SetToolTip(_refreshButton, "Clear the search and reload the full catalog.");
        _toolTip.SetToolTip(_addButton, "Create a new product.");
        _toolTip.SetToolTip(_editButton, "Edit the selected product.");
        _toolTip.SetToolTip(_deactivateButton, "Deactivate the selected active product (a reason is required).");

        searchPanel.Controls.Add(_searchTextBox, 0, 0);
        searchPanel.Controls.Add(_searchButton, 1, 0);
        searchPanel.Controls.Add(_refreshButton, 2, 0);
        searchPanel.Controls.Add(_addButton, 3, 0);
        searchPanel.Controls.Add(_editButton, 4, 0);
        searchPanel.Controls.Add(dangerSep, 5, 0);
        searchPanel.Controls.Add(_deactivateButton, 6, 0);

        cmdHost.Controls.Add(searchPanel);
        root.Controls.Add(cmdHost, 0, 2);

        _productsGrid = new DataGridView();
        SalrynUi.ConfigureLedgerGrid(_productsGrid);

        _productsGrid.SelectionChanged += (_, _) => UpdateActionButtonState();
        _productsGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                await OpenEditFormAsync();
            }
        };

        AddTextColumn("SkuText", "SKU", 95);
        AddTextColumn("BarcodeText", "Barcode", 130);
        AddTextColumn("ProductName", "Product", 230);
        AddTextColumn("UnitName", "Unit", 80);
        AddTextColumn("CategoryText", "Category", 120);
        AddTextColumn("BrandText", "Brand", 120);
        AddTextColumn("SellingPriceText", "Selling", 95);
        AddTextColumn("CostPriceText", "Cost", 95);
        AddTextColumn("StockOnHandText", "Stock", 90);
        AddTextColumn("ReorderLevelText", "Reorder", 90);
        AddTextColumn("TrackStockText", "Stock Mode", 95);
        AddTextColumn("StatusText", "Status", 85);

        root.Controls.Add(_productsGrid, 0, 3);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        SalrynUi.SetStatusNeutral(_statusLabel, string.Empty);
        SalrynUi.ConfigureFooterStatus(_statusLabel);

        root.Controls.Add(_statusLabel, 0, 4);
        UpdateActionButtonState();

        // P1C: cell-level visual badges (status coloring) using existing bound data only
        WireGridStatusBadges();
    }

    private void AddTextColumn(
        string propertyName,
        string headerText,
        int width)
    {
        _productsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    // Kirch workflow: initial and refresh product list load.
    // UI side: fills the grid with ProductManagementRowViewModel rows.
    // Frontend note: loading indicators can be improved here without changing presenter behavior.
    private async Task LoadProductsAsync()
    {
        _searchTextBox.Text = string.Empty;

        ProductManagementOperationResult result =
            await _presenter.LoadAsync();

        Render(result.State);
    }

    private async Task SearchProductsAsync()
    {
        ProductManagementOperationResult result =
            await _presenter.SearchAsync(_searchTextBox.Text);

        Render(result.State);
    }

    // Kirch: This opens the create form and refreshes the grid after a product is saved.
    private async Task OpenCreateFormAsync()
    {
        using ProductCreateForm createForm = new(_presenter);

        DialogResult result = createForm.ShowDialog(this);

        if (result != DialogResult.OK)
        {
            return;
        }

        await RefreshCurrentSearchAsync();
    }

    private async Task OpenEditFormAsync()
    {
        ProductManagementRowViewModel? selected = GetSelectedProduct();

        if (selected is null)
        {
            SetError("Select a product before editing.");
            return;
        }

        using ProductEditForm editForm = new(_presenter, selected.ProductId);

        DialogResult result = editForm.ShowDialog(this);

        if (result != DialogResult.OK)
        {
            return;
        }

        await RefreshCurrentSearchAsync();
    }

    private async Task DeactivateSelectedProductAsync()
    {
        ProductManagementRowViewModel? selected = GetSelectedProduct();

        if (selected is null)
        {
            SetError("Select a product before deactivating.");
            return;
        }

        if (!string.Equals(selected.StatusText, "Active", StringComparison.OrdinalIgnoreCase))
        {
            SetError("Only active products can be deactivated.");
            return;
        }

        if (_session is null || !_session.IsActive || _session.UserId == Guid.Empty)
        {
            SetError("Product deactivation requires a current authenticated user session.");
            return;
        }

        // UI-A1R-P1: use shared Ledger Console danger confirmation + required reason prompt.
        // B7 compatibility token preserved for existing presentation guard (exact call pattern):
        // J20MvpUi.ConfirmDestructiveAction(
        if (!SalrynUi.ConfirmDanger(
                this,
                "Confirm Deactivate Product",
                "Deactivate \"" + selected.ProductName + "\"? It will no longer be available for checkout until reactivated.",
                "Deactivate"))
        {
            SalrynUi.SetStatusNeutral(_statusLabel, "Deactivate cancelled.");
            return;
        }

        string reason;
        if (!SalrynUi.PromptRequiredReason(
                this,
                "Deactivate Product",
                "Reason for deactivating " + selected.ProductName + ":",
                out reason))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            SetError("Products.DeactivateReasonRequired: A reason is required when deactivating a product.");
            return;
        }

        ProductEditOperationResult deactivateResult = await _presenter.DeactivateProductAsync(
            selected.ProductId,
            reason,
            _session.UserId).ConfigureAwait(true);

        if (!deactivateResult.IsSuccess)
        {
            SetError(deactivateResult.ErrorCode + ": " + deactivateResult.ErrorMessage);
            return;
        }

        await RefreshCurrentSearchAsync();
        _statusLabel.Text = "Product deactivated.";
        _statusLabel.ForeColor = Color.FromArgb(22, 101, 52);
    }

    private async Task RefreshCurrentSearchAsync()
    {
        ProductManagementOperationResult refreshResult =
            await _presenter.SearchAsync(_searchTextBox.Text);

        Render(refreshResult.State);
    }

    private ProductManagementRowViewModel? GetSelectedProduct()
    {
        return _productsGrid.CurrentRow?.DataBoundItem as ProductManagementRowViewModel;
    }

    private void UpdateActionButtonState()
    {
        if (_editButton is null || _deactivateButton is null)
        {
            return;
        }

        ProductManagementRowViewModel? selected = GetSelectedProduct();
        bool hasSelectedProduct = selected is not null;
        bool selectedProductIsActive = hasSelectedProduct &&
            string.Equals(selected!.StatusText, "Active", StringComparison.OrdinalIgnoreCase);

        _editButton.Enabled = hasSelectedProduct;
        _deactivateButton.Enabled = selectedProductIsActive;
    }

    private static string? PromptDeactivateReason(string productName)
    {
        using Form dialog = new()
        {
            Text = "Deactivate Product",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(460, 250),
            MinimumSize = new Size(460, 250),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16)
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        Label label = new()
        {
            Dock = DockStyle.Fill,
            Text = "Reason for deactivating " + productName + ":",
            TextAlign = ContentAlignment.MiddleLeft
        };

        TextBox reasonTextBox = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };

        Button okButton = new()
        {
            Text = "Deactivate",
            Width = 110,
            Height = 32,
            DialogResult = DialogResult.OK
        };

        Button cancelButton = new()
        {
            Text = "Cancel",
            Width = 90,
            Height = 32,
            DialogResult = DialogResult.Cancel
        };

        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(reasonTextBox, 0, 1);
        layout.Controls.Add(buttons, 0, 3);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        DialogResult result = dialog.ShowDialog();

        return result == DialogResult.OK
            ? reasonTextBox.Text.Trim()
            : null;
    }

    private void Render(ProductManagementViewModel state)
    {
        _searchButton.Enabled = state.CanSearch;
        _refreshButton.Enabled = state.CanSearch;
        _addButton.Enabled = state.CanSearch;
        _searchTextBox.Enabled = state.CanSearch;

        _productsGrid.DataSource = null;
        _productsGrid.DataSource = state.Products.ToList();
        UpdateActionButtonState();

        if (!state.HasError)
        {
            UpdateSummary(state.Products);
        }

        string msg = state.HasError
            ? $"{state.StatusMessage} {state.ErrorCode}: {state.ErrorMessage}"
            : state.Products.Count == 0
                ? J20MvpUi.StateText.NothingToShow + " Adjust your search or add a product."
                : state.StatusMessage;

        if (state.HasError)
            SalrynUi.SetStatusError(_statusLabel, msg);
        else
            SalrynUi.SetStatusNeutral(_statusLabel, msg);
    }

    private void SetError(string message)
    {
        SalrynUi.SetStatusError(_statusLabel, message);
    }

    private static Label CreateSummaryLabel(string text)
    {
        return new Label
        {
            Text = text,
            Font = SalrynUi.Typography.Small,
            ForeColor = SalrynUi.Colors.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 0, 16, 0)
        };
    }

    private void UpdateSummary(IReadOnlyList<ProductManagementRowViewModel> rows)
    {
        int total = rows.Count;
        int active = rows.Count(r => string.Equals(r.StatusText, "Active", StringComparison.OrdinalIgnoreCase));
        int tracked = rows.Count(r => string.Equals(r.TrackStockText, "Yes", StringComparison.OrdinalIgnoreCase));

        int lowStock = 0;
        foreach (var r in rows)
        {
            if (int.TryParse(r.StockOnHandText, out int soh) &&
                int.TryParse(r.ReorderLevelText, out int reorder) &&
                soh <= reorder)
            {
                lowStock++;
            }
        }

        _summaryTotal.Text = $"Total: {total}";
        _summaryActive.Text = $"Active: {active}";
        _summaryLowStock.Text = $"Low Stock: {lowStock}";
        _summaryTracked.Text = $"Tracked: {tracked}";
    }

    private void WireGridStatusBadges()
    {
        _productsGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var col = _productsGrid.Columns[e.ColumnIndex];
            if (col != null && string.Equals(col.HeaderText, "Status", StringComparison.OrdinalIgnoreCase) && e.Value is string val)
            {
                if (string.Equals(val, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    e.CellStyle.BackColor = Color.FromArgb(220, 252, 231);
                    e.CellStyle.ForeColor = Color.FromArgb(22, 101, 52);
                }
                else
                {
                    e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                    e.CellStyle.ForeColor = SalrynUi.Colors.Danger;
                }
            }
        };
    }
}
