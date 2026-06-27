using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialOutput;

public sealed class OfficialOutputTextFileWorkflowForm : Form
{
    private readonly SalrynAppServices _services;
    private readonly CurrentAppSession? _session;
    private readonly DateTimePicker _rangeStartPicker = new();
    private readonly DateTimePicker _rangeEndPicker = new();
    private readonly ComboBox _documentKindComboBox = new();
    private readonly TextBox _machineProfileIdTextBox = new();
    private readonly TextBox _registrationScopeTextBox = new();
    private readonly Button _loadCandidatesButton = new();
    private readonly DataGridView _candidatesGrid = J20MvpUi.Grid();
    private readonly TextBox _outputRootPathTextBox = new();
    private readonly Button _runButton = new();
    private readonly Label _statusLabel = new();
    private IReadOnlyList<OfficialOutputTextFileSourceIdentityCandidate> _candidates =
        Array.Empty<OfficialOutputTextFileSourceIdentityCandidate>();

    public OfficialOutputTextFileWorkflowForm(
        SalrynAppServices services,
        CurrentAppSession? session)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _session = session;

        Text = "Official Output Text File";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1280, 760);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
        UpdateRunButtonState();
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 5);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 134F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Official Output Text File",
            "Select an official evidence-backed document and run the guarded text-file workflow."), 0, 0);

        root.Controls.Add(BuildFilterPanel(), 0, 1);
        root.Controls.Add(BuildCandidateGridPanel(), 0, 2);
        root.Controls.Add(BuildRunPanel(), 0, 3);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        _statusLabel.Text = "Enter source filters, load candidates, select one row, then provide an output root path.";
        root.Controls.Add(_statusLabel, 0, 4);
    }

    private Panel BuildFilterPanel()
    {
        Panel panel = J20MvpUi.Card();
        panel.Margin = new Padding(0, 0, 0, 12);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 2
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        panel.Controls.Add(layout);

        layout.Controls.Add(BuildFieldLabel("Range start"), 0, 0);
        layout.Controls.Add(BuildFieldLabel("Range end"), 1, 0);
        layout.Controls.Add(BuildFieldLabel("Document kind"), 2, 0);
        layout.Controls.Add(BuildFieldLabel("Machine profile id"), 3, 0);
        layout.Controls.Add(BuildFieldLabel("Registration scope"), 4, 0);

        ConfigureDatePicker(_rangeStartPicker, DateTime.Today);
        ConfigureDatePicker(_rangeEndPicker, DateTime.Today.AddDays(1));

        _documentKindComboBox.Dock = DockStyle.Fill;
        _documentKindComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _documentKindComboBox.Items.Add(new DocumentKindOption("Official receipt", ElectronicJournalDocumentKind.OfficialReceipt));
        _documentKindComboBox.Items.Add(new DocumentKindOption("Official invoice", ElectronicJournalDocumentKind.OfficialInvoice));
        _documentKindComboBox.SelectedIndex = 0;

        _machineProfileIdTextBox.Dock = DockStyle.Fill;
        _machineProfileIdTextBox.PlaceholderText = "Optional GUID";

        _registrationScopeTextBox.Dock = DockStyle.Fill;
        _registrationScopeTextBox.PlaceholderText = "Required when machine id is blank";

        J20MvpUi.ConfigurePrimaryButton(_loadCandidatesButton, "Load Candidates");
        _loadCandidatesButton.Dock = DockStyle.Fill;

        layout.Controls.Add(_rangeStartPicker, 0, 1);
        layout.Controls.Add(_rangeEndPicker, 1, 1);
        layout.Controls.Add(_documentKindComboBox, 2, 1);
        layout.Controls.Add(_machineProfileIdTextBox, 3, 1);
        layout.Controls.Add(_registrationScopeTextBox, 4, 1);
        layout.Controls.Add(_loadCandidatesButton, 5, 1);

        return panel;
    }

    private Panel BuildCandidateGridPanel()
    {
        Panel gridCard = J20MvpUi.Card();
        _candidatesGrid.Dock = DockStyle.Fill;
        gridCard.Controls.Add(_candidatesGrid);
        return gridCard;
    }

    private Panel BuildRunPanel()
    {
        Panel panel = J20MvpUi.Card();
        panel.Margin = new Padding(0, 0, 0, 12);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(layout);

        layout.Controls.Add(BuildFieldLabel("Output root path"), 0, 0);

        _outputRootPathTextBox.Dock = DockStyle.Fill;
        _outputRootPathTextBox.PlaceholderText = "Explicit folder path required";

        J20MvpUi.ConfigurePrimaryButton(_runButton, "Run Text File");
        _runButton.Dock = DockStyle.Fill;

        layout.Controls.Add(_outputRootPathTextBox, 0, 1);
        layout.Controls.Add(_runButton, 1, 1);

        return panel;
    }

    private void WireEvents()
    {
        _loadCandidatesButton.Click += async (_, _) => await LoadCandidatesAsync().ConfigureAwait(true);
        _runButton.Click += async (_, _) => await RunSelectedAsync().ConfigureAwait(true);
        _candidatesGrid.SelectionChanged += (_, _) => UpdateRunButtonState();
        _outputRootPathTextBox.TextChanged += (_, _) => UpdateRunButtonState();
    }

    private async Task LoadCandidatesAsync()
    {
        if (!TryBuildEvidenceRequest(out OfficialEvidenceReadRequest? request))
        {
            return;
        }

        SetStatus("Loading official evidence-backed candidates...", J20MvpUi.Colors.TextMuted);
        _loadCandidatesButton.Enabled = false;

        try
        {
            Result<IReadOnlyList<OfficialOutputTextFileSourceIdentityCandidate>> result =
                await _services.OfficialOutputTextFileSourceIdentity
                    .ListCandidatesAsync(new OfficialOutputTextFileSourceIdentityListRequest(request))
                    .ConfigureAwait(true);

            if (result.IsFailure)
            {
                _candidates = Array.Empty<OfficialOutputTextFileSourceIdentityCandidate>();
                BindCandidateRows();
                SetStatus($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}", J20MvpUi.Colors.Danger);
                return;
            }

            _candidates = result.Value;
            BindCandidateRows();
            SetStatus($"Loaded {_candidates.Count} official evidence-backed candidates.", J20MvpUi.Colors.TextMuted);
        }
        finally
        {
            _loadCandidatesButton.Enabled = true;
            UpdateRunButtonState();
        }
    }

    private async Task RunSelectedAsync()
    {
        if (!TryBuildTriggerRequest(out OfficialOutputTextFileWorkflowUiTriggerRequest? triggerRequest))
        {
            return;
        }

        SetStatus("Running official output text-file workflow...", J20MvpUi.Colors.TextMuted);
        _runButton.Enabled = false;

        try
        {
            OfficialOutputTextFileWorkflowUiCommandResult result =
                await _services.OfficialOutputTextFileWorkflowUi
                    .TryRunAsync(triggerRequest)
                    .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                SetStatus(
                    $"{result.StatusCode}: {result.ErrorCode ?? result.StatusMessage}",
                    J20MvpUi.Colors.Danger);
                return;
            }

            SetStatus(
                $"Official output text-file workflow completed. Workflow reference: {result.References?.WorkflowReference ?? "(none)"}.",
                J20MvpUi.Colors.Success);
        }
        finally
        {
            UpdateRunButtonState();
        }
    }

    private bool TryBuildEvidenceRequest(out OfficialEvidenceReadRequest? request)
    {
        request = null;

        DateTimeOffset rangeStart = new(_rangeStartPicker.Value);
        DateTimeOffset rangeEnd = new(_rangeEndPicker.Value);

        if (rangeEnd <= rangeStart)
        {
            SetStatus("Official evidence date range end must be after range start.", J20MvpUi.Colors.Danger);
            return false;
        }

        if (_documentKindComboBox.SelectedItem is not DocumentKindOption documentKind)
        {
            SetStatus("Document kind filter is required before listing official evidence candidates.", J20MvpUi.Colors.Danger);
            return false;
        }

        Guid? machineProfileId = null;
        string? machineProfileText = NormalizeOptional(_machineProfileIdTextBox.Text);

        if (machineProfileText is not null)
        {
            if (!Guid.TryParse(machineProfileText, out Guid parsedMachineProfileId) ||
                parsedMachineProfileId == Guid.Empty)
            {
                SetStatus("Machine profile id must be a non-empty GUID.", J20MvpUi.Colors.Danger);
                return false;
            }

            machineProfileId = parsedMachineProfileId;
        }

        string? registrationScope = NormalizeOptional(_registrationScopeTextBox.Text);

        if (!machineProfileId.HasValue && registrationScope is null)
        {
            SetStatus("Machine profile id or registration scope is required before listing official evidence candidates.", J20MvpUi.Colors.Danger);
            return false;
        }

        request = new OfficialEvidenceReadRequest(
            RangeStartedAt: rangeStart,
            RangeEndedAt: rangeEnd,
            BirMachineProfileId: machineProfileId,
            RegistrationScopeCode: registrationScope,
            DocumentKind: documentKind.Kind);

        return true;
    }

    private bool TryBuildTriggerRequest(
        out OfficialOutputTextFileWorkflowUiTriggerRequest? triggerRequest)
    {
        triggerRequest = null;

        OfficialOutputTextFileSourceIdentityCandidate? candidate = GetSelectedCandidate();

        if (candidate is null)
        {
            SetStatus("Select an official evidence-backed candidate before running.", J20MvpUi.Colors.Danger);
            return false;
        }

        string? outputRootPath = NormalizeOptional(_outputRootPathTextBox.Text);

        if (outputRootPath is null)
        {
            SetStatus("Explicit output root path is required before running.", J20MvpUi.Colors.Danger);
            return false;
        }

        if (_session is null ||
            !_session.IsActive ||
            _session.UserId == Guid.Empty)
        {
            SetStatus("An active app user session is required before running.", J20MvpUi.Colors.Danger);
            return false;
        }

        string? terminalId = ResolveTerminalId(candidate);

        if (terminalId is null)
        {
            SetStatus("Terminal id is required from official evidence or Local Host context before running.", J20MvpUi.Colors.Danger);
            return false;
        }

        DateTimeOffset requestedAt = DateTimeOffset.UtcNow;
        IReadOnlyDictionary<string, string> metadata = BuildMetadata(candidate);

        Result<OfficialOutputTextFileSourceSelectionRequest> sourceSelection =
            _services.OfficialOutputTextFileSourceIdentity.BuildSourceSelectionRequest(
                new OfficialOutputTextFileSourceIdentitySelectionRequest(
                    candidate,
                    requestedAt,
                    _session.UserId,
                    terminalId,
                    metadata));

        if (sourceSelection.IsFailure)
        {
            SetStatus($"{sourceSelection.ErrorDetail.Code}: {sourceSelection.ErrorDetail.Message}", J20MvpUi.Colors.Danger);
            return false;
        }

        triggerRequest = new OfficialOutputTextFileWorkflowUiTriggerRequest(
            OutputRootPath: outputRootPath,
            RequestedAt: requestedAt,
            RequestedByUserId: _session.UserId,
            TerminalId: terminalId,
            Metadata: metadata,
            SourceSelectionRequest: sourceSelection.Value);

        return true;
    }

    private void BindCandidateRows()
    {
        _candidatesGrid.DataSource = _candidates
            .Select((candidate, index) => new OfficialOutputTextFileCandidateRow(
                CandidateIndex: index,
                JournalEntryId: candidate.JournalEntryId.ToString("D"),
                OfficialDocumentNumber: candidate.OfficialDocumentNumber,
                DocumentKind: candidate.DocumentKind.ToString(),
                BirMachineProfileId: candidate.BirMachineProfileId.ToString("D"),
                RegistrationScopeCode: candidate.RegistrationScopeCode ?? string.Empty,
                TransactionTimestamp: candidate.TransactionTimestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
                TerminalId: candidate.TerminalId ?? string.Empty))
            .ToList();

        if (_candidatesGrid.Columns["CandidateIndex"] is not null)
        {
            _candidatesGrid.Columns["CandidateIndex"]!.Visible = false;
        }
    }

    private OfficialOutputTextFileSourceIdentityCandidate? GetSelectedCandidate()
    {
        if (_candidatesGrid.CurrentRow?.DataBoundItem is not OfficialOutputTextFileCandidateRow row ||
            row.CandidateIndex < 0 ||
            row.CandidateIndex >= _candidates.Count)
        {
            return null;
        }

        return _candidates[row.CandidateIndex];
    }

    private string? ResolveTerminalId(OfficialOutputTextFileSourceIdentityCandidate candidate)
    {
        string? evidenceTerminalId = NormalizeOptional(candidate.TerminalId);

        if (evidenceTerminalId is not null)
        {
            return evidenceTerminalId;
        }

        var terminal = _services.LocalHostRequestContextProvider.Profile.Terminal;

        return terminal is not null && terminal.TerminalId != Guid.Empty
            ? terminal.TerminalId.ToString("D")
            : null;
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(
        OfficialOutputTextFileSourceIdentityCandidate candidate)
    {
        SortedDictionary<string, string> metadata = new(StringComparer.Ordinal)
        {
            ["journalEntryId"] = candidate.JournalEntryId.ToString("D"),
            ["officialDocumentNumber"] = candidate.OfficialDocumentNumber,
            ["sourceReference"] = candidate.SourceReference,
            ["uiSurface"] = nameof(OfficialOutputTextFileWorkflowForm)
        };

        string? registrationScope = NormalizeOptional(candidate.RegistrationScopeCode);

        if (registrationScope is not null)
        {
            metadata["registrationScopeCode"] = registrationScope;
        }

        return metadata;
    }

    private void UpdateRunButtonState()
    {
        _runButton.Enabled =
            GetSelectedCandidate() is not null &&
            !string.IsNullOrWhiteSpace(_outputRootPathTextBox.Text);
    }

    private void SetStatus(string message, Color color)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = color;
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

    private static void ConfigureDatePicker(DateTimePicker picker, DateTime value)
    {
        picker.Dock = DockStyle.Fill;
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "yyyy-MM-dd HH:mm";
        picker.ShowUpDown = true;
        picker.Value = value;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record DocumentKindOption(
        string Label,
        ElectronicJournalDocumentKind Kind)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private sealed record OfficialOutputTextFileCandidateRow(
        int CandidateIndex,
        string JournalEntryId,
        string OfficialDocumentNumber,
        string DocumentKind,
        string BirMachineProfileId,
        string RegistrationScopeCode,
        string TransactionTimestamp,
        string TerminalId);
}
