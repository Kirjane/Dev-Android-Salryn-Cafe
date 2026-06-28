namespace Salryn.App.WinForms.Features.Products;

// Kirch file guide: edit product modal form.
// UI location: opened from the Products screen Edit Product action.
// Frontend note: keep field collection thin; ProductManagementPresenter owns validation and repository writes.

public sealed class ProductEditForm : Form
{
    private readonly ProductManagementPresenter _presenter;
    private readonly Guid _productId;

    private TextBox _skuTextBox = null!;
    private TextBox _barcodeTextBox = null!;
    private TextBox _nameTextBox = null!;
    private TextBox _descriptionTextBox = null!;
    private ComboBox _unitComboBox = null!;
    private ComboBox _categoryComboBox = null!;
    private ComboBox _brandComboBox = null!;
    private ComboBox _supplierComboBox = null!;
    private TextBox _sellingPriceTextBox = null!;
    private TextBox _costPriceTextBox = null!;
    private TextBox _wholesalePriceTextBox = null!;
    private TextBox _stockOnHandTextBox = null!;
    private TextBox _reorderLevelTextBox = null!;
    private CheckBox _trackStockCheckBox = null!;
    private CheckBox _allowDiscountCheckBox = null!;
    private TextBox _notesTextBox = null!;
    private Label _statusLabel = null!;
    private Button _saveButton = null!;
    private Button _cancelButton = null!;
    private DateTimeOffset _createdAt;
    private bool _isActive;

    public ProductEditForm(
        ProductManagementPresenter presenter,
        Guid productId)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _productId = productId;

        InitializeComponent();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadProductAsync().ConfigureAwait(true);
    }

    private void InitializeComponent()
    {
        Text = "Edit Product";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 780);
        Size = new Size(780, 840);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(246, 247, 249)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Edit Product",
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var formPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 16,
            AutoScroll = true,
            BackColor = Color.White,
            Padding = new Padding(14)
        };

        formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int i = 0; i < 16; i++)
        {
            formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, i is 3 or 15 ? 82 : 42));
        }

        _skuTextBox = BuildTextBox();
        _barcodeTextBox = BuildTextBox();
        _nameTextBox = BuildTextBox();
        _descriptionTextBox = BuildMultilineTextBox();
        _unitComboBox = BuildComboBox();
        _categoryComboBox = BuildComboBox();
        _brandComboBox = BuildComboBox();
        _supplierComboBox = BuildComboBox();
        _sellingPriceTextBox = BuildTextBox();
        _costPriceTextBox = BuildTextBox();
        _wholesalePriceTextBox = BuildTextBox();
        _stockOnHandTextBox = BuildTextBox();
        _stockOnHandTextBox.ReadOnly = true;
        _stockOnHandTextBox.BackColor = Color.FromArgb(243, 244, 246);
        _reorderLevelTextBox = BuildTextBox();
        _trackStockCheckBox = new CheckBox { Dock = DockStyle.Fill, Text = "Track stock for this product" };
        _allowDiscountCheckBox = new CheckBox { Dock = DockStyle.Fill, Text = "Allow discount for this product" };
        _notesTextBox = BuildMultilineTextBox();

        AddRow(formPanel, 0, "SKU", _skuTextBox);
        AddRow(formPanel, 1, "Barcode", _barcodeTextBox);
        AddRow(formPanel, 2, "Name *", _nameTextBox);
        AddRow(formPanel, 3, "Description", _descriptionTextBox);
        AddRow(formPanel, 4, "Unit *", _unitComboBox);
        AddRow(formPanel, 5, "Category", _categoryComboBox);
        AddRow(formPanel, 6, "Brand", _brandComboBox);
        AddRow(formPanel, 7, "Supplier", _supplierComboBox);
        AddRow(formPanel, 8, "Selling Price *", _sellingPriceTextBox);
        AddRow(formPanel, 9, "Cost Price", _costPriceTextBox);
        AddRow(formPanel, 10, "Wholesale Price", _wholesalePriceTextBox);
        AddRow(formPanel, 11, "Stock On Hand (read-only)", _stockOnHandTextBox);
        AddRow(formPanel, 12, "Reorder Level", _reorderLevelTextBox);
        AddRow(formPanel, 13, "Stock Tracking", _trackStockCheckBox);
        AddRow(formPanel, 14, "Discount", _allowDiscountCheckBox);
        AddRow(formPanel, 15, "Notes", _notesTextBox);

        root.Controls.Add(formPanel, 0, 1);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(75, 85, 99)
        };

        root.Controls.Add(_statusLabel, 0, 2);

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };

        _saveButton = BuildButton("Save", Color.FromArgb(31, 41, 55), Color.White);
        _cancelButton = BuildButton("Cancel", Color.White, Color.FromArgb(17, 24, 39));

        _saveButton.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        buttonsPanel.Controls.Add(_saveButton);
        buttonsPanel.Controls.Add(_cancelButton);
        root.Controls.Add(buttonsPanel, 0, 3);

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private async Task LoadProductAsync()
    {
        SetBusy(true, "Loading product...");

        ProductEditFormViewModel state = await _presenter.LoadEditFormAsync(_productId).ConfigureAwait(true);

        if (state.HasError)
        {
            SetBusy(false, state.ErrorCode + ": " + state.ErrorMessage);
            _saveButton.Enabled = false;
            return;
        }

        _createdAt = state.CreatedAt;
        _isActive = state.IsActive;

        BindComboBox(_unitComboBox, state.Units);
        BindComboBox(_categoryComboBox, state.Categories);
        BindComboBox(_brandComboBox, state.Brands);
        BindComboBox(_supplierComboBox, state.Suppliers);

        SelectOption(_unitComboBox, state.UnitId);
        SelectOption(_categoryComboBox, state.CategoryId);
        SelectOption(_brandComboBox, state.BrandId);
        SelectOption(_supplierComboBox, state.SupplierId);

        _skuTextBox.Text = state.Sku ?? string.Empty;
        _barcodeTextBox.Text = state.Barcode ?? string.Empty;
        _nameTextBox.Text = state.Name;
        _descriptionTextBox.Text = state.Description ?? string.Empty;
        _sellingPriceTextBox.Text = state.SellingPrice;
        _costPriceTextBox.Text = state.CostPrice ?? string.Empty;
        _wholesalePriceTextBox.Text = state.WholesalePrice ?? string.Empty;
        _stockOnHandTextBox.Text = state.StockOnHand;
        _reorderLevelTextBox.Text = state.ReorderLevel;
        _trackStockCheckBox.Checked = state.TrackStock;
        _allowDiscountCheckBox.Checked = state.AllowDiscount;
        _notesTextBox.Text = state.Notes ?? string.Empty;

        SetBusy(false, state.IsActive ? state.StatusMessage : "Product is inactive. Editing is disabled.");
        _saveButton.Enabled = state.IsActive;
    }

    private async Task SaveAsync()
    {
        ProductEditRequest request = new(
            ProductId: _productId,
            Sku: _skuTextBox.Text,
            Barcode: _barcodeTextBox.Text,
            Name: _nameTextBox.Text,
            Description: _descriptionTextBox.Text,
            CategoryId: SelectedOptionId(_categoryComboBox),
            BrandId: SelectedOptionId(_brandComboBox),
            UnitId: SelectedOptionId(_unitComboBox) ?? Guid.Empty,
            SupplierId: SelectedOptionId(_supplierComboBox),
            CostPrice: _costPriceTextBox.Text,
            SellingPrice: _sellingPriceTextBox.Text,
            WholesalePrice: _wholesalePriceTextBox.Text,
            StockOnHand: null,
            ReorderLevel: _reorderLevelTextBox.Text,
            TrackStock: _trackStockCheckBox.Checked,
            AllowDiscount: _allowDiscountCheckBox.Checked,
            Notes: _notesTextBox.Text,
            CreatedAt: _createdAt);

        SetBusy(true, "Saving product changes...");

        ProductEditOperationResult result = await _presenter.UpdateProductAsync(request).ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            SetBusy(false, result.ErrorCode + ": " + result.ErrorMessage);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetBusy(bool isBusy, string message)
    {
        _saveButton.Enabled = !isBusy && _isActive;
        _cancelButton.Enabled = !isBusy;
        _statusLabel.Text = message;
    }

    private static TextBox BuildTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10)
        };
    }

    private static TextBox BuildMultilineTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };
    }

    private static ComboBox BuildComboBox()
    {
        return new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10)
        };
    }

    private static Button BuildButton(string text, Color backColor, Color foreColor)
    {
        return new Button
        {
            Width = 120,
            Height = 38,
            Text = text,
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat
        };
    }

    private static void AddRow(
        TableLayoutPanel panel,
        int row,
        string labelText,
        Control control)
    {
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = labelText,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(55, 65, 81)
        };

        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private static void BindComboBox(
        ComboBox comboBox,
        IReadOnlyList<ProductCatalogOptionViewModel> options)
    {
        comboBox.DataSource = options.ToList();
        comboBox.DisplayMember = nameof(ProductCatalogOptionViewModel.DisplayText);
        comboBox.ValueMember = nameof(ProductCatalogOptionViewModel.Id);
    }

    private static Guid? SelectedOptionId(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ProductCatalogOptionViewModel option
            ? option.Id
            : null;
    }

    private static void SelectOption(ComboBox comboBox, Guid? id)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ProductCatalogOptionViewModel option && option.Id == id)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }
}
