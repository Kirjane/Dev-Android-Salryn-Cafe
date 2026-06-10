using System.Drawing;
using System.Windows.Forms;

namespace Salryn.App.WinForms.Features.Checkout;

public sealed class CheckoutShellForm : Form
{
    private readonly Label _statusLabel = new();

    public CheckoutShellForm()
    {
        Text = "SALRYN POS Checkout - UI Shell";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1280, 780);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        BuildShell();
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122F));
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

        Label subtitle = new()
        {
            AutoSize = true,
            Text = "UI shell only — product search, cart actions, payment, and sale completion are not wired yet.",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(210, 220, 230),
            Location = new Point(21, 48)
        };

        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);

        return panel;
    }

    private Control CreateProductInputSection()
    {
        GroupBox group = CreateGroupBox("Product Scan / Search Placeholder");

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2,
            Padding = new Padding(12)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

        Label inputLabel = CreateFieldLabel("Barcode / Product:");
        TextBox input = new()
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Scan barcode or type product name later...",
            Enabled = false
        };

        Button scanButton = CreateDisabledButton("Scan / Search");
        Button addButton = CreateDisabledButton("Add Item");

        Label resultLabel = new()
        {
            Dock = DockStyle.Fill,
            Text = "Product result placeholder: product lookup is not wired in Phase 39.",
            ForeColor = Color.FromArgb(90, 100, 115),
            TextAlign = ContentAlignment.MiddleLeft
        };

        layout.Controls.Add(inputLabel, 0, 0);
        layout.Controls.Add(input, 1, 0);
        layout.Controls.Add(scanButton, 2, 0);
        layout.Controls.Add(addButton, 3, 0);

        layout.Controls.Add(CreateFieldLabel("Result:"), 0, 1);
        layout.Controls.Add(resultLabel, 1, 1);
        layout.SetColumnSpan(resultLabel, 4);

        group.Controls.Add(layout);

        return group;
    }

    private Control CreateCartSection()
    {
        GroupBox group = CreateGroupBox("Draft Cart Placeholder");

        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductColumn", HeaderText = "Product" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "BarcodeColumn", HeaderText = "Barcode" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QtyColumn", HeaderText = "Qty" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitColumn", HeaderText = "Unit" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PriceColumn", HeaderText = "Price" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DiscountColumn", HeaderText = "Discount" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TotalColumn", HeaderText = "Line Total" });

        group.Controls.Add(grid);

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
        GroupBox group = CreateGroupBox("Totals Placeholder");

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

        AddTotalRow(totals, 0, "Subtotal", "PHP 0.00", false);
        AddTotalRow(totals, 1, "Discount", "PHP 0.00", false);
        AddTotalRow(totals, 2, "Tax", "PHP 0.00", false);
        AddTotalRow(totals, 3, "Total", "PHP 0.00", true);
        AddTotalRow(totals, 4, "Paid", "PHP 0.00", false);
        AddTotalRow(totals, 5, "Balance", "PHP 0.00", true);

        group.Controls.Add(totals);

        return group;
    }

    private Control CreatePaymentSection()
    {
        GroupBox group = CreateGroupBox("Payment Placeholder");

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

        TextBox amount = new()
        {
            Dock = DockStyle.Fill,
            Enabled = false,
            PlaceholderText = "0.00"
        };

        Button payCash = CreateDisabledButton("Pay Cash");

        layout.Controls.Add(CreateFieldLabel("Method:"), 0, 0);
        layout.Controls.Add(method, 1, 0);
        layout.Controls.Add(CreateFieldLabel("Amount:"), 0, 1);
        layout.Controls.Add(amount, 1, 1);
        layout.Controls.Add(payCash, 1, 2);

        group.Controls.Add(layout);

        return group;
    }

    private Control CreateActionsSection()
    {
        GroupBox group = CreateGroupBox("Checkout Actions Placeholder");

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12)
        };

        actions.Controls.Add(CreateWideDisabledButton("Remove Item"));
        actions.Controls.Add(CreateWideDisabledButton("Clear Cart"));
        actions.Controls.Add(CreateWideDisabledButton("Hold Transaction"));
        actions.Controls.Add(CreateWideDisabledButton("Complete Sale"));

        Label note = new()
        {
            AutoSize = false,
            Width = 260,
            Height = 70,
            Text = "Actions are intentionally disabled in Phase 39. Checkout completion remains gated for later phases.",
            ForeColor = Color.FromArgb(90, 100, 115)
        };

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
        _statusLabel.Text = "Engine ready. UI shell only. No product lookup, cart mutation, payment, sale completion, printing, or stock movement is wired yet.";
        _statusLabel.ForeColor = Color.FromArgb(45, 58, 75);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        panel.Controls.Add(_statusLabel);

        return panel;
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

    private static Button CreateDisabledButton(string text)
    {
        return new Button
        {
            Dock = DockStyle.Fill,
            Text = text,
            Enabled = false,
            UseVisualStyleBackColor = true
        };
    }

    private static Button CreateWideDisabledButton(string text)
    {
        return new Button
        {
            Width = 260,
            Height = 38,
            Text = text,
            Enabled = false,
            UseVisualStyleBackColor = true,
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private static void AddTotalRow(
        TableLayoutPanel table,
        int row,
        string label,
        string value,
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

        Label valueControl = new()
        {
            Dock = DockStyle.Fill,
            Text = value,
            Font = font,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = emphasized
                ? Color.FromArgb(20, 31, 46)
                : Color.FromArgb(55, 65, 80)
        };

        table.Controls.Add(labelControl, 0, row);
        table.Controls.Add(valueControl, 1, row);
    }
}

