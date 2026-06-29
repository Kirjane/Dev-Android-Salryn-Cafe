using Salryn.App.WinForms.Features.Mvp;

namespace Salryn.App.WinForms.Features.Products;

// Kirch file guide: add product modal form.
// UI location: opened from the Products screen Add Product button.
// Frontend note: controls map to ProductCreateRequest; preserve required fields and presenter save flow.

// Kirch: This form collects new product input and delegates validation/saving to the presenter.
public sealed class ProductCreateForm : Form
{
    private readonly ProductManagementPresenter _presenter;

    // Kirch control group: Add Product form fields.
    // UI side: these controls map directly to ProductCreateRequest fields.
    // Frontend safe zone: grouping, labels, helper text, tab order, spacing, and validation display.
    // Do not change: required field meaning, catalog dropdown binding, or presenter save flow.
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

    public ProductCreateForm(ProductManagementPresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

        InitializeComponent();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        await LoadCatalogsAsync();
    }

    // Kirch: This builds the Add Product UI in code so the screen stays version-control friendly.
    private void InitializeComponent()
    {
        Text = "Add Product";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 760);
        Size = new Size(780, 820);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        Controls.Add(root);

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Add Product",
            Font = SalrynUi.Typography.Title,
            ForeColor = SalrynUi.Colors.TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        };

        root.Controls.Add(titleLabel, 0, 0);

        var formPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 16,
            AutoScroll = true,
            BackColor = SalrynUi.Colors.Surface,
            Padding = new Padding(14)
        };

        formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int i = 0; i < 16; i++)
        {
            formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, i is 3 or 15 ? 82 : 42));
        }

        root.Controls.Add(formPanel, 0, 1);

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
        _reorderLevelTextBox = BuildTextBox();

        _trackStockCheckBox = new CheckBox
        {
            Dock = DockStyle.Fill,
            Text = "Track stock for this product",
            Checked = true,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _allowDiscountCheckBox = new CheckBox
        {
            Dock = DockStyle.Fill,
            Text = "Allow discount for this product",
            Checked = true,
            TextAlign = ContentAlignment.MiddleLeft
        };

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
        AddRow(formPanel, 11, "Stock On Hand", _stockOnHandTextBox);
        AddRow(formPanel, 12, "Reorder Level", _reorderLevelTextBox);
        AddRow(formPanel, 13, "Stock Tracking", _trackStockCheckBox);
        AddRow(formPanel, 14, "Discount", _allowDiscountCheckBox);
        AddRow(formPanel, 15, "Notes", _notesTextBox);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        SalrynUi.SetStatusNeutral(_statusLabel, string.Empty);

        root.Controls.Add(_statusLabel, 0, 2);

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };

        _saveButton = new Button { Width = 120, Height = 38 };
        SalrynUi.ConfigurePrimaryButton(_saveButton, "Save");
        _saveButton.Click += async (_, _) => await SaveAsync();

        _cancelButton = new Button { Width = 120, Height = 38 };
        SalrynUi.ConfigureSecondaryButton(_cancelButton, "Cancel");

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

        _sellingPriceTextBox.Text = "0";
        _stockOnHandTextBox.Text = "0";
        _reorderLevelTextBox.Text = "0";
    }

    // Kirch: This fills unit/category/brand/supplier dropdowns before the user saves a product.
    private async Task LoadCatalogsAsync()
    {
        SetBusy(true, "Loading catalog options...");

        ProductCreateFormViewModel state = await _presenter.LoadCreateFormAsync();

        if (state.HasError)
        {
            SetBusy(false, $"{state.ErrorCode}: {state.ErrorMessage}");
            _saveButton.Enabled = false;
            return;
        }

        BindComboBox(_unitComboBox, state.Units);
        BindComboBox(_categoryComboBox, state.Categories);
        BindComboBox(_brandComboBox, state.Brands);
        BindComboBox(_supplierComboBox, state.Suppliers);

        SetBusy(false, state.StatusMessage);
    }

    // Kirch: This packages form values into a request and closes only after a successful save.
    // Kirch workflow: Add Product save action.
    // UI side: Save button gathers raw field text and delegates validation/create to ProductManagementPresenter.
    // Frontend note: keep this thin; do not move parsing or repository calls into the form.
    private async Task SaveAsync()
    {
        ProductCreateRequest request = new(
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
            StockOnHand: _stockOnHandTextBox.Text,
            ReorderLevel: _reorderLevelTextBox.Text,
            TrackStock: _trackStockCheckBox.Checked,
            AllowDiscount: _allowDiscountCheckBox.Checked,
            Notes: _notesTextBox.Text);

        SetBusy(true, "Saving product...");

        ProductCreateOperationResult result =
            await _presenter.CreateProductAsync(request);

        if (!result.IsSuccess)
        {
            SetBusy(false, $"{result.ErrorCode}: {result.ErrorMessage}");
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
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

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static Guid? SelectedOptionId(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ProductCatalogOptionViewModel option
            ? option.Id
            : null;
    }

    // Kirch: This keeps Save/Cancel locked during async work and shows the current form status.
    private void SetBusy(bool isBusy, string message)
    {
        _saveButton.Enabled = !isBusy;
        _cancelButton.Enabled = !isBusy;
        _statusLabel.Text = message;
    }
}
