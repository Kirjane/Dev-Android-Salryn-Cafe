using System.Drawing;
using System.Windows.Forms;

namespace Salryn.App.WinForms.Features.Checkout;

// Kirch file guide: main cashier checkout screen.
// UI location: opened after cashier shift gate passes.
// Frontend note: restyle cards, grid, buttons, and spacing here; do not bypass CheckoutPresenter.

public sealed class CheckoutShellForm : Form
{
    private readonly CheckoutPresenter _presenter;

    // Kirch UI map: checkout header/status labels.
    // UI side: top cashier/shift/receipt/readiness context and bottom operation status.
    // Frontend safe zone: typography, grouping, status badge styling, and spacing.
    // Do not change: labels render CheckoutPresenter state only.
    private readonly Label _modeLabel = new();
    private readonly ComboBox _checkoutModeComboBox = new();
    private readonly Label _shiftContextLabel = new();
    private readonly Label _receiptNumberLabel = new();
    private readonly Label _completionReadinessLabel = new();
    private readonly Label _completionDryRunLabel = new();
    private readonly Label _saleSubmitGuardLabel = new();
    private readonly Label _saleSubmitResultLabel = new();
    private readonly Label _statusLabel = new();

    // Kirch UI map: product search and prepared item controls.
    // UI side: left-side search card where barcode/name input becomes a prepared cart line.
    // Frontend safe zone: scanner-friendly input sizing, result card layout, and add button styling.
    // Do not change: Search and Add Item must continue through CheckoutPresenter.
    private readonly TextBox _productInputTextBox = new();
    private readonly Button _searchButton = new();
    private readonly Button _addItemButton = new();
    private readonly Label _productResultLabel = new();
    private readonly Label _preparedNameLabel = new();
    private readonly Label _preparedMetaLabel = new();

    // Kirch UI map: cart and totals controls.
    // UI side: cart grid shows selected sale lines; totals labels summarize subtotal, discounts, tax, paid, change, and balance.
    // Frontend safe zone: grid column width, row height, totals alignment, and visual hierarchy.
    // Do not change: selected line id and totals come from CheckoutViewModel.
    private readonly DataGridView _cartGrid = new();

    private readonly Label _itemCountLabel = new();
    private readonly Label _subtotalValueLabel = new();
    private readonly Label _discountValueLabel = new();
    private readonly Label _taxValueLabel = new();
    private readonly Label _totalValueLabel = new();
    private readonly Label _paidValueLabel = new();
    private readonly Label _changeValueLabel = new();
    private readonly Label _balanceValueLabel = new();

    // Kirch UI map: payment and completion action controls.
    // UI side: right-side payment card and action buttons for dry-run, submit guard, remove, clear, and complete sale.
    // Frontend safe zone: button color, grouping, disabled states, and cashier-friendly spacing.
    // Do not change: payment and complete-sale actions must continue through CheckoutPresenter.
    private readonly TextBox _cashAmountTextBox = new();
    private readonly Button _payCashButton = new();

    private readonly Button _dryRunCompletionButton = new();
    private readonly Button _prepareSubmitGuardButton = new();
    private readonly Button _removeItemButton = new();
    private readonly Button _clearCartButton = new();
    private readonly Button _completeSaleButton = new();

    private bool _isRendering;

    public CheckoutShellForm(CheckoutPresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

        Text = "SALRYN POS Checkout";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 680);
        Size = new Size(1280, 720);
        Font = new Font("Segoe UI", 9F);
        BackColor = UiColors.AppBackground;

        BuildShell();
        WireEvents();

        Render(_presenter.Initialize().State);

        Shown += async (_, _) =>
        {
            CheckoutPresenterOperationResult readinessResult =
                await _presenter.RefreshOfficialModeReadinessAsync();

            Render(readinessResult.State);
        };
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

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

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

        _shiftContextLabel.AutoSize = true;
        _shiftContextLabel.Text = "Checkout workspace";
        _shiftContextLabel.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        _shiftContextLabel.ForeColor = UiColors.HeaderMuted;
        _shiftContextLabel.Location = new Point(23, 52);

        _checkoutModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _checkoutModeComboBox.Width = 250;
        _checkoutModeComboBox.Height = 28;
        _checkoutModeComboBox.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _checkoutModeComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _checkoutModeComboBox.Location = new Point(header.Width - _checkoutModeComboBox.Width - 20, 12);

        _modeLabel.AutoSize = false;
        _modeLabel.Width = 620;
        _modeLabel.Height = 26;
        _modeLabel.Text = "Repository-backed product search · Sale completion locked";
        _modeLabel.TextAlign = ContentAlignment.MiddleRight;
        _modeLabel.Font = new Font("Segoe UI", 8.8F, FontStyle.Bold);
        _modeLabel.ForeColor = UiColors.HeaderMuted;
        _modeLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _modeLabel.Location = new Point(header.Width - _modeLabel.Width - 20, 44);

        header.Resize += (_, _) =>
        {
            _checkoutModeComboBox.Location = new Point(header.Width - _checkoutModeComboBox.Width - 20, 12);
            _modeLabel.Location = new Point(header.Width - _modeLabel.Width - 20, 44);
        };

        header.Controls.Add(title);
        header.Controls.Add(_shiftContextLabel);
        header.Controls.Add(_checkoutModeComboBox);
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
            Padding = new Padding(0, 10, 0, 10),
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
        Panel viewport = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiColors.AppBackground
        };

        TableLayoutPanel right = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            BackColor = UiColors.AppBackground
        };

        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 230F));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 164F));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 420F));

        right.Controls.Add(CreateTotalsCard(), 0, 0);
        right.Controls.Add(CreatePaymentCard(), 0, 1);
        right.Controls.Add(CreateActionsCard(), 0, 2);

        viewport.Controls.Add(right);

        return viewport;
    }

    // Kirch layout section: product search card.
    // UI side: cashier scans/types barcode or product text here, then prepares one item before adding to cart.
    // Frontend note: this is the first workflow area to polish for fast checkout speed.
    private Control CreateProductSearchCard()
    {
        Panel card = CreateCard();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4,
            Padding = new Padding(14),
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
            Padding = new Padding(14),
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
            Padding = new Padding(14),
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
            Padding = new Padding(14),
            BackColor = Color.White
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));

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
            RowCount = 13,
            Padding = new Padding(14),
            BackColor = Color.White
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(CreateSectionTitle("Actions"), 0, 0);

        _receiptNumberLabel.Dock = DockStyle.Fill;
        _receiptNumberLabel.TextAlign = ContentAlignment.MiddleLeft;
        _receiptNumberLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _receiptNumberLabel.ForeColor = UiColors.TextMuted;
        _receiptNumberLabel.Text = "Receipt: not assigned";
        layout.Controls.Add(_receiptNumberLabel, 0, 1);

        ConfigureSecondaryButton(_dryRunCompletionButton, "Validate Completion Dry Run");
        ConfigureSecondaryButton(_prepareSubmitGuardButton, "Prepare Submit Guard");
        ConfigureLockedButton(_completeSaleButton, "Complete Sale Locked");
        ConfigureSecondaryButton(_removeItemButton, "Remove Selected Item");
        ConfigureSecondaryButton(_clearCartButton, "Clear Draft Cart");

        layout.Controls.Add(_dryRunCompletionButton, 0, 2);
        layout.Controls.Add(_prepareSubmitGuardButton, 0, 3);
        layout.Controls.Add(_completeSaleButton, 0, 4);

        _completionReadinessLabel.Dock = DockStyle.Fill;
        _completionReadinessLabel.TextAlign = ContentAlignment.MiddleLeft;
        _completionReadinessLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _completionReadinessLabel.ForeColor = UiColors.TextMuted;
        _completionReadinessLabel.Text = "Completion: not ready";
        layout.Controls.Add(_completionReadinessLabel, 0, 5);

        _completionDryRunLabel.Dock = DockStyle.Fill;
        _completionDryRunLabel.TextAlign = ContentAlignment.MiddleLeft;
        _completionDryRunLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _completionDryRunLabel.ForeColor = UiColors.TextMuted;
        _completionDryRunLabel.Text = "Dry run: not executed";
        layout.Controls.Add(_completionDryRunLabel, 0, 6);

        _saleSubmitGuardLabel.Dock = DockStyle.Fill;
        _saleSubmitGuardLabel.TextAlign = ContentAlignment.MiddleLeft;
        _saleSubmitGuardLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _saleSubmitGuardLabel.ForeColor = UiColors.TextMuted;
        _saleSubmitGuardLabel.Text = "Submit guard: not prepared";
        layout.Controls.Add(_saleSubmitGuardLabel, 0, 7);

        _saleSubmitResultLabel.Dock = DockStyle.Fill;
        _saleSubmitResultLabel.TextAlign = ContentAlignment.MiddleLeft;
        _saleSubmitResultLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _saleSubmitResultLabel.ForeColor = UiColors.TextMuted;
        _saleSubmitResultLabel.Text = "Submit result: not executed";
        layout.Controls.Add(_saleSubmitResultLabel, 0, 8);

        layout.Controls.Add(_removeItemButton, 0, 9);
        layout.Controls.Add(_clearCartButton, 0, 10);

        Label note = new()
        {
            Dock = DockStyle.Fill,
            Text = "Complete Sale stays guarded by dry run and submit guard. Scroll this side panel if using a short display.",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            ForeColor = UiColors.TextMuted
        };

        layout.Controls.Add(note, 0, 11);

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

        _checkoutModeComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_isRendering)
            {
                return;
            }

            CheckoutPresenterOperationResult result = _checkoutModeComboBox.SelectedIndex == 1
                ? _presenter.SelectOfficialMode()
                : _presenter.SelectStandardMode();

            Render(result.State);
        };

        _dryRunCompletionButton.Click += (_, _) =>
        {
            Render(_presenter.DryRunCompletionRequest().State);
        };

        _prepareSubmitGuardButton.Click += (_, _) =>
        {
            Render(_presenter.PrepareSaleSubmitGuard().State);
        };

        _completeSaleButton.Click += async (_, _) =>
        {
            CheckoutViewModel beforeSubmit = _presenter.State;

            DialogResult confirmation = ShowCompletionConfirmationDialog(beforeSubmit);

            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            _completeSaleButton.Enabled = false;
            _completeSaleButton.Text = "Completing Sale...";

            CheckoutPresenterOperationResult result =
                await _presenter.ExecutePreparedSaleSubmitAsync();

            Render(result.State);

            ShowCompletionResultDialog(result.State, result.IsSuccess);
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

    private DialogResult ShowCompletionConfirmationDialog(CheckoutViewModel state)
    {
        return ShowCheckoutMessageDialog(
            "Confirm Complete Sale",
            BuildCompletionConfirmationMessage(state),
            "Yes, Complete Sale",
            "No");
    }

    private void ShowCompletionResultDialog(CheckoutViewModel state, bool isSuccess)
    {
        ShowCheckoutMessageDialog(
            isSuccess ? "Sale Completed" : "Sale Not Completed",
            BuildCompletionResultMessage(state),
            "OK",
            secondaryButtonText: null);
    }

    private DialogResult ShowCheckoutMessageDialog(
        string title,
        string message,
        string primaryButtonText,
        string? secondaryButtonText)
    {
        using Form dialog = new()
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Size = new Size(560, 380),
            MinimumSize = new Size(560, 380),
            Font = new Font("Segoe UI", 9F),
            BackColor = UiColors.AppBackground
        };

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(18),
            BackColor = UiColors.AppBackground
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));

        Label titleLabel = new()
        {
            Dock = DockStyle.Fill,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = UiColors.TextStrong
        };

        TextBox messageBox = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = UiColors.TextStrong,
            Text = message,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular)
        };

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = UiColors.AppBackground
        };

        Button primaryButton = new()
        {
            Text = primaryButtonText,
            Width = secondaryButtonText is null ? 130 : 150,
            Height = 34,
            DialogResult = secondaryButtonText is null ? DialogResult.OK : DialogResult.Yes
        };

        buttons.Controls.Add(primaryButton);

        if (secondaryButtonText is not null)
        {
            Button secondaryButton = new()
            {
                Text = secondaryButtonText,
                Width = 110,
                Height = 34,
                DialogResult = DialogResult.No
            };

            buttons.Controls.Add(secondaryButton);
            dialog.CancelButton = secondaryButton;
        }
        else
        {
            dialog.CancelButton = primaryButton;
        }

        dialog.AcceptButton = primaryButton;

        root.Controls.Add(titleLabel, 0, 0);
        root.Controls.Add(messageBox, 0, 1);
        root.Controls.Add(buttons, 0, 2);

        dialog.Controls.Add(root);

        return dialog.ShowDialog(this);
    }

    private static string BuildCompletionConfirmationMessage(CheckoutViewModel state)
    {
        return string.Join(
            Environment.NewLine,
            "Complete this sale?",
            string.Empty,
            $"Receipt: {state.ReceiptNumber}",
            $"Items: {state.Lines.Count}",
            $"Total: {state.Totals.TotalText}",
            $"Paid: {state.Totals.AmountPaidText}",
            $"Change: {state.Totals.ChangeText}",
            string.Empty,
            "This will save the sale and lock this checkout draft.");
    }

    private static string BuildCompletionResultMessage(CheckoutViewModel state)
    {
        if (state.SaleSubmitResult.IsSuccess && state.SaleSubmitResult.IsCompleted)
        {
            return string.Join(
                Environment.NewLine,
                "Sale completed.",
                string.Empty,
                $"Receipt: {state.ReceiptNumber}",
                $"Items: {state.Lines.Count}",
                $"Total: {state.Totals.TotalText}",
                $"Paid: {state.Totals.AmountPaidText}",
                $"Change: {state.Totals.ChangeText}",
                string.Empty,
                "Use Start New Sale to reset the checkout workspace.");
        }

        return string.Join(
            Environment.NewLine,
            "Sale was not completed.",
            string.Empty,
            state.SaleSubmitResult.DetailText);
    }

    // Kirch render boundary: this method paints presenter state into controls.
    // UI side: all checkout labels/buttons/grids refresh from one CheckoutViewModel snapshot.
    // Do not put business rules here; change the presenter/view model instead.
    private void Render(CheckoutViewModel state)
    {
        _isRendering = true;

        try
        {
            if (_productInputTextBox.Text != state.ProductInputText)
            {
                _productInputTextBox.Text = state.ProductInputText;
            }

            RenderCheckoutModeSelector(state);
            _modeLabel.Text = state.CheckoutModeText;
            _modeLabel.ForeColor = state.OfficialMode.IsReady
                ? UiColors.HeaderSuccess
                : state.OfficialMode.HasCheckedReadiness
                    ? UiColors.HeaderWarning
                    : UiColors.HeaderMuted;
            _shiftContextLabel.Text = state.CashierSessionText;
            _receiptNumberLabel.Text = string.IsNullOrWhiteSpace(state.ReceiptNumber)
                ? "Receipt: not assigned"
                : $"Receipt: {state.ReceiptNumber}";
            _completionReadinessLabel.Text = $"Completion: {state.CompletionReadiness.SummaryText}";
            _completionReadinessLabel.ForeColor = state.CompletionReadiness.CanBuildCompletionRequest
                ? UiColors.Success
                : UiColors.TextMuted;
            _completionDryRunLabel.Text = $"Dry run: {state.CompletionDryRun.SummaryText}";
            _completionDryRunLabel.ForeColor = !state.CompletionDryRun.HasRun
                ? UiColors.TextMuted
                : state.CompletionDryRun.IsSuccess
                    ? UiColors.Success
                    : UiColors.Danger;
            _saleSubmitGuardLabel.Text = $"Submit guard: {state.SaleSubmitGuard.SummaryText}";
            _saleSubmitGuardLabel.ForeColor = state.SaleSubmitGuard.IsPrepared
                ? UiColors.Success
                : state.SaleSubmitGuard.ErrorCode is not null
                    ? UiColors.Danger
                    : UiColors.TextMuted;
            _saleSubmitResultLabel.Text = $"Submit result: {state.SaleSubmitResult.SummaryText}";
            _saleSubmitResultLabel.ForeColor = !state.SaleSubmitResult.HasRun
                ? UiColors.TextMuted
                : state.SaleSubmitResult.IsSuccess
                    ? UiColors.Success
                    : UiColors.Danger;

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

            bool saleCompleted = state.SaleSubmitResult.IsCompleted;

            _searchButton.Enabled = !saleCompleted && state.CanSearchProduct;
            _addItemButton.Enabled = !saleCompleted && state.CanAddPreparedProductLine;
            _removeItemButton.Enabled = !saleCompleted && state.Lines.Count > 0 && GetSelectedLineId().HasValue;
            _clearCartButton.Text = saleCompleted ? "Start New Sale" : "Clear Draft Cart";
            _clearCartButton.Enabled = saleCompleted || state.Lines.Count > 0;
            _payCashButton.Enabled = !saleCompleted && state.CanAddCashPayment;
            _dryRunCompletionButton.Enabled = !saleCompleted;
            _prepareSubmitGuardButton.Enabled =
                !saleCompleted &&
                state.CompletionDryRun.IsSuccess &&
                !state.SaleSubmitGuard.IsInProgress;
            _completeSaleButton.Text = state.SaleSubmitResult.IsCompleted
                ? "Sale Completed"
                : state.SaleSubmitGuard.IsInProgress
                    ? "Completing Sale..."
                    : state.CanCompleteSale
                        ? "Complete Sale"
                        : state.SaleSubmitGuard.CanExecuteSubmit
                            ? "Complete Sale Ready"
                            : "Complete Sale Locked";
            _completeSaleButton.BackColor = state.CanCompleteSale
                ? UiColors.Success
                : UiColors.Locked;
            _completeSaleButton.ForeColor = state.CanCompleteSale
                ? Color.White
                : UiColors.TextMuted;
            _completeSaleButton.Enabled = state.CanCompleteSale;

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

    private void RenderCheckoutModeSelector(CheckoutViewModel state)
    {
        string officialModeText = state.OfficialMode.CanSelectOfficialMode
            ? "Official output (ready)"
            : "Official output (locked)";

        if (_checkoutModeComboBox.Items.Count != 2 ||
            !string.Equals(Convert.ToString(_checkoutModeComboBox.Items[0]), "Standard POS sale", StringComparison.Ordinal) ||
            !string.Equals(Convert.ToString(_checkoutModeComboBox.Items[1]), officialModeText, StringComparison.Ordinal))
        {
            _checkoutModeComboBox.Items.Clear();
            _checkoutModeComboBox.Items.Add("Standard POS sale");
            _checkoutModeComboBox.Items.Add(officialModeText);
        }

        int selectedIndex = state.OfficialMode.IsOfficialModeSelected ? 1 : 0;

        if (_checkoutModeComboBox.SelectedIndex != selectedIndex)
        {
            _checkoutModeComboBox.SelectedIndex = selectedIndex;
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
        public static readonly Color HeaderSuccess = Color.FromArgb(187, 247, 208);
        public static readonly Color HeaderWarning = Color.FromArgb(254, 240, 138);
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
