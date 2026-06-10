using System.Drawing;
using System.Windows.Forms;

namespace Salryn.App.WinForms.Features.Checkout;

public sealed class CheckoutShellForm : Form
{
    private readonly CheckoutPresenter _presenter;

    private readonly Label _subtitleLabel = new();
    private readonly TextBox _productInputTextBox = new();
    private readonly Button _searchButton = new();
    private readonly Button _addItemButton = new();
    private readonly Label _resultLabel = new();
    private readonly Label _preparedProductLabel = new();
    private readonly DataGridView _cartGrid = new();

    private readonly Label _subtotalValueLabel = new();
    private readonly Label _discountValueLabel = new();
    private readonly Label _taxValueLabel = new();
    private readonly Label _totalValueLabel = new();
    private readonly Label _paidValueLabel = new();
    private readonly Label _balanceValueLabel = new();

    private readonly TextBox _cashAmountTextBox = new();
    private readonly Button _payCashButton = new();
    private readonly Button _removeItemButton = new();
    private readonly Button _clearCartButton = new();
    private readonly Button _completeSaleButton = new();
    private readonly Label _statusLabel = new();

    private bool _isRendering;

    public CheckoutShellForm(CheckoutPresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

        Text = "SALRYN POS Checkout - Repository Search Attempt";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1280, 780);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        BuildShell();
        WireEvents();

        Render(_presenter.Initialize().State);
    }

    private void BuildShell()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(14),
            BackColor = Color.FromArgb(245, 247, 250)
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

        Controls.Add(root);

        Control header = CreateHeader();
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 3);

        Control productInput = CreateProductInputSection();
        root.Controls.Add(productInput, 0, 1);
        root.SetColumnSpan(productInput, 3);

        Control cart = CreateCartSection();
        root.Controls.Add(cart, 0, 2);
        root.SetColumnSpan(cart, 2);

        Control rightPanel = CreateRightPanel();
        root.Controls.Add(rightPanel, 2, 2);

        Control status = CreateStatusSection();
        root.Controls.Add(status, 0, 3);
        root.SetColumnSpan(status, 3);
    }

    private void WireEvents()
    {
        _productInputTextBox.TextChanged += (_, _) =>
        {
            if (_isRendering)
            {
                return;
            }

            Render(_presenter.SetProductInputText(_productInputTextBox.Text).State);
        };

        _productInputTextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            await SearchCurrentProductAsync();
        };

        _searchButton.Click += async (_, _) => await SearchCurrentProductAsync();

        _addItemButton.Click += (_, _) =>
        {
            Render(_presenter.AddPreparedProductLine().State);
            _productInputTextBox.Focus();
        };

        _cartGrid.SelectionChanged += (_, _) =>
        {
            if (_isRendering)
            {
                return;
            }

            Guid? lineId = GetSelectedLineId();
            Render(_presenter.SelectLine(lineId).State);
        };

        _removeItemButton.Click += (_, _) =>
        {
            Guid? lineId = GetSelectedLineId();

            if (lineId.HasValue)
            {
                Render(_presenter.RemoveLine(lineId.Value).State);
            }
        };

        _clearCartButton.Click += (_, _) =>
        {
            Render(_presenter.ClearDraft().State);
            _productInputTextBox.Focus();
        };

        _cashAmountTextBox.TextChanged += (_, _) =>
        {
            if (_isRendering)
            {
                return;
            }

            CheckoutPresenterOperationResult result =
                _presenter.SetCashAmountText(_cashAmountTextBox.Text);

            Render(result.State);
        };

        _payCashButton.Click += (_, _) =>
        {
            CheckoutPresenterOperationResult amountResult =
                _presenter.SetCashAmountText(_cashAmountTextBox.Text);

            if (!amountResult.IsSuccess)
            {
                Render(amountResult.State);
                return;
            }

            Render(_presenter.AddCashPayment().State);
        };
    }

    private async Task SearchCurrentProductAsync()
    {
        _searchButton.Enabled = false;

        try
        {
            CheckoutPresenterOperationResult result =
                await _presenter.SearchProductAsync(_productInputTextBox.Text);

            Render(result.State);
        }
        finally
        {
            _searchButton.Enabled = _presenter.State.CanSearchProduct;
        }
    }

    private Control CreateHeader()
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 10, 18, 10),
            BackColor = Color.FromArgb(20, 31, 46)
        };

        Label title = new()
        {
            AutoSize = true,
            Text = "SALRYN POS Checkout",
            Font = new Font("Segoe UI", 20F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(18, 10)
        };

        _subtitleLabel.AutoSize = true;
        _subtitleLabel.Text = "Front-end attempt - product search now uses the real read-only product repository.";
        _subtitleLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        _subtitleLabel.ForeColor = Color.FromArgb(210, 220, 230);
        _subtitleLabel.Location = new Point(21, 48);

        panel.Controls.Add(title);
        panel.Controls.Add(_subtitleLabel);

        return panel;
    }

    private Control CreateProductInputSection()
    {
        GroupBox group = CreateGroupBox("Product Scan / Search");

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 3,
            Padding = new Padding(12)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        Label inputLabel = CreateFieldLabel("Barcode / Product:");

        _productInputTextBox.Dock = DockStyle.Fill;
        _productInputTextBox.PlaceholderText = "Scan barcode or type an existing product name...";
        _productInputTextBox.Enabled = true;

        _searchButton.Dock = DockStyle.Fill;
        _searchButton.Text = "Scan / Search";
        _searchButton.UseVisualStyleBackColor = true;

        _addItemButton.Dock = DockStyle.Fill;
        _addItemButton.Text = "Add Item";
        _addItemButton.UseVisualStyleBackColor = true;

        _resultLabel.Dock = DockStyle.Fill;
        _resultLabel.ForeColor = Color.FromArgb(90, 100, 115);
        _resultLabel.TextAlign = ContentAlignment.MiddleLeft;

        _preparedProductLabel.Dock = DockStyle.Fill;
        _preparedProductLabel.ForeColor = Color.FromArgb(45, 58, 75);
        _preparedProductLabel.TextAlign = ContentAlignment.MiddleLeft;

        layout.Controls.Add(inputLabel, 0, 0);
        layout.Controls.Add(_productInputTextBox, 1, 0);
        layout.Controls.Add(_searchButton, 2, 0);
        layout.Controls.Add(_addItemButton, 3, 0);

        layout.Controls.Add(CreateFieldLabel("Result:"), 0, 1);
        layout.Controls.Add(_resultLabel, 1, 1);
        layout.SetColumnSpan(_resultLabel, 4);

        layout.Controls.Add(CreateFieldLabel("Prepared:"), 0, 2);
        layout.Controls.Add(_preparedProductLabel, 1, 2);
        layout.SetColumnSpan(_preparedProductLabel, 4);

        group.Controls.Add(layout);

        return group;
    }

    private Control CreateCartSection()
    {
        GroupBox group = CreateGroupBox("Draft Cart");

        _cartGrid.Dock = DockStyle.Fill;
        _cartGrid.ReadOnly = true;
        _cartGrid.AllowUserToAddRows = false;
        _cartGrid.AllowUserToDeleteRows = false;
        _cartGrid.AllowUserToResizeRows = false;
        _cartGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _cartGrid.BackgroundColor = Color.White;
        _cartGrid.BorderStyle = BorderStyle.None;
        _cartGrid.RowHeadersVisible = false;
        _cartGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _cartGrid.MultiSelect = false;

        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductColumn", HeaderText = "Product" });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "BarcodeColumn", HeaderText = "Barcode" });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QtyColumn", HeaderText = "Qty" });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitColumn", HeaderText = "Unit" });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PriceColumn", HeaderText = "Price" });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DiscountColumn", HeaderText = "Discount" });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TotalColumn", HeaderText = "Line Total" });

        group.Controls.Add(_cartGrid);

        return group;
    }

    private Control CreateRightPanel()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8, 0, 0, 0)
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 230F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(CreateTotalsSection(), 0, 0);
        layout.Controls.Add(CreatePaymentSection(), 0, 1);
        layout.Controls.Add(CreateActionsSection(), 0, 2);

        return layout;
    }

    private Control CreateTotalsSection()
    {
        GroupBox group = CreateGroupBox("Totals");

        TableLayoutPanel totals = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12)
        };

        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));

        for (int i = 0; i < 6; i++)
        {
            totals.RowStyles.Add(new RowStyle(SizeType.Percent, 16.66F));
        }

        AddTotalRow(totals, 0, "Subtotal", _subtotalValueLabel, false);
        AddTotalRow(totals, 1, "Discount", _discountValueLabel, false);
        AddTotalRow(totals, 2, "Tax", _taxValueLabel, false);
        AddTotalRow(totals, 3, "Total", _totalValueLabel, true);
        AddTotalRow(totals, 4, "Paid", _paidValueLabel, false);
        AddTotalRow(totals, 5, "Balance", _balanceValueLabel, true);

        group.Controls.Add(totals);

        return group;
    }

    private Control CreatePaymentSection()
    {
        GroupBox group = CreateGroupBox("Payment");

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        ComboBox method = new()
        {
            Dock = DockStyle.Fill,
            Enabled = false,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        method.Items.Add("Cash");
        method.SelectedIndex = 0;

        _cashAmountTextBox.Dock = DockStyle.Fill;
        _cashAmountTextBox.PlaceholderText = "0.00";

        _payCashButton.Dock = DockStyle.Fill;
        _payCashButton.Text = "Pay Cash";
        _payCashButton.UseVisualStyleBackColor = true;

        layout.Controls.Add(CreateFieldLabel("Method:"), 0, 0);
        layout.Controls.Add(method, 1, 0);
        layout.Controls.Add(CreateFieldLabel("Amount:"), 0, 1);
        layout.Controls.Add(_cashAmountTextBox, 1, 1);
        layout.Controls.Add(_payCashButton, 1, 2);

        group.Controls.Add(layout);

        return group;
    }

    private Control CreateActionsSection()
    {
        GroupBox group = CreateGroupBox("Checkout Actions");

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12)
        };

        _removeItemButton.Width = 260;
        _removeItemButton.Height = 38;
        _removeItemButton.Text = "Remove Item";
        _removeItemButton.UseVisualStyleBackColor = true;
        _removeItemButton.Margin = new Padding(0, 0, 0, 8);

        _clearCartButton.Width = 260;
        _clearCartButton.Height = 38;
        _clearCartButton.Text = "Clear Cart";
        _clearCartButton.UseVisualStyleBackColor = true;
        _clearCartButton.Margin = new Padding(0, 0, 0, 8);

        _completeSaleButton.Width = 260;
        _completeSaleButton.Height = 38;
        _completeSaleButton.Text = "Complete Sale";
        _completeSaleButton.UseVisualStyleBackColor = true;
        _completeSaleButton.Margin = new Padding(0, 0, 0, 8);
        _completeSaleButton.Enabled = false;

        Label note = new()
        {
            AutoSize = false,
            Width = 260,
            Height = 80,
            Text = "Complete Sale is still locked. This phase only binds product search, draft cart, totals, and cash draft payment to presenter state.",
            ForeColor = Color.FromArgb(90, 100, 115)
        };

        actions.Controls.Add(_removeItemButton);
        actions.Controls.Add(_clearCartButton);
        actions.Controls.Add(_completeSaleButton);
        actions.Controls.Add(note);

        group.Controls.Add(actions);

        return group;
    }

    private Control CreateStatusSection()
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 8, 14, 8),
            BackColor = Color.FromArgb(232, 238, 246)
        };

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.FromArgb(45, 58, 75);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        panel.Controls.Add(_statusLabel);

        return panel;
    }

    private void Render(CheckoutViewModel state)
    {
        _isRendering = true;

        try
        {
            _productInputTextBox.Text = state.ProductInputText;
            _resultLabel.Text = state.ProductResultText;

            _preparedProductLabel.Text = state.PreparedProduct is null
                ? "No prepared product."
                : $"{state.PreparedProduct.ProductName} | {state.PreparedProduct.Barcode ?? "no barcode"} | {state.PreparedProduct.UnitPriceText} | Stock: {state.PreparedProduct.StockOnHandText} {state.PreparedProduct.UnitName}";

            _subtotalValueLabel.Text = state.Totals.SubtotalText;
            _discountValueLabel.Text = state.Totals.SaleDiscountText;
            _taxValueLabel.Text = state.Totals.TaxText;
            _totalValueLabel.Text = state.Totals.TotalText;
            _paidValueLabel.Text = state.Totals.AmountPaidText;
            _balanceValueLabel.Text = state.Totals.BalanceDueText;

            _cashAmountTextBox.Text = state.CashAmountCents == 0
                ? string.Empty
                : (state.CashAmountCents / 100m).ToString("0.00");

            _searchButton.Enabled = state.CanSearchProduct;
            _addItemButton.Enabled = state.CanAddPreparedProductLine;
            _removeItemButton.Enabled = state.CanRemoveSelectedLine && GetSelectedLineId().HasValue;
            _clearCartButton.Enabled = state.Lines.Count > 0;
            _payCashButton.Enabled = state.CanAddCashPayment;
            _completeSaleButton.Enabled = false;

            _statusLabel.Text = state.HasError
                ? $"{state.StatusMessage} [{state.ErrorCode}]"
                : state.StatusMessage;

            RenderCart(state.Lines);
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void RenderCart(IReadOnlyList<CheckoutLineViewModel> lines)
    {
        Guid? selectedLineId = GetSelectedLineId();

        _cartGrid.Rows.Clear();

        foreach (CheckoutLineViewModel line in lines)
        {
            int rowIndex = _cartGrid.Rows.Add(
                line.ProductName,
                line.Barcode ?? string.Empty,
                line.QuantityText,
                line.UnitName,
                line.UnitPriceText,
                line.DiscountText,
                line.LineTotalText);

            _cartGrid.Rows[rowIndex].Tag = line.LineId;
        }

        if (selectedLineId.HasValue)
        {
            foreach (DataGridViewRow row in _cartGrid.Rows)
            {
                if (row.Tag is Guid id && id == selectedLineId.Value)
                {
                    row.Selected = true;
                    break;
                }
            }
        }
    }

    private Guid? GetSelectedLineId()
    {
        if (_cartGrid.SelectedRows.Count == 0)
        {
            return null;
        }

        return _cartGrid.SelectedRows[0].Tag is Guid lineId
            ? lineId
            : null;
    }

    private static GroupBox CreateGroupBox(string title)
    {
        return new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = title,
            Padding = new Padding(10),
            BackColor = Color.White
        };
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(55, 65, 80)
        };
    }

    private static void AddTotalRow(
        TableLayoutPanel table,
        int row,
        string label,
        Label valueControl,
        bool emphasized)
    {
        Font font = emphasized
            ? new Font("Segoe UI", 10.5F, FontStyle.Bold)
            : new Font("Segoe UI", 9.5F, FontStyle.Regular);

        Label labelControl = new()
        {
            Dock = DockStyle.Fill,
            Text = label,
            Font = font,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(55, 65, 80)
        };

        valueControl.Dock = DockStyle.Fill;
        valueControl.Text = "PHP 0.00";
        valueControl.Font = font;
        valueControl.TextAlign = ContentAlignment.MiddleRight;
        valueControl.ForeColor = emphasized
            ? Color.FromArgb(20, 31, 46)
            : Color.FromArgb(55, 65, 80);

        table.Controls.Add(labelControl, 0, row);
        table.Controls.Add(valueControl, 1, row);
    }
}


