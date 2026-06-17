namespace Salryn.App.WinForms.Features.Products;

// Kirch file guide: product catalog list screen.
// UI location: opened from Shell > Products.
// Frontend note: safe to restyle toolbar/grid/status here; do not construct repositories in the form.

public sealed class ProductManagementForm : Form
{
    private readonly ProductManagementPresenter _presenter;

    private TextBox _searchTextBox = null!;
    private Button _searchButton = null!;
    private Button _refreshButton = null!;
    private Button _addButton = null!;
    private DataGridView _productsGrid = null!;
    private Label _statusLabel = null!;

    public ProductManagementForm(ProductManagementPresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

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
        MinimumSize = new Size(1100, 680);
        Size = new Size(1200, 720);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(246, 247, 249)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        Controls.Add(root);

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Products",
            Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
            TextAlign = ContentAlignment.MiddleLeft
        };

        root.Controls.Add(titleLabel, 0, 0);

        var searchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1
        };

        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
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

        _searchButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Search",
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            BackColor = Color.FromArgb(31, 41, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };

        _searchButton.Click += async (_, _) => await SearchProductsAsync();

        _refreshButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Refresh",
            Font = new Font(Font.FontFamily, 10, FontStyle.Regular),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(17, 24, 39),
            FlatStyle = FlatStyle.Flat
        };

        _refreshButton.Click += async (_, _) => await LoadProductsAsync();

        _addButton = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Add Product",
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            BackColor = Color.FromArgb(22, 101, 52),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };

        _addButton.Click += async (_, _) => await OpenCreateFormAsync();

        searchPanel.Controls.Add(_searchTextBox, 0, 0);
        searchPanel.Controls.Add(_searchButton, 1, 0);
        searchPanel.Controls.Add(_refreshButton, 2, 0);
        searchPanel.Controls.Add(_addButton, 3, 0);

        root.Controls.Add(searchPanel, 0, 1);

        _productsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            EditMode = DataGridViewEditMode.EditProgrammatically,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
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

        root.Controls.Add(_productsGrid, 0, 2);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(75, 85, 99)
        };

        root.Controls.Add(_statusLabel, 0, 3);
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

        ProductManagementOperationResult refreshResult =
            await _presenter.SearchAsync(_searchTextBox.Text);

        Render(refreshResult.State);
    }

    private void Render(ProductManagementViewModel state)
    {
        _searchButton.Enabled = state.CanSearch;
        _refreshButton.Enabled = state.CanSearch;
        _addButton.Enabled = state.CanSearch;
        _searchTextBox.Enabled = state.CanSearch;

        _productsGrid.DataSource = null;
        _productsGrid.DataSource = state.Products.ToList();

        _statusLabel.Text = state.HasError
            ? $"{state.StatusMessage} {state.ErrorCode}: {state.ErrorMessage}"
            : state.StatusMessage;

        _statusLabel.ForeColor = state.HasError
            ? Color.FromArgb(185, 28, 28)
            : Color.FromArgb(75, 85, 99);
    }
}
