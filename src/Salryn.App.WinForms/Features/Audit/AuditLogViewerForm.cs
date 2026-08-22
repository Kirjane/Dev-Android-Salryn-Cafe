using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Audit;

// Kirch file guide: read-only support view for audit and error log records.
// UI location: opened from Shell > Audit.
// Frontend note: filter/paging controls stay read-only; repository calls must remain behind AppServices.
public sealed class AuditLogViewerForm : Form
{
    private static readonly int[] AllowedPageSizes = { 25, 50, 100, 200 };

    private readonly SalrynAppServices _services;

    private readonly DateTimePicker _fromDatePicker = new();
    private readonly DateTimePicker _toDatePicker = new();
    private readonly TextBox _searchTextBox = new();
    private readonly ComboBox _pageSizeComboBox = new();
    private readonly Button _applyButton = new();
    private readonly Button _refreshButton = new();
    private readonly Button _previousPageButton = new();
    private readonly Button _nextPageButton = new();
    private readonly TabControl _tabs = new();
    private readonly DataGridView _auditGrid = J20MvpUi.Grid();
    private readonly DataGridView _errorGrid = J20MvpUi.Grid();
    private readonly Label _statusLabel = new();

    // Kirch B7: hover guidance for the read-only filter and paging controls.
    private readonly ToolTip _toolTip = J20MvpUi.CreateToolTip();

    private int _auditPageNumber = 1;
    private int _errorPageNumber = 1;
    private PagedResult<AuditLogReadModel>? _auditPage;
    private PagedResult<ErrorLogReadModel>? _errorPage;

    public AuditLogViewerForm(SalrynAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Text = "SALRYN Audit";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1040, 720);
        Size = new Size(1180, 780);
        Font = new Font("Segoe UI", 9F);
        BackColor = SalrynUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadActiveTabAsync(resetPage: true).ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

        Controls.Add(root);

        root.Controls.Add(SalrynUi.CreatePageHeader(
            "Audit / Error Logs",
            "Read-only support view with filter, paging, and status for local audit and error records."), 0, 0);

        root.Controls.Add(BuildFilterPanel(), 0, 1);
        root.Controls.Add(BuildPagingPanel(), 0, 2);

        // Ledger console styling layered over the read-only MVP grids.
        SalrynUi.ApplyLedgerGridStyle(_auditGrid);
        SalrynUi.ApplyLedgerGridStyle(_errorGrid);
        WireErrorSeverityColoring();

        TabPage auditPage = new("Audit Logs");
        auditPage.Controls.Add(_auditGrid);

        TabPage errorPage = new("Error Logs");
        errorPage.Controls.Add(_errorGrid);

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(auditPage);
        _tabs.TabPages.Add(errorPage);
        root.Controls.Add(_tabs, 0, 3);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        SalrynUi.SetStatusNeutral(_statusLabel, string.Empty);
        SalrynUi.ConfigureFooterStatus(_statusLabel);
        root.Controls.Add(_statusLabel, 0, 4);
    }

    // Error log rows are inherently error-severity; color the area cell danger from already-loaded values.
    private void WireErrorSeverityColoring()
    {
        _errorGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (_errorGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(ErrorLogRow.Area))
            {
                e.CellStyle.ForeColor = SalrynUi.Colors.Danger;
                e.CellStyle.SelectionForeColor = SalrynUi.Colors.Danger;
            }
        };
    }

    private Control BuildFilterPanel()
    {
        Panel commandBar = SalrynUi.CreateCommandBar();

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            BackColor = SalrynUi.Colors.Surface
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));

        ConfigureDatePicker(_fromDatePicker);
        ConfigureDatePicker(_toDatePicker);

        _searchTextBox.Dock = DockStyle.Fill;
        _searchTextBox.PlaceholderText = "Search action, entity, user, reason, area, message...";

        _pageSizeComboBox.Dock = DockStyle.Fill;
        _pageSizeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _pageSizeComboBox.Items.AddRange(AllowedPageSizes.Select(static size => (object)size).ToArray());
        _pageSizeComboBox.SelectedItem = 50;

        SalrynUi.ConfigurePrimaryButton(_applyButton, "Apply");

        _toolTip.SetToolTip(_fromDatePicker, "Optional start date. Tick the box to enable this filter.");
        _toolTip.SetToolTip(_toDatePicker, "Optional end date. Tick the box to enable this filter.");
        _toolTip.SetToolTip(_searchTextBox, "Type text to filter records, then click Apply.");
        _toolTip.SetToolTip(_pageSizeComboBox, "How many rows to show per page.");
        _toolTip.SetToolTip(_applyButton, "Apply the current filters and reload from the first page.");

        panel.Controls.Add(CreateFieldLabel("From"), 0, 0);
        panel.Controls.Add(_fromDatePicker, 1, 0);
        panel.Controls.Add(CreateFieldLabel("To"), 2, 0);
        panel.Controls.Add(_toDatePicker, 3, 0);
        panel.Controls.Add(_searchTextBox, 4, 0);
        panel.Controls.Add(_pageSizeComboBox, 5, 0);
        panel.Controls.Add(_applyButton, 6, 0);

        commandBar.Controls.Add(panel);
        return commandBar;
    }

    private Control BuildPagingPanel()
    {
        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = SalrynUi.Colors.AppBackground,
            Padding = new Padding(0, 8, 0, 0)
        };

        SalrynUi.ConfigureSecondaryButton(_nextPageButton, "Next Page");
        SalrynUi.ConfigureSecondaryButton(_previousPageButton, "Previous Page");
        SalrynUi.ConfigureSecondaryButton(_refreshButton, "Refresh");

        _toolTip.SetToolTip(_nextPageButton, "Go to the next page of records.");
        _toolTip.SetToolTip(_previousPageButton, "Go to the previous page of records.");
        _toolTip.SetToolTip(_refreshButton, "Reload the current page.");

        panel.Controls.Add(_nextPageButton);
        panel.Controls.Add(_previousPageButton);
        panel.Controls.Add(_refreshButton);

        return panel;
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = SalrynUi.Colors.TextStrong
        };
    }

    private static void ConfigureDatePicker(DateTimePicker picker)
    {
        picker.Dock = DockStyle.Fill;
        picker.Format = DateTimePickerFormat.Short;
        picker.ShowCheckBox = true;
        picker.Checked = false;
        picker.Value = DateTime.Today;
    }

    private void WireEvents()
    {
        _applyButton.Click += async (_, _) => await LoadActiveTabAsync(resetPage: true).ConfigureAwait(true);
        _refreshButton.Click += async (_, _) => await LoadActiveTabAsync(resetPage: false).ConfigureAwait(true);
        _previousPageButton.Click += async (_, _) => await ChangePageAsync(-1).ConfigureAwait(true);
        _nextPageButton.Click += async (_, _) => await ChangePageAsync(1).ConfigureAwait(true);
        _tabs.SelectedIndexChanged += async (_, _) => await LoadActiveTabAsync(resetPage: false).ConfigureAwait(true);
        _pageSizeComboBox.SelectedIndexChanged += async (_, _) => await LoadActiveTabAsync(resetPage: true).ConfigureAwait(true);
    }

    private async Task ChangePageAsync(int delta)
    {
        if (IsAuditTabActive)
        {
            _auditPageNumber = Math.Max(1, _auditPageNumber + delta);
        }
        else
        {
            _errorPageNumber = Math.Max(1, _errorPageNumber + delta);
        }

        await LoadActiveTabAsync(resetPage: false).ConfigureAwait(true);
    }

    private bool IsAuditTabActive => _tabs.SelectedIndex == 0;

    private async Task LoadActiveTabAsync(bool resetPage)
    {
        if (resetPage)
        {
            _auditPageNumber = 1;
            _errorPageNumber = 1;
        }

        SetBusy("Loading logs...");

        try
        {
            if (IsAuditTabActive)
            {
                await LoadAuditPageAsync().ConfigureAwait(true);
            }
            else
            {
                await LoadErrorPageAsync().ConfigureAwait(true);
            }

            UpdatePagingButtons();
        }
        finally
        {
            SetReady();
        }
    }

    private async Task LoadAuditPageAsync()
    {
        AuditLogSearchFilter filter = BuildAuditFilter(_auditPageNumber);
        var result = await _services.AuditReadRepository
            .SearchAuditLogsAsync(filter)
            .ConfigureAwait(true);

        if (result.IsFailure)
        {
            _auditGrid.DataSource = Array.Empty<AuditLogRow>();
            _auditPage = null;
            SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
            return;
        }

        _auditPage = result.Value;
        _auditPageNumber = result.Value.PageNumber;

        _auditGrid.DataSource = result.Value.Items
            .Select(static item => new AuditLogRow(
                CreatedAt: item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                User: item.UserDisplayName ?? string.Empty,
                Action: item.Action,
                EntityType: item.EntityType,
                EntityId: item.EntityId ?? string.Empty,
                Reason: item.Reason ?? string.Empty))
            .ToList();

        SetStatusForActiveTab();
    }

    private async Task LoadErrorPageAsync()
    {
        ErrorLogSearchFilter filter = BuildErrorFilter(_errorPageNumber);
        var result = await _services.ErrorLogReadRepository
            .SearchErrorLogsAsync(filter)
            .ConfigureAwait(true);

        if (result.IsFailure)
        {
            _errorGrid.DataSource = Array.Empty<ErrorLogRow>();
            _errorPage = null;
            SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
            return;
        }

        _errorPage = result.Value;
        _errorPageNumber = result.Value.PageNumber;

        _errorGrid.DataSource = result.Value.Items
            .Select(static item => new ErrorLogRow(
                CreatedAt: item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                User: item.UserDisplayName ?? string.Empty,
                Area: item.Area,
                Message: item.Message,
                TechnicalDetail: item.TechnicalDetail ?? string.Empty))
            .ToList();

        SetStatusForActiveTab();
    }

    private AuditLogSearchFilter BuildAuditFilter(int pageNumber)
    {
        return new AuditLogSearchFilter(
            new PageRequest(pageNumber, GetSelectedPageSize()),
            GetFromDate(),
            GetToDate(),
            new SearchText(_searchTextBox.Text));
    }

    private ErrorLogSearchFilter BuildErrorFilter(int pageNumber)
    {
        return new ErrorLogSearchFilter(
            new PageRequest(pageNumber, GetSelectedPageSize()),
            GetFromDate(),
            GetToDate(),
            new SearchText(_searchTextBox.Text));
    }

    private int GetSelectedPageSize()
    {
        if (_pageSizeComboBox.SelectedItem is int pageSize &&
            AllowedPageSizes.Contains(pageSize))
        {
            return pageSize;
        }

        return 50;
    }

    private DateTimeOffset? GetFromDate()
    {
        if (!_fromDatePicker.Checked)
        {
            return null;
        }

        DateTime localDate = _fromDatePicker.Value.Date;
        return new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
    }

    private DateTimeOffset? GetToDate()
    {
        if (!_toDatePicker.Checked)
        {
            return null;
        }

        DateTime localEnd = _toDatePicker.Value.Date.AddDays(1).AddTicks(-1);
        return new DateTimeOffset(localEnd, TimeZoneInfo.Local.GetUtcOffset(localEnd));
    }

    private void UpdatePagingButtons()
    {
        if (IsAuditTabActive)
        {
            _previousPageButton.Enabled = _auditPage?.HasPreviousPage == true;
            _nextPageButton.Enabled = _auditPage?.HasNextPage == true;
            return;
        }

        _previousPageButton.Enabled = _errorPage?.HasPreviousPage == true;
        _nextPageButton.Enabled = _errorPage?.HasNextPage == true;
    }

    private void SetStatusForActiveTab()
    {
        if (IsAuditTabActive && _auditPage is not null)
        {
            string auditMessage = _auditPage.TotalCount == 0
                ? J20MvpUi.StateText.NothingToShow + " No audit records match these filters."
                : $"Audit page {_auditPage.PageNumber} of {Math.Max(1, _auditPage.TotalPages)}; " +
                  $"loaded {_auditPage.Items.Count} of {_auditPage.TotalCount} row(s).";
            SalrynUi.SetStatusNeutral(_statusLabel, auditMessage);
            return;
        }

        if (!IsAuditTabActive && _errorPage is not null)
        {
            string errorMessage = _errorPage.TotalCount == 0
                ? J20MvpUi.StateText.NothingToShow + " No error records match these filters."
                : $"Error page {_errorPage.PageNumber} of {Math.Max(1, _errorPage.TotalPages)}; " +
                  $"loaded {_errorPage.Items.Count} of {_errorPage.TotalCount} row(s).";
            SalrynUi.SetStatusNeutral(_statusLabel, errorMessage);
        }
    }

    private void SetBusy(string message)
    {
        _applyButton.Enabled = false;
        _refreshButton.Enabled = false;
        _previousPageButton.Enabled = false;
        _nextPageButton.Enabled = false;
        SalrynUi.SetStatusNeutral(_statusLabel, message);
    }

    private void SetReady()
    {
        _applyButton.Enabled = true;
        _refreshButton.Enabled = true;
        UpdatePagingButtons();
    }

    private void SetError(string message)
    {
        SalrynUi.SetStatusError(_statusLabel, message);
        UpdatePagingButtons();
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
