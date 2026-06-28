using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Audit;

// Kirch file guide: read-only support view for audit and error log records.
// UI location: opened from Shell > Audit.
// Frontend note: filtering and paging can be added later; keep this screen read-only.
public sealed class AuditLogViewerForm : Form
{
    private readonly SalrynAppServices _services;

    private readonly Button _refreshButton = new();
    private readonly DataGridView _auditGrid = J20MvpUi.Grid();
    private readonly DataGridView _errorGrid = J20MvpUi.Grid();
    private readonly Label _statusLabel = new();

    public AuditLogViewerForm(SalrynAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Text = "SALRYN Audit";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1040, 660);
        Size = new Size(1120, 720);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadLogsAsync().ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 4);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Audit / Error Logs",
            "Read-only support view for local audit records and captured errors."), 0, 0);

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = J20MvpUi.Colors.AppBackground,
            Padding = new Padding(0, 8, 0, 0)
        };

        J20MvpUi.ConfigurePrimaryButton(_refreshButton, "Refresh");
        toolbar.Controls.Add(_refreshButton);
        root.Controls.Add(toolbar, 0, 1);

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill
        };

        TabPage auditPage = new("Audit Logs");
        auditPage.Controls.Add(_auditGrid);

        TabPage errorPage = new("Error Logs");
        errorPage.Controls.Add(_errorGrid);

        tabs.TabPages.Add(auditPage);
        tabs.TabPages.Add(errorPage);

        root.Controls.Add(tabs, 0, 2);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        root.Controls.Add(_statusLabel, 0, 3);
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await LoadLogsAsync().ConfigureAwait(true);
    }

    private async Task LoadLogsAsync()
    {
        _refreshButton.Enabled = false;
        _statusLabel.Text = "Loading audit and error logs...";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;

        try
        {
            var auditResult = await _services.AuditReadRepository
                .SearchAuditLogsAsync(PageRequest.FirstPage(100))
                .ConfigureAwait(true);
            var errorResult = await _services.ErrorLogReadRepository
                .SearchErrorLogsAsync(PageRequest.FirstPage(100))
                .ConfigureAwait(true);

            if (auditResult.IsFailure)
            {
                _auditGrid.DataSource = Array.Empty<AuditLogRow>();
                SetError($"{auditResult.ErrorDetail.Code}: {auditResult.ErrorDetail.Message}");
                return;
            }

            if (errorResult.IsFailure)
            {
                _errorGrid.DataSource = Array.Empty<ErrorLogRow>();
                SetError($"{errorResult.ErrorDetail.Code}: {errorResult.ErrorDetail.Message}");
                return;
            }

            _auditGrid.DataSource = auditResult.Value.Items
                .Select(static item => new AuditLogRow(
                    CreatedAt: item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    User: item.UserDisplayName ?? string.Empty,
                    Action: item.Action,
                    EntityType: item.EntityType,
                    EntityId: item.EntityId ?? string.Empty,
                    Reason: item.Reason ?? string.Empty))
                .ToList();

            _errorGrid.DataSource = errorResult.Value.Items
                .Select(static item => new ErrorLogRow(
                    CreatedAt: item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    User: item.UserDisplayName ?? string.Empty,
                    Area: item.Area,
                    Message: item.Message,
                    TechnicalDetail: item.TechnicalDetail ?? string.Empty))
                .ToList();

            _statusLabel.Text =
                $"Loaded {auditResult.Value.Items.Count} audit rows of {auditResult.Value.TotalCount}; " +
                $"{errorResult.Value.Items.Count} error rows of {errorResult.Value.TotalCount}.";
            _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        }
        finally
        {
            _refreshButton.Enabled = true;
        }
    }

    private void SetError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
    }

    private sealed record AuditLogRow(
        string CreatedAt,
        string User,
        string Action,
        string EntityType,
        string EntityId,
        string Reason);

    private sealed record ErrorLogRow(
        string CreatedAt,
        string User,
        string Area,
        string Message,
        string TechnicalDetail);
}
