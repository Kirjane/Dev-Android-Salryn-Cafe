using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Checkout;
using Salryn.App.WinForms.Features.Products;
using Salryn.App.WinForms.Features.Shifts;

namespace Salryn.App.WinForms.Features.Shell;

public sealed class SalrynShellForm : Form
{
    private readonly SalrynAppServices _services;
    private readonly CurrentAppSession? _session;
    private readonly ShellPresenter _presenter;
    private readonly ShellViewModel _state;
    private readonly Dictionary<string, Button> _moduleButtons = new(StringComparer.OrdinalIgnoreCase);

    private Label _titleLabel = null!;
    private Label _userLabel = null!;
    private Label _statusLabel = null!;
    private FlowLayoutPanel _navigationPanel = null!;
    private Panel _workspacePanel = null!;

    public SalrynShellForm(
        SalrynAppServices services,
        CurrentAppSession? session)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _session = session;
        _presenter = new ShellPresenter(session);
        _state = _presenter.Build();

        InitializeComponent();
        Render();
    }

    // Kirch: Builds the main application shell that routes users to modules based on permissions.
    private void InitializeComponent()
    {
        Text = "SALRYN";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1280, 780);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            BackColor = Color.FromArgb(246, 247, 249)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        Controls.Add(root);

        Panel header = BuildHeader();
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 12)
        };

        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(body, 0, 1);

        _navigationPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0),
            BackColor = Color.FromArgb(246, 247, 249)
        };

        body.Controls.Add(_navigationPanel, 0, 0);

        _workspacePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(24)
        };

        body.Controls.Add(_workspacePanel, 1, 0);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(90, 96, 106)
        };

        root.Controls.Add(_statusLabel, 0, 2);
    }

    private Panel BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(31, 41, 55),
            Padding = new Padding(22, 12, 22, 12)
        };

        _titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _userLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Color.FromArgb(209, 213, 219),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        };

        header.Controls.Add(_userLabel);
        header.Controls.Add(_titleLabel);

        return header;
    }

    private void Render()
    {
        _titleLabel.Text = _state.Title;
        _userLabel.Text = _state.UserLine;
        _statusLabel.Text = _state.StatusLine;

        _navigationPanel.Controls.Clear();
        _moduleButtons.Clear();

        foreach (ShellModule module in _state.Modules)
        {
            Button button = BuildModuleButton(module);
            _moduleButtons[module.Key] = button;
            _navigationPanel.Controls.Add(button);
        }

        ShellModule? firstEnabledModule = _state.Modules.FirstOrDefault(module => module.IsEnabled);

        if (firstEnabledModule is null)
        {
            ShowPlaceholder(
                "No available module",
                "No active user session or no permissions were loaded.",
                "The backend permission model exists, but login and first-run user selection are not implemented yet.");

            return;
        }

        ShowPlaceholder(
            firstEnabledModule.Title,
            firstEnabledModule.Description,
            "Select a module from the left. POS Checkout now checks for an open cashier shift before opening.");
    }

    private Button BuildModuleButton(ShellModule module)
    {
        var button = new Button
        {
            Width = 260,
            Height = 54,
            Margin = new Padding(0, 0, 0, 10),
            Text = module.IsEnabled
                ? module.Title
                : module.Title + " (Locked)",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 14, 0),
            Enabled = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = module.IsEnabled ? Color.White : Color.FromArgb(229, 231, 235),
            ForeColor = module.IsEnabled ? Color.FromArgb(17, 24, 39) : Color.FromArgb(107, 114, 128)
        };

        button.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        button.Click += (_, _) => ActivateModule(module.Key);

        return button;
    }

    private void ActivateModule(string moduleKey)
    {
        if (!_presenter.TryGetModule(moduleKey, out ShellModule module))
        {
            ShowPlaceholder(
                "Unknown module",
                "The selected module key was not found.",
                moduleKey);

            return;
        }

        if (!module.IsEnabled)
        {
            ShowPlaceholder(
                module.Title,
                module.Description,
                module.DisabledReason);

            return;
        }

        if (module.Key == ShellModuleKeys.PosCheckout)
        {
            OpenCheckout();
            return;
        }

        if (module.Key == ShellModuleKeys.Products)
        {
            OpenProducts();
            return;
        }

        ShowPlaceholder(
            module.Title,
            module.Description,
            "This module is permission-routed but its full workflow is not implemented in this phase.");
    }

    // Kirch: This opens product management with read/write/catalog services from the composition root.
    private void OpenProducts()
    {
        if (_session is null)
        {
            ShowPlaceholder(
                "Products",
                "Product management requires an authenticated user session.",
                "Login again before opening the product management workspace.");

            return;
        }

        using ProductManagementForm productForm =
            new ProductManagementForm(new ProductManagementPresenter(
                _services.ProductReadRepository,
                _services.ProductWriteRepository,
                _services.CatalogReadRepository));

        productForm.StartPosition = FormStartPosition.CenterParent;
        productForm.ShowDialog(this);
    }

    private void OpenCheckout()
    {
        if (_session is null)
        {
            ShowPlaceholder(
                "POS Checkout",
                "Checkout requires an authenticated user session.",
                "Login again before entering the POS checkout workspace.");

            return;
        }

        using CashierShiftGateForm shiftGateForm = new(
            new CashierShiftGatePresenter(
                _session,
                _services.CashierSessions));

        shiftGateForm.StartPosition = FormStartPosition.CenterParent;

        DialogResult shiftGateResult = shiftGateForm.ShowDialog(this);

        if (shiftGateResult != DialogResult.OK)
        {
            ShowPlaceholder(
                "POS Checkout",
                "Checkout was not opened.",
                "A cashier shift must be open before entering the POS checkout workspace.");

            return;
        }

        if (shiftGateForm.OpenSession is null)
        {
            ShowPlaceholder(
                "POS Checkout",
                "Checkout was not opened.",
                "Cashier shift gate did not return an open shift session.");

            return;
        }

        CheckoutSessionContext checkoutSessionContext =
            CheckoutSessionContext.From(
                _session,
                shiftGateForm.OpenSession);

        using CheckoutShellForm checkoutForm =
            new CheckoutShellForm(new CheckoutPresenter(
                _services.CheckoutProductSearch,
                checkoutSessionContext,
                receiptNumberBoundary: null,
                completionBoundary: new CheckoutCompletionBoundary(_services.PosCheckoutApplication)));

        checkoutForm.StartPosition = FormStartPosition.CenterParent;
        checkoutForm.ShowDialog(this);
    }

    private void ShowPlaceholder(
        string title,
        string description,
        string note)
    {
        _workspacePanel.Controls.Clear();

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };

        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var descriptionLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = description,
            Font = new Font(Font.FontFamily, 11, FontStyle.Regular),
            ForeColor = Color.FromArgb(75, 85, 99),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var noteLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = note,
            Font = new Font(Font.FontFamily, 10, FontStyle.Regular),
            ForeColor = Color.FromArgb(107, 114, 128),
            TextAlign = ContentAlignment.TopLeft,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14)
        };

        var footerLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Phase 45: App shell and permission-based mode routing. Backend sale completion, inventory writes, and admin workflows remain untouched.",
            ForeColor = Color.FromArgb(107, 114, 128),
            TextAlign = ContentAlignment.BottomLeft
        };

        content.Controls.Add(titleLabel, 0, 0);
        content.Controls.Add(descriptionLabel, 0, 1);
        content.Controls.Add(noteLabel, 0, 2);
        content.Controls.Add(footerLabel, 0, 3);

        _workspacePanel.Controls.Add(content);
    }
}








