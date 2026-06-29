using System.Globalization;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.Features.Inventory;

// Kirch: UI-only inventory adjustment workflow. Stock writes stay inside IInventoryWriteRepository.
public sealed class InventoryAdjustmentForm : Form
{
    private readonly IProductReadRepository _products;
    private readonly IInventoryWriteRepository _inventoryWrites;
    private readonly Guid _userId;

    private readonly TextBox _searchTextBox = new();
    private readonly Button _searchButton = new();
    private readonly DataGridView _productsGrid = J20MvpUi.Grid();
    private readonly TextBox _quantityDeltaTextBox = new();
    private readonly TextBox _reasonTextBox = new();
    private readonly Button _applyButton = new();
    private readonly Button _cancelButton = new();
    private readonly Label _statusLabel = new();

    public InventoryAdjustmentForm(
        IProductReadRepository products,
        IInventoryWriteRepository inventoryWrites,
        Guid userId)
    {
        _products = products ?? throw new ArgumentNullException(nameof(products));
        _inventoryWrites = inventoryWrites ?? throw new ArgumentNullException(nameof(inventoryWrites));
        _userId = userId;

        Text = "Adjust Inventory";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 640);
        Size = new Size(1080, 700);
        Font = new Font("Segoe UI", 9F);
        BackColor = SalrynUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await SearchProductsAsync().ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        // P2: SalrynUi ledger console layout
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

        Controls.Add(root);

        root.Controls.Add(SalrynUi.CreatePageHeader(
            "Adjust Inventory",
            "Search a stock-tracked product, enter a signed quantity delta, and record a reason."), 0, 0);

        var cmdBar = SalrynUi.CreateCommandBar();
        var searchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8F));

        _searchTextBox.Dock = DockStyle.Fill;
        _searchTextBox.Font = new Font("Segoe UI", 10.5F);
        _searchTextBox.PlaceholderText = "Search product name, barcode, or SKU...";

        SalrynUi.ConfigurePrimaryButton(_searchButton, "Search");

        searchPanel.Controls.Add(_searchTextBox, 0, 0);
        searchPanel.Controls.Add(_searchButton, 1, 0);
        cmdBar.Controls.Add(searchPanel);
        root.Controls.Add(cmdBar, 0, 1);

        var gridCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SalrynUi.Colors.Surface,
            BorderStyle = BorderStyle.FixedSingle
        };
        SalrynUi.ConfigureInventoryGrid(_productsGrid);
        gridCard.Controls.Add(_productsGrid);
        root.Controls.Add(gridCard, 0, 2);

        var adjustmentPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = SalrynUi.Colors.Surface,
            Padding = new Padding(8)
        };
        adjustmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        adjustmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        adjustmentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        adjustmentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        adjustmentPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var section = SalrynUi.CreateSectionTitle("Adjustment");
        adjustmentPanel.Controls.Add(section, 0, 0);
        adjustmentPanel.SetColumnSpan(section, 2);

        adjustmentPanel.Controls.Add(SalrynUi.CreateFieldLabel("Quantity delta", required: false), 0, 1);

        _quantityDeltaTextBox.Dock = DockStyle.Fill;
        _quantityDeltaTextBox.Font = new Font("Segoe UI", 10.5F);
        _quantityDeltaTextBox.PlaceholderText = "Use positive to add, negative to remove";
        adjustmentPanel.Controls.Add(_quantityDeltaTextBox, 1, 1);

        // Required reason with visual marker
        adjustmentPanel.Controls.Add(SalrynUi.CreateFieldLabel("Reason", required: true), 0, 2);

        _reasonTextBox.Dock = DockStyle.Fill;
        _reasonTextBox.Multiline = true;
        _reasonTextBox.MaxLength = 500;
        _reasonTextBox.Font = new Font("Segoe UI", 10F);
        _reasonTextBox.PlaceholderText = "Required adjustment reason...";
        adjustmentPanel.Controls.Add(_reasonTextBox, 1, 2);

        root.Controls.Add(adjustmentPanel, 0, 3);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        SalrynUi.StyleFooterStrip(footer);

        // Adjust action styled as warning/primary per target
        SalrynUi.ConfigureWarningButton(_applyButton, "Adjust Stock");
        SalrynUi.ConfigureSecondaryButton(_cancelButton, "Cancel");

        footer.Controls.Add(_applyButton);
        footer.Controls.Add(_cancelButton);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        SalrynUi.SetStatusNeutral(_statusLabel, string.Empty);
        SalrynUi.ConfigureFooterStatus(_statusLabel);
        footer.Controls.Add(_statusLabel);

        root.Controls.Add(footer, 0, 4);
    }

    private static Label BuildLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = J20MvpUi.Colors.TextStrong,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };
    }

    private void WireEvents()
    {
        _searchButton.Click += async (_, _) => await SearchProductsAsync().ConfigureAwait(true);

        _searchTextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            await SearchProductsAsync().ConfigureAwait(true);
        };

        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        _applyButton.Click += async (_, _) => await ApplyAdjustmentAsync().ConfigureAwait(true);
    }

    private async Task SearchProductsAsync()
    {
        SalrynUi.SetStatusNeutral(_statusLabel, "Searching products...");

        string? searchText = string.IsNullOrWhiteSpace(_searchTextBox.Text)
            ? null
            : _searchTextBox.Text.Trim();

        var result = await _products.SearchAsync(new ProductSearchFilter(
            SearchText: searchText,
            CategoryId: null,
            BrandId: null,
            IsActive: true,
            IncludeInactive: false,
            Page: PageRequest.FirstPage(100))).ConfigureAwait(true);

        if (result.IsFailure)
        {
            _productsGrid.DataSource = Array.Empty<ProductAdjustmentRow>();
            SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
            return;
        }

        _productsGrid.DataSource = result.Value.Items
            .Select(static product => new ProductAdjustmentRow(
                ProductId: product.Id,
                Sku: product.Sku ?? string.Empty,
                Barcode: product.Barcode ?? string.Empty,
                Name: product.Name,
                Unit: product.UnitName,
                Stock: product.TrackStock ? product.StockOnHand.ToString() : "Not tracked",
                TrackStock: product.TrackStock,
                Status: product.IsActive ? "Active" : "Inactive"))
            .ToList();

        if (_productsGrid.Columns["ProductId"] is not null)
        {
            _productsGrid.Columns["ProductId"]!.Visible = false;
        }

        SalrynUi.SetStatusNeutral(_statusLabel, $"Loaded {result.Value.Items.Count} products. Total matches: {result.Value.TotalCount}.");
    }

    private async Task ApplyAdjustmentAsync()
    {
        ProductAdjustmentRow? selectedProduct = GetSelectedProduct();

        if (selectedProduct is null)
        {
            SetError("Select a product before applying an adjustment.");
            return;
        }

        if (!selectedProduct.TrackStock)
        {
            SetError("Selected product is not stock-tracked.");
            return;
        }

        if (!TryParseQuantityDelta(
            _quantityDeltaTextBox.Text,
            out StockMovementType movementType,
            out Quantity quantity,
            out string? quantityError))
        {
            SetError(quantityError ?? "Quantity delta is invalid.");
            return;
        }

        string reason = _reasonTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(reason))
        {
            SetError("Adjustment reason is required.");
            return;
        }

        if (_userId == Guid.Empty)
        {
            SetError("Inventory adjustment requires an active user id.");
            return;
        }

        _applyButton.Enabled = false;
        SalrynUi.SetStatusNeutral(_statusLabel, "Saving inventory adjustment...");

        StockMovement movement = new()
        {
            Id = Guid.NewGuid(),
            ProductId = selectedProduct.ProductId,
            MovementType = movementType,
            QuantityChange = quantity,
            CostPrice = null,
            ReferenceType = "INVENTORY_ADJUSTMENT",
            ReferenceId = null,
            Reason = reason,
            UserId = _userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Result result = await _inventoryWrites
            .AddStockMovementAsync(movement)
            .ConfigureAwait(true);

        if (result.IsFailure)
        {
            _applyButton.Enabled = true;
            SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private ProductAdjustmentRow? GetSelectedProduct()
    {
        return _productsGrid.CurrentRow?.DataBoundItem as ProductAdjustmentRow;
    }

    private static bool TryParseQuantityDelta(
        string? rawValue,
        out StockMovementType movementType,
        out Quantity quantity,
        out string? error)
    {
        movementType = StockMovementType.AdjustmentAdd;
        quantity = Quantity.Zero;
        error = null;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            error = "Quantity delta is required.";
            return false;
        }

        bool parsed = decimal.TryParse(
                rawValue.Trim(),
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out decimal delta) ||
            decimal.TryParse(
                rawValue.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out delta);

        if (!parsed)
        {
            error = "Quantity delta must be a valid number.";
            return false;
        }

        if (delta == 0)
        {
            error = "Quantity delta cannot be zero.";
            return false;
        }

        movementType = delta > 0
            ? StockMovementType.AdjustmentAdd
            : StockMovementType.AdjustmentRemove;

        Result<Quantity> quantityResult = Quantity.FromUnits(Math.Abs(delta));

        if (quantityResult.IsFailure)
        {
            error = quantityResult.ErrorDetail.Message;
            return false;
        }

        quantity = quantityResult.Value;
        return true;
    }

    private void SetError(string message)
    {
        SalrynUi.SetStatusError(_statusLabel, message);
    }

    private sealed record ProductAdjustmentRow(
        Guid ProductId,
        string Sku,
        string Barcode,
        string Name,
        string Unit,
        string Stock,
        bool TrackStock,
        string Status);
}
