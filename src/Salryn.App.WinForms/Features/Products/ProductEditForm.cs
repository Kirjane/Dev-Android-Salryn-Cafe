using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.Models;

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
    private ComboBox _taxClassificationComboBox = null!;
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
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        Controls.Add(root);

        // P1C: stronger header
        var header = SalrynUi.CreatePageHeader("Edit Product", "Update catalog details. Stock is managed via Inventory.");
        root.Controls.Add(header, 0, 0);

        var formPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 18,
            AutoScroll = true,
            BackColor = SalrynUi.Colors.Surface,
            Padding = new Padding(14)
        };

        formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int i = 0; i < 18; i++)
        {
            formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, i is 3 or 17 ? 82 : 42));
        }

        // Section title inside the card area
        var section = SalrynUi.CreateSectionTitle("Product Details");
        formPanel.Controls.Add(section, 0, 0);
        formPanel.SetColumnSpan(section, 2);

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
        _taxClassificationComboBox = BuildComboBox();
        _notesTextBox = BuildMultilineTextBox();

        AddRow(formPanel, 1, "SKU", _skuTextBox);
        AddRow(formPanel, 2, "Barcode", _barcodeTextBox);
        AddRow(formPanel, 3, "Name *", _nameTextBox);
        AddRow(formPanel, 4, "Description", _descriptionTextBox);
        AddRow(formPanel, 5, "Unit *", _unitComboBox);
        AddRow(formPanel, 6, "Category", _categoryComboBox);
        AddRow(formPanel, 7, "Brand", _brandComboBox);
        AddRow(formPanel, 8, "Supplier", _supplierComboBox);
        AddRow(formPanel, 9, "Selling Price *", _sellingPriceTextBox);
        AddRow(formPanel, 10, "Cost Price", _costPriceTextBox);
        AddRow(formPanel, 11, "Wholesale Price", _wholesalePriceTextBox);
        // P1C: stock host with helper text under (read-only remains enforced)
        var stockHost = new Panel { Dock = DockStyle.Fill };
        _stockOnHandTextBox.Dock = DockStyle.Top;
        _stockOnHandTextBox.Height = SalrynUi.Spacing.FieldHeight;
        stockHost.Controls.Add(_stockOnHandTextBox);
        var stockNote = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 18,
            Text = "Stock is adjusted from Inventory, not Product Edit.",
            Font = SalrynUi.Typography.Small,
            ForeColor = SalrynUi.Colors.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        stockHost.Controls.Add(stockNote);
        AddRow(formPanel, 12, "Stock On Hand (read-only)", stockHost);
        AddRow(formPanel, 13, "Reorder Level", _reorderLevelTextBox);
        AddRow(formPanel, 14, "Stock Tracking", _trackStockCheckBox);
        AddRow(formPanel, 15, "Discount", _allowDiscountCheckBox);
        AddRow(formPanel, 16, "Tax Classification *", _taxClassificationComboBox);
        AddRow(formPanel, 17, "Notes", _notesTextBox);

        root.Controls.Add(formPanel, 0, 1);

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
        SalrynUi.StyleFooterStrip(buttonsPanel);

        _saveButton = BuildButton("Save", Color.FromArgb(31, 41, 55), Color.White);
        SalrynUi.ConfigurePrimaryButton(_saveButton, "Save");
        _cancelButton = BuildButton("Cancel", Color.White, Color.FromArgb(17, 24, 39));
        SalrynUi.ConfigureSecondaryButton(_cancelButton, "Cancel");

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
            string err = state.ErrorCode + ": " + state.ErrorMessage;
            SetBusy(false, err);
            SalrynUi.SetStatusError(_statusLabel, err);
            _saveButton.Enabled = false;
            return;
        }

        _createdAt = state.CreatedAt;
        _isActive = state.IsActive;

        BindComboBox(_unitComboBox, state.Units);
        BindComboBox(_categoryComboBox, state.Categories);
        BindComboBox(_brandComboBox, state.Brands);
        BindComboBox(_supplierComboBox, state.Suppliers);
        BindTaxClassificationCombo(_taxClassificationComboBox);
        SelectTaxClassification(_taxClassificationComboBox, state.TaxClassification);

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
            TaxClassification: SelectedTaxClassification(_taxClassificationComboBox),
            Notes: _notesTextBox.Text,
            CreatedAt: _createdAt);

        SetBusy(true, "Saving product changes...");

        ProductEditOperationResult result = await _presenter.UpdateProductAsync(request).ConfigureAwait(true);

        if (!result.IsSuccess)
        {
            string err = result.ErrorCode + ": " + result.ErrorMessage;
            SetBusy(false, err);
            SalrynUi.SetStatusError(_statusLabel, err);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetBusy(bool isBusy, string message)
    {
        _saveButton.Enabled = !isBusy && _isActive;
        _cancelButton.Enabled = !isBusy;
        SalrynUi.SetStatusNeutral(_statusLabel, message);
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
        bool required = labelText.Contains('*');
        var label = SalrynUi.CreateFieldLabel(labelText.Replace(" *", ""), required);

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

    private static void BindTaxClassificationCombo(ComboBox comboBox)
    {
        comboBox.DataSource = ProductTaxClassificationMapper.GetDisplayOptions()
            .Select(option => new ProductTaxClassificationOptionViewModel(option.Value, option.DisplayText))
            .ToList();
        comboBox.DisplayMember = nameof(ProductTaxClassificationOptionViewModel.DisplayText);
        comboBox.SelectedIndex = 0;
    }

    private static ProductTaxClassification SelectedTaxClassification(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ProductTaxClassificationOptionViewModel option
            ? option.Value
            : ProductTaxClassification.Vatable;
    }

    private static void SelectTaxClassification(ComboBox comboBox, ProductTaxClassification classification)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ProductTaxClassificationOptionViewModel option && option.Value == classification)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }
}
