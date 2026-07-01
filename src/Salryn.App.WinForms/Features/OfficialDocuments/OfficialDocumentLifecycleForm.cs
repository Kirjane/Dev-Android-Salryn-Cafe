using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Models;

namespace Salryn.App.WinForms.Features.OfficialDocuments;

public sealed class OfficialDocumentLifecycleForm : Form
{
    private readonly OfficialDocumentLifecyclePresenter _presenter;
    private readonly TextBox _officialDocumentNumberTextBox = new();
    private readonly ComboBox _documentKindComboBox = new();
    private readonly TextBox _machineProfileIdTextBox = new();
    private readonly TextBox _registrationScopeTextBox = new();
    private readonly TextBox _journalEntryIdTextBox = new();
    private readonly Button _loadButton = new();
    private readonly DataGridView _eventsGrid = J20MvpUi.Grid();
    private readonly TextBox _reasonTextBox = new();
    private readonly Button _reprintButton = new();
    private readonly Button _voidButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _correctionButton = new();
    private readonly Button _refundButton = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _statusLabel = new();
    private bool _isBusy;

    public OfficialDocumentLifecycleForm(
        SalrynAppServices services,
        CurrentAppSession session)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(session);

        _presenter = new OfficialDocumentLifecyclePresenter(
            services.OfficialDocumentLifecycleRead,
            services.OfficialDocumentLifecycleOperations,
            services.OfficialDocumentReprints,
            services.OfficialOutputSourceRead,
            session);

        Text = "Official Documents";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1280, 760);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
        ApplyViewModel(_presenter.State);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 5);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 134F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Official Documents",
            "Review official document lifecycle state and record controlled audit events."), 0, 0);

        root.Controls.Add(BuildSearchPanel(), 0, 1);
        root.Controls.Add(BuildSummaryPanel(), 0, 2);
        root.Controls.Add(BuildEventsAndActionsPanel(), 0, 3);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        root.Controls.Add(_statusLabel, 0, 4);
    }

    private Panel BuildSearchPanel()
    {
        Panel panel = J20MvpUi.Card();
        panel.Margin = new Padding(0, 0, 0, 12);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 2
        };

        for (int index = 0; index < 5; index++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        }

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(layout);

        layout.Controls.Add(BuildFieldLabel("Official document #"), 0, 0);
        layout.Controls.Add(BuildFieldLabel("Document kind"), 1, 0);
        layout.Controls.Add(BuildFieldLabel("Machine profile id"), 2, 0);
        layout.Controls.Add(BuildFieldLabel("Registration scope"), 3, 0);
        layout.Controls.Add(BuildFieldLabel("Journal entry id"), 4, 0);

        _officialDocumentNumberTextBox.Dock = DockStyle.Fill;
        _documentKindComboBox.Dock = DockStyle.Fill;
        _documentKindComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _documentKindComboBox.Items.Add(new DocumentKindOption("Official receipt", ElectronicJournalDocumentKind.OfficialReceipt));
        _documentKindComboBox.Items.Add(new DocumentKindOption("Official invoice", ElectronicJournalDocumentKind.OfficialInvoice));
        _documentKindComboBox.SelectedIndex = 0;
        _machineProfileIdTextBox.Dock = DockStyle.Fill;
        _registrationScopeTextBox.Dock = DockStyle.Fill;
        _journalEntryIdTextBox.Dock = DockStyle.Fill;

        J20MvpUi.ConfigurePrimaryButton(_loadButton, "Load");
        _loadButton.Dock = DockStyle.Fill;

        layout.Controls.Add(_officialDocumentNumberTextBox, 0, 1);
        layout.Controls.Add(_documentKindComboBox, 1, 1);
        layout.Controls.Add(_machineProfileIdTextBox, 2, 1);
        layout.Controls.Add(_registrationScopeTextBox, 3, 1);
        layout.Controls.Add(_journalEntryIdTextBox, 4, 1);
        layout.Controls.Add(_loadButton, 5, 1);

        return panel;
    }

    private Panel BuildSummaryPanel()
    {
        Panel panel = J20MvpUi.Card();
        panel.Margin = new Padding(0, 0, 0, 12);
        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.TextAlign = ContentAlignment.TopLeft;
        _summaryLabel.Padding = new Padding(8);
        panel.Controls.Add(_summaryLabel);
        return panel;
    }

    private Panel BuildEventsAndActionsPanel()
    {
        Panel panel = J20MvpUi.Card();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        panel.Controls.Add(layout);

        _eventsGrid.Dock = DockStyle.Fill;
        layout.Controls.Add(_eventsGrid, 0, 0);
        layout.SetRowSpan(_eventsGrid, 2);

        TableLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(12, 0, 0, 0)
        };

        for (int index = 0; index < 7; index++)
        {
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 7F));
        }

        _reasonTextBox.Dock = DockStyle.Fill;
        _reasonTextBox.PlaceholderText = "Reason (required for all actions)";

        ConfigureActionButton(_reprintButton, "Reprint");
        ConfigureActionButton(_voidButton, "Void");
        ConfigureActionButton(_cancelButton, "Cancel");
        ConfigureActionButton(_correctionButton, "Correction");
        ConfigureActionButton(_refundButton, "Refund");

        actions.Controls.Add(_reasonTextBox, 0, 0);
        actions.Controls.Add(_reprintButton, 0, 1);
        actions.Controls.Add(_voidButton, 0, 2);
        actions.Controls.Add(_cancelButton, 0, 3);
        actions.Controls.Add(_correctionButton, 0, 4);
        actions.Controls.Add(_refundButton, 0, 5);

        layout.Controls.Add(actions, 1, 0);

        return panel;
    }

    private void WireEvents()
    {
        _loadButton.Click += async (_, _) => await LoadDocumentAsync().ConfigureAwait(true);
        _reprintButton.Click += async (_, _) => await ExecuteActionAsync(_presenter.ReprintAsync).ConfigureAwait(true);
        _voidButton.Click += async (_, _) => await ExecuteActionAsync(_presenter.VoidAsync).ConfigureAwait(true);
        _cancelButton.Click += async (_, _) => await ExecuteActionAsync(_presenter.CancelAsync).ConfigureAwait(true);
        _correctionButton.Click += async (_, _) => await ExecuteActionAsync(_presenter.CorrectionAsync).ConfigureAwait(true);
        _refundButton.Click += async (_, _) => await ExecuteActionAsync(_presenter.RefundAsync).ConfigureAwait(true);
    }

    private async Task LoadDocumentAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        SetBusy(true);

        try
        {
            OfficialDocumentLifecycleSearchInput input = BuildSearchInput();
            OfficialDocumentLifecycleOperationResult result =
                await _presenter.LoadAsync(input).ConfigureAwait(true);

            ApplyViewModel(result.State);
        }
        finally
        {
            _isBusy = false;
            SetBusy(false);
        }
    }

    private async Task ExecuteActionAsync(
        Func<string?, CancellationToken, Task<OfficialDocumentLifecycleOperationResult>> action)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        SetBusy(true);

        try
        {
            OfficialDocumentLifecycleOperationResult result =
                await action(_reasonTextBox.Text, CancellationToken.None).ConfigureAwait(true);

            ApplyViewModel(result.State);
        }
        finally
        {
            _isBusy = false;
            SetBusy(false);
        }
    }

    private OfficialDocumentLifecycleSearchInput BuildSearchInput()
    {
        Guid? journalEntryId = Guid.TryParse(_journalEntryIdTextBox.Text.Trim(), out Guid parsedJournalEntryId) &&
            parsedJournalEntryId != Guid.Empty
                ? parsedJournalEntryId
                : null;

        Guid? machineProfileId = Guid.TryParse(_machineProfileIdTextBox.Text.Trim(), out Guid parsedMachineProfileId) &&
            parsedMachineProfileId != Guid.Empty
                ? parsedMachineProfileId
                : null;

        DocumentKindOption? kindOption = _documentKindComboBox.SelectedItem as DocumentKindOption;

        return new OfficialDocumentLifecycleSearchInput(
            NormalizeOptional(_officialDocumentNumberTextBox.Text),
            kindOption?.Kind,
            machineProfileId,
            NormalizeOptional(_registrationScopeTextBox.Text),
            journalEntryId);
    }

    private void ApplyViewModel(OfficialDocumentLifecycleViewModel state)
    {
        if (!state.IsLoaded)
        {
            _summaryLabel.Text = state.StatusMessage;
        }
        else
        {
            _summaryLabel.Text =
                $"Document: {state.OfficialDocumentNumber} ({state.DocumentKind}){Environment.NewLine}" +
                $"Sale: {state.SaleId} · Journal: {state.JournalEntryId}{Environment.NewLine}" +
                $"Machine: {state.MachineProfileId} · Scope: {state.RegistrationScopeCode} · Terminal: {state.TerminalId}{Environment.NewLine}" +
                $"Lifecycle: {state.LifecycleState} · Z coverage: {state.ZCoverageStatus}{Environment.NewLine}" +
                $"Snapshot hash: {state.SnapshotPayloadHash} · Hash matches: {state.SnapshotHashMatches}{Environment.NewLine}" +
                (state.Issues.Count > 0
                    ? "Issues: " + string.Join("; ", state.Issues)
                    : "No lifecycle issues detected.");
        }

        _eventsGrid.DataSource = state.Events.ToList();
        _reprintButton.Enabled = state.CanReprint;
        _voidButton.Enabled = state.CanVoid;
        _cancelButton.Enabled = state.CanCancel;
        _correctionButton.Enabled = state.CanCorrection;
        _refundButton.Enabled = state.CanRefund;

        _statusLabel.Text = state.StatusMessage;
        _statusLabel.ForeColor = state.HasError
            ? J20MvpUi.Colors.Danger
            : J20MvpUi.Colors.TextMuted;
    }

    private void SetBusy(bool isBusy)
    {
        _loadButton.Enabled = !isBusy;
        _reprintButton.Enabled = !isBusy && _presenter.State.CanReprint;
        _voidButton.Enabled = !isBusy && _presenter.State.CanVoid;
        _cancelButton.Enabled = !isBusy && _presenter.State.CanCancel;
        _correctionButton.Enabled = !isBusy && _presenter.State.CanCorrection;
        _refundButton.Enabled = !isBusy && _presenter.State.CanRefund;
    }

    private static void ConfigureActionButton(Button button, string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 4, 0, 4);
    }

    private static Label BuildFieldLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = J20MvpUi.Colors.TextMuted
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record DocumentKindOption(string Label, ElectronicJournalDocumentKind Kind)
    {
        public override string ToString() => Label;
    }
}
