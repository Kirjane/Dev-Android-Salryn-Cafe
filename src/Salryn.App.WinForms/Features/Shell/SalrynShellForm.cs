using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Audit;
using Salryn.App.WinForms.Features.Backup;
using Salryn.App.WinForms.Features.Checkout;
using Salryn.App.WinForms.Features.Dashboard;
using Salryn.App.WinForms.Features.Inventory;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.App.WinForms.Features.OfficialOutput;
using Salryn.App.WinForms.Features.Products;
using Salryn.App.WinForms.Features.Receipt;
using Salryn.App.WinForms.Features.Sales;
using Salryn.App.WinForms.Features.Settings;
using Salryn.Core.Security;

namespace Salryn.App.WinForms.Features.Shell;

// Kirch file guide: Main authenticated shell form.
// UI location: after login, all left-nav module clicks route through this form.
// Frontend note: nav styling and workspace placeholder rendering live here; permissions come from ShellPresenter.

// Kirch: Authenticated shell that routes visible modules through ShellPresenter permissions.
public sealed class SalrynShellForm : Form
{
    private readonly SalrynAppServices _services;
    private readonly CurrentAppSession _session;
    private readonly ShellPresenter _presenter;

    private readonly Panel _navPanel = new();
    private readonly Panel _contentPanel = new();
    private readonly Label _titleLabel = new();
    private readonly Label _descriptionLabel = new();
    private readonly Label _detailLabel = new();

    public SalrynShellForm(
        SalrynAppServices services,
        CurrentAppSession session)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _presenter = new ShellPresenter(session);

        InitializeComponent();
        LoadModules();
    }

    private void InitializeComponent()
    {
        Text = "SALRYN";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1320, 780);
        BackColor = Color.FromArgb(246, 247, 249);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _navPanel.Dock = DockStyle.Fill;
        _navPanel.BackColor = Color.FromArgb(31, 41, 55);
        _navPanel.Padding = new Padding(16);

        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.BackColor = Color.FromArgb(246, 247, 249);
        _contentPanel.Padding = new Padding(36);

        root.Controls.Add(_navPanel, 0, 0);
        root.Controls.Add(_contentPanel, 1, 0);

        Controls.Add(root);
    }

    private void LoadModules()
    {
        _navPanel.Controls.Clear();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.FromArgb(31, 41, 55)
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));

        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = "SALRYN\nLocal POS",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var modulesPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.FromArgb(31, 41, 55)
        };

        foreach (ShellModule module in _presenter.GetModules())
        {
            modulesPanel.Controls.Add(CreateModuleButton(module));
        }

        var footer = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"Signed in as\n{_session.DisplayName}\n{_session.RoleName}",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(209, 213, 219),
            TextAlign = ContentAlignment.MiddleLeft
        };

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(modulesPanel, 0, 1);
        layout.Controls.Add(footer, 0, 2);

        _navPanel.Controls.Add(layout);

        ActivateModule(ShellModuleKeys.PosCheckout);
    }

    private Button CreateModuleButton(ShellModule module)
    {
        var button = new Button
        {
            Width = 230,
            Height = 54,
            Margin = new Padding(0, 0, 0, 10),
            Text = module.Title,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 12, 0),
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            Enabled = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = module.IsEnabled ? Color.White : Color.FromArgb(229, 231, 235),
            ForeColor = module.IsEnabled ? Color.FromArgb(17, 24, 39) : Color.FromArgb(107, 114, 128)
        };

        button.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        button.Click += (_, _) => ActivateModule(module.Key);

        return button;
    }

    // Kirch workflow: all left-nav clicks enter here before opening a module.
    // UI side: this controls whether the workspace opens a real screen or a locked/placeholder state.
    // Do not bypass: ShellPresenter remains the source of module permission/access decisions.
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

        if (module.Key == ShellModuleKeys.Inventory)
        {
            OpenInventory();
            return;
        }

        if (module.Key == ShellModuleKeys.SalesHistory)
        {
            OpenSalesHistory();
            return;
        }

        if (module.Key == ShellModuleKeys.Reports)
        {
            OpenDashboard();
            return;
        }

        if (module.Key == ShellModuleKeys.OfficialOutputTextFile)
        {
            OpenOfficialOutputTextFile();
            return;
        }

        if (module.Key == ShellModuleKeys.Settings)
        {
            OpenSettings();
            return;
        }

        if (module.Key == ShellModuleKeys.Backup)
        {
            OpenBackup();
            return;
        }

        if (module.Key == ShellModuleKeys.Audit)
        {
            OpenAudit();
            return;
        }

        ShowPlaceholder(
            module.Title,
            module.Description,
            "Module route is not implemented yet.");
    }

    private void ShowPlaceholder(
        string title,
        string description,
        string? detail)
    {
        _contentPanel.Controls.Clear();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.FromArgb(246, 247, 249)
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _titleLabel.Text = title;
        _titleLabel.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
        _titleLabel.ForeColor = Color.FromArgb(17, 24, 39);
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _titleLabel.Dock = DockStyle.Fill;

        _descriptionLabel.Text = description;
        _descriptionLabel.Font = new Font("Segoe UI", 12F, FontStyle.Regular);
        _descriptionLabel.ForeColor = Color.FromArgb(75, 85, 99);
        _descriptionLabel.TextAlign = ContentAlignment.TopLeft;
        _descriptionLabel.Dock = DockStyle.Fill;

        _detailLabel.Text = detail ?? string.Empty;
        _detailLabel.Font = new Font("Segoe UI", 10F, FontStyle.Italic);
        _detailLabel.ForeColor = Color.FromArgb(107, 114, 128);
        _detailLabel.TextAlign = ContentAlignment.TopLeft;
        _detailLabel.Dock = DockStyle.Fill;

        layout.Controls.Add(_titleLabel, 0, 0);
        layout.Controls.Add(_descriptionLabel, 0, 1);
        layout.Controls.Add(_detailLabel, 0, 2);

        _contentPanel.Controls.Add(layout);
    }

    // Kirch: Opens the June 20 MVP dashboard skeleton through AppServices.
    // Frontend note: screens may be restyled, but construction must keep flowing through AppServices/presenters.
    private void OpenDashboard()
    {
        using DashboardForm dashboardForm = new DashboardForm(_services);
        dashboardForm.StartPosition = FormStartPosition.CenterParent;
        dashboardForm.ShowDialog(this);
    }

    private void OpenOfficialOutputTextFile()
    {
        using OfficialOutputTextFileWorkflowForm officialOutputForm = new(_services, _session);
        officialOutputForm.StartPosition = FormStartPosition.CenterParent;
        officialOutputForm.ShowDialog(this);
    }

    // Kirch: Opens the June 20 MVP inventory skeleton through AppServices.
    private void OpenInventory()
    {
        using InventoryForm inventoryForm = _session is null
            ? new InventoryForm(_services)
            : new InventoryForm(_services, _session);
        inventoryForm.StartPosition = FormStartPosition.CenterParent;
        inventoryForm.ShowDialog(this);
    }

    // Kirch: Opens the June 20 MVP sales history skeleton through AppServices.
    private void OpenSalesHistory()
    {
        using SalesHistoryForm salesHistoryForm = _session is null
            ? new SalesHistoryForm(_services)
            : new SalesHistoryForm(_services, _session);
        salesHistoryForm.StartPosition = FormStartPosition.CenterParent;
        salesHistoryForm.ShowDialog(this);
    }

    // Kirch: Opens the June 20 MVP store settings skeleton through AppServices.
    private void OpenSettings()
    {
        using StoreSettingsForm settingsForm = new StoreSettingsForm(_services);
        settingsForm.StartPosition = FormStartPosition.CenterParent;
        settingsForm.ShowDialog(this);
    }

    private void OpenBackup()
    {
        using BackupForm backupForm = new(_services);
        backupForm.StartPosition = FormStartPosition.CenterParent;
        backupForm.ShowDialog(this);
    }

    private void OpenAudit()
    {
        using AuditLogViewerForm auditForm = new(_services);
        auditForm.StartPosition = FormStartPosition.CenterParent;
        auditForm.ShowDialog(this);
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
            new ProductManagementForm(
                new ProductManagementPresenter(
                    _services.ProductReadRepository,
                    _services.ProductWriteRepository,
                    _services.CatalogReadRepository),
                _session);

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
                _services.PosCheckoutApplication,
                _services.OfficialCheckoutCompletion,
                _services.CheckoutOfficialModeReadiness,
                checkoutSessionContext));

        checkoutForm.StartPosition = FormStartPosition.CenterParent;
        checkoutForm.ShowDialog(this);
    }
}