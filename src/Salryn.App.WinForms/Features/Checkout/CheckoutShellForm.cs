using System.Drawing;
using System.Windows.Forms;

namespace Salryn.App.WinForms.Features.Checkout;

public sealed class CheckoutShellForm : Form
{
    private readonly CheckoutPresenter _presenter;

    private readonly Label _modeLabel = new();
    private readonly Label _statusLabel = new();

    private readonly TextBox _productInputTextBox = new();
    private readonly Button _searchButton = new();
    private readonly Button _addItemButton = new();
    private readonly Label _productResultLabel = new();
    private readonly Label _preparedNameLabel = new();
    private readonly Label _preparedMetaLabel = new();

    private readonly DataGridView _cartGrid = new();

    private readonly Label _itemCountLabel = new();
    private readonly Label _subtotalValueLabel = new();
    private readonly Label _discountValueLabel = new();
    private readonly Label _taxValueLabel = new();
    private readonly Label _totalValueLabel = new();
    private readonly Label _paidValueLabel = new();
    private readonly Label _changeValueLabel = new();
    private readonly Label _balanceValueLabel = new();

    private readonly TextBox _cashAmountTextBox = new();
    private readonly Button _payCashButton = new();

    private readonly Button _removeItemButton = new();
    private readonly Button _clearCartButton = new();
    private readonly Button _completeSaleButton = new();

    private bool _isRendering;

    public CheckoutShellForm(CheckoutPresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

        Text = "SALRYN POS Checkout";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1220, 760);
        Size = new Size(1360, 820);
        Font = new Font("Segoe UI", 9F);
        BackColor = UiColors.AppBackground;

        BuildShell();
        WireEvents();

        Render(_presenter.Initialize().State);
    }

    private void BuildShell()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            BackColor = UiColors.AppBackground
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));

        Controls.Add(root);

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateWorkspace(), 0, 1);
        root.Controls.Add(CreateStatusBar(), 0, 2);
    }

    private Control CreateHeader()
    {
        Panel header = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 20, 12),
            BackColor = UiColors.Header
        };

        Label title = new()
        {
            AutoSize = true,
            Text = "SALRYN POS",
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 10)
        };

        Label subtitle = new()
        {
            AutoSize = true,
            Text = "Checkout workspace",
            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
            ForeColor = UiColors.HeaderMuted,
            Location = new Point(23, 52)
        };

        _modeLabel.AutoSize = false;
        _modeLabel.Width = 370;
        _modeLabel.Height = 34;
        _modeLabel.Text = "Repository-backed product search • Sale completion locked";
        _modeLabel.TextAlign = ContentAlignment.MiddleRight;
        _modeLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _modeLabel.ForeColor = UiColors.HeaderMuted;
        _modeLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _modeLabel.Location = new Point(header.Width - _modeLabel.Width - 20, 26);

        header.Resize += (_, _) =>
        {
            _modeLabel.Location = new Point(header.Width - _modeLabel.Width - 20, 26);
        };

        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_modeLabel);

        return header;
    }

    private Control CreateWorkspace()
    {
        TableLayoutPanel workspace = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 14, 0, 14),
            BackColor = UiColors.AppBackground
        };

        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72F));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));

        workspace.Controls.Add(CreateLeftWorkspace(), 0, 0);
        workspace.Controls.Add(CreateRightWorkspace(), 1, 0);

        return workspace;
    }

    private Control CreateLeftWorkspace()
    {
        TableLayoutPanel left = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 12, 0),
            BackColor = UiColors.AppBackground
        };

        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 178F));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        left.Controls.Add(CreateProductSearchCard(), 0, 0);
        left.Controls.Add(CreateCartCard(), 0, 1);

        return left;
    }

    private Control CreateRightWorkspace()
    {
        TableLayoutPanel right = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            BackColor = UiColors.AppBackground
        };

        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 292F));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 174F));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        right.Controls.Add(CreateTotalsCard(), 0, 0);
        right.Controls.Add(CreatePaymentCard(), 0, 1);
        right.Controls.Add(CreateActionsCard(), 0, 2);

        return right;
    }

    private Control CreateProductSearchCard()
    {
        Panel card = CreateCard();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = Color.White
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124F));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Label title = CreateSectionTitle("Product lookup");
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 4);

        layout.Controls.Add(CreateFieldLabel("Barcode / Name"), 0, 1);

        _productInputTextBox.Dock = DockStyle.Fill;
        _productInputTextBox.PlaceholderText = "Scan barcode or type product name";
        _productInputTextBox.Font = new Font("Segoe UI", 11F, FontStyle.Regular);
        layout.Controls.Add(_productInputTextBox, 1, 1);

        ConfigurePrimaryButton(_searchButton, "Search");
        layout.Controls.Add(_searchButton, 2, 1);

        ConfigureSuccessButton(_addItemButton, "Add");
        layout.Controls.Add(_addItemButton, 3, 1);

        layout.Controls.Add(CreateFieldLabel("Lookup status"), 0, 2);

        _productResultLabel.Dock = DockStyle.Fill;
        _productResultLabel.TextAlign = ContentAlignment.MiddleLeft;
        _productResultLabel.ForeColor = UiColors.TextMuted;
        layout.Controls.Add(_productResultLabel, 1, 2);
        layout.SetColumnSpan(_productResultLabel, 3);

        layout.Controls.Add(CreateFieldLabel("Prepared item"), 0, 3);

        Panel preparedPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 6, 10, 6),
            BackColor = UiColors.PreparedBackground
        };

        _preparedNameLabel.Dock = DockStyle.Top;
        _preparedNameLabel.Height = 24;
        _preparedNameLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        _preparedNameLabel.ForeColor = UiColors.TextStrong;

        _preparedMetaLabel.Dock = DockStyle.Fill;
        _preparedMetaLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        _preparedMetaLabel.ForeColor = UiColors.TextMuted;

        preparedPanel.Controls.Add(_preparedMetaLabel);
        preparedPanel.Controls.Add(_preparedNameLabel);

        layout.Controls.Add(preparedPanel, 1, 3);
        layout.SetColumnSpan(preparedPanel, 3);

        card.Controls.Add(layout);

        return card;
    }

    private Control CreateCartCard()
    {
        Panel card = CreateCard();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18),
            BackColor = Color.White
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Panel header = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };

        Label title = CreateSectionTitle("Draft cart");
        title.Location = new Point(0, 4);

        _itemCountLabel.AutoSize = false;
        _itemCountLabel.Width = 160;
        _itemCountLabel.Height = 28;
        _itemCountLabel.TextAlign = ContentAlignment.MiddleRight;
        _itemCountLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _itemCountLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _itemCountLabel.ForeColor = UiColors.TextMuted;
        _itemCountLabel.Location = new Point(header.Width - _itemCountLabel.Width, 5);

        header.Resize += (_, _) =>
        {
            _itemCountLabel.Location = new Point(header.Width - _itemCountLabel.Width, 5);
        };

        header.Controls.Add(title);
        header.Controls.Add(_itemCountLabel);

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
        _cartGrid.GridColor = UiColors.Border;
        _cartGrid.EnableHeadersVisualStyles = false;
        _cartGrid.ColumnHeadersDefaultCellStyle.BackColor = UiColors.GridHeader;
        _cartGrid.ColumnHeadersDefaultCellStyle.ForeColor = UiColors.TextStrong;
        _cartGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _cartGrid.DefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        _cartGrid.DefaultCellStyle.SelectionBackColor = UiColors.Selection;
        _cartGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        _cartGrid.RowTemplate.Height = 32;

        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductColumn", HeaderText = "Product", FillWeight = 180 });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "BarcodeColumn", HeaderText = "Barcode", FillWeight = 110 });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QtyColumn", HeaderText = "Qty", FillWeight = 55 });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitColumn", HeaderText = "Unit", FillWeight = 60 });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PriceColumn", HeaderText = "Price", FillWeight = 90 });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DiscountColumn", HeaderText = "Discount", FillWeight = 90 });
        _cartGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TotalColumn", HeaderText = "Line Total", FillWeight = 100 });

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_cartGrid, 0, 1);

        card.Controls.Add(layout);

        return card;
    }

    private Control CreateTotalsCard()
    {
        Panel card = CreateCard();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18),
            BackColor = Color.White
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(CreateSectionTitle("Totals"), 0, 0);

        TableLayoutPanel totals = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            BackColor = Color.White
        };

        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));

        for (int i = 0; i < 7; i++)
        {
            totals.RowStyles.Add(new RowStyle(SizeType.Percent, 14.28F));
        }

        AddTotalRow(totals, 0, "Subtotal", _subtotalValueLabel, false);
        AddTotalRow(totals, 1, "Discount", _discountValueLabel, false);
        AddTotalRow(totals, 2, "Tax", _taxValueLabel, false);
        AddTotalRow(totals, 3, "Total", _totalValueLabel, true);
        AddTotalRow(totals, 4, "Paid", _paidValueLabel, false);
        AddTotalRow(totals, 5, "Change", _changeValueLabel, false);
        AddTotalRow(totals, 6, "Balance", _balanceValueLabel, true);

        layout.Controls.Add(totals, 0, 1);

        card.Controls.Add(layout);

        return card;
    }

    private Control CreatePaymentCard()
    {
        Panel card = CreateCard();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = Color.White
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Label title = CreateSectionTitle("Payment");
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);

        layout.Controls.Add(CreateFieldLabel("Method"), 0, 1);

        ComboBox method = new()
        {
            Dock = DockStyle.Fill,
            Enabled = false,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        method.Items.Add("Cash");
        method.SelectedIndex = 0;

        layout.Controls.Add(method, 1, 1);

        layout.Controls.Add(CreateFieldLabel("Cash"), 0, 2);

        _cashAmountTextBox.Dock = DockStyle.Fill;
        _cashAmountTextBox.PlaceholderText = "0.00";
        _cashAmountTextBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        layout.Controls.Add(_cashAmountTextBox, 1, 2);

        ConfigurePrimaryButton(_payCashButton, "Add Cash Payment");
        layout.Controls.Add(_payCashButton, 1, 3);

        card.Controls.Add(layout);

        return card;
    }

    private Control CreateActionsCard()
    {
        Panel card = CreateCard();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(18),
            BackColor = Color.White
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(CreateSectionTitle("Actions"), 0, 0);

        ConfigureSecondaryButton(_removeItemButton, "Remove Selected Item");
        ConfigureSecondaryButton(_clearCartButton, "Clear Draft Cart");
        ConfigureLockedButton(_completeSaleButton, "Complete Sale Locked");

        layout.Controls.Add(_removeItemButton, 0, 1);
        layout.Controls.Add(_clearCartButton, 0, 2);
        layout.Controls.Add(_completeSaleButton, 0, 3);

        Label note = new()
        {
            Dock = DockStyle.Fill,
            Text = "This screen can search products, add draft lines, show totals, and add draft cash payment. Final sale save remains locked.",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = UiColors.TextMuted
        };

        layout.Controls.Add(note, 0, 4);

        card.Controls.Add(layout);

        return card;
    }

    private Control CreateStatusBar()
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 8),
            BackColor = UiColors.StatusBackground
        };

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _statusLabel.ForeColor = UiColors.TextStrong;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        panel.Controls.Add(_statusLabel);

        return panel;
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

            Render(_presenter.SelectLine(GetSelectedLineId()).State);
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

            Render(_presenter.SetCashAmountText(_cashAmountTextBox.Text).State);
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

    private void Render(CheckoutViewModel state)
    {
        _isRendering = true;

        try
        {
            if (_productInputTextBox.Text != state.ProductInputText)
            {
                _productInputTextBox.Text = state.ProductInputText;
            }

            _productResultLabel.Text = state.ProductResultText;

            if (state.PreparedProduct is null)
            {
                _preparedNameLabel.Text = "No product prepared";
                _preparedMetaLabel.Text = "Search an existing product, then add it to the draft cart.";
            }
            else
            {
                _preparedNameLabel.Text = state.PreparedProduct.ProductName;
                _preparedMetaLabel.Text =
                    $"{state.PreparedProduct.Barcode ?? "No barcode"} • {state.PreparedProduct.UnitPriceText} • Stock {state.PreparedProduct.StockOnHandText} {state.PreparedProduct.UnitName}";
            }

            RenderCart(state.Lines);

            _itemCountLabel.Text = $"{state.Lines.Count} item(s)";
            _subtotalValueLabel.Text = state.Totals.SubtotalText;
            _discountValueLabel.Text = state.Totals.SaleDiscountText;
            _taxValueLabel.Text = state.Totals.TaxText;
            _totalValueLabel.Text = state.Totals.TotalText;
            _paidValueLabel.Text = state.Totals.AmountPaidText;
            _changeValueLabel.Text = state.Totals.ChangeText;
            _balanceValueLabel.Text = state.Totals.BalanceDueText;

            if (!_cashAmountTextBox.Focused)
            {
                _cashAmountTextBox.Text = state.CashAmountCents == 0
                    ? string.Empty
                    : (state.CashAmountCents / 100m).ToString("0.00");
            }

            _searchButton.Enabled = state.CanSearchProduct;
            _addItemButton.Enabled = state.CanAddPreparedProductLine;
            _removeItemButton.Enabled = state.Lines.Count > 0 && GetSelectedLineId().HasValue;
            _clearCartButton.Enabled = state.Lines.Count > 0;
            _payCashButton.Enabled = state.CanAddCashPayment;
            _completeSaleButton.Enabled = false;

            _statusLabel.Text = state.HasError
                ? $"{state.StatusMessage} [{state.ErrorCode}]"
                : state.StatusMessage;

            _statusLabel.ForeColor = state.HasError
                ? UiColors.Danger
                : UiColors.TextStrong;
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

    private static Panel CreateCard()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 12),
            BackColor = UiColors.Border
        };
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = UiColors.TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiColors.TextMuted
        };
    }

    private static void AddTotalRow(
        TableLayoutPanel table,
        int row,
        string label,
        Label valueControl,
        bool emphasized)
    {
        Font labelFont = emphasized
            ? new Font("Segoe UI", 10.5F, FontStyle.Bold)
            : new Font("Segoe UI", 9.5F, FontStyle.Regular);

        Font valueFont = emphasized
            ? new Font("Segoe UI", 13F, FontStyle.Bold)
            : new Font("Segoe UI", 10F, FontStyle.Bold);

        Label labelControl = new()
        {
            Dock = DockStyle.Fill,
            Text = label,
            Font = labelFont,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiColors.TextMuted
        };

        valueControl.Dock = DockStyle.Fill;
        valueControl.Text = "PHP 0.00";
        valueControl.Font = valueFont;
        valueControl.TextAlign = ContentAlignment.MiddleRight;
        valueControl.ForeColor = emphasized
            ? UiColors.TextStrong
            : UiColors.TextMuted;

        table.Controls.Add(labelControl, 0, row);
        table.Controls.Add(valueControl, 1, row);
    }

    private static void ConfigurePrimaryButton(Button button, string text)
    {
        button.Dock = DockStyle.Fill;
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = UiColors.Primary;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.UseVisualStyleBackColor = false;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    private static void ConfigureSuccessButton(Button button, string text)
    {
        button.Dock = DockStyle.Fill;
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = UiColors.Success;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.UseVisualStyleBackColor = false;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    private static void ConfigureSecondaryButton(Button button, string text)
    {
        button.Dock = DockStyle.Fill;
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = UiColors.Border;
        button.BackColor = Color.White;
        button.ForeColor = UiColors.TextStrong;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.UseVisualStyleBackColor = false;
        button.Margin = new Padding(0, 0, 0, 8);
    }

    private static void ConfigureLockedButton(Button button, string text)
    {
        button.Dock = DockStyle.Fill;
        button.Text = text;
        button.Enabled = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = UiColors.Locked;
        button.ForeColor = UiColors.TextMuted;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.UseVisualStyleBackColor = false;
        button.Margin = new Padding(0, 0, 0, 8);
    }

    private static class UiColors
    {
        public static readonly Color AppBackground = Color.FromArgb(244, 247, 251);
        public static readonly Color Header = Color.FromArgb(15, 23, 42);
        public static readonly Color HeaderMuted = Color.FromArgb(203, 213, 225);
        public static readonly Color Border = Color.FromArgb(220, 227, 235);
        public static readonly Color GridHeader = Color.FromArgb(241, 245, 249);
        public static readonly Color PreparedBackground = Color.FromArgb(248, 250, 252);
        public static readonly Color StatusBackground = Color.FromArgb(226, 232, 240);
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Selection = Color.FromArgb(37, 99, 235);
        public static readonly Color Locked = Color.FromArgb(226, 232, 240);
        public static readonly Color Danger = Color.FromArgb(185, 28, 28);
        public static readonly Color TextStrong = Color.FromArgb(15, 23, 42);
        public static readonly Color TextMuted = Color.FromArgb(71, 85, 105);
    }
}
