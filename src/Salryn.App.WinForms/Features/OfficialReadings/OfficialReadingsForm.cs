using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Models;

namespace Salryn.App.WinForms.Features.OfficialReadings;

public sealed class OfficialReadingsForm : Form
{
    private readonly OfficialReadingsPresenter _presenter;
    private readonly DateTimePicker _rangeStartPicker = new();
    private readonly DateTimePicker _rangeEndPicker = new();
    private readonly ComboBox _documentKindComboBox = new();
    private readonly TextBox _machineProfileIdTextBox = new();
    private readonly TextBox _registrationScopeTextBox = new();
    private readonly Button _previewButton = new();
    private readonly Button _finalizeButton = new();
    private readonly TextBox _previewTextBox = new();
    private readonly TextBox _finalizeTextBox = new();
    private readonly Label _statusLabel = new();
    private bool _isBusy;

    public OfficialReadingsForm(
        SalrynAppServices services,
        CurrentAppSession session)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(session);

        _presenter = new OfficialReadingsPresenter(
            services.OfficialXReading,
            services.OfficialZReading,
            session);

        Text = "Official Readings";
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Official Readings",
            "Preview X-reading totals and finalize Z-reading with enriched persistence."), 0, 0);

        root.Controls.Add(BuildFilterPanel(), 0, 1);
        root.Controls.Add(BuildPreviewPanel(), 0, 2);
        root.Controls.Add(BuildFinalizePanel(), 0, 3);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
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

        for (int index = 0; index < 5; index++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        }

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
        _registrationScopeTextBox.Dock = DockStyle.Fill;

        J20MvpUi.ConfigurePrimaryButton(_previewButton, "Preview X-Reading");
        _previewButton.Dock = DockStyle.Fill;

        layout.Controls.Add(_rangeStartPicker, 0, 1);
        layout.Controls.Add(_rangeEndPicker, 1, 1);
        layout.Controls.Add(_documentKindComboBox, 2, 1);
        layout.Controls.Add(_machineProfileIdTextBox, 3, 1);
        layout.Controls.Add(_registrationScopeTextBox, 4, 1);
        layout.Controls.Add(_previewButton, 5, 1);

        return panel;
    }

    private Panel BuildPreviewPanel()
    {
        Panel panel = J20MvpUi.Card();
        panel.Margin = new Padding(0, 0, 0, 12);
        _previewTextBox.Dock = DockStyle.Fill;
        _previewTextBox.Multiline = true;
        _previewTextBox.ReadOnly = true;
        _previewTextBox.ScrollBars = ScrollBars.Vertical;
        _previewTextBox.Font = new Font("Consolas", 9F);
        panel.Controls.Add(_previewTextBox);
        return panel;
    }

    private Panel BuildFinalizePanel()
    {
        Panel panel = J20MvpUi.Card();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
        panel.Controls.Add(layout);

        _finalizeTextBox.Dock = DockStyle.Fill;
        _finalizeTextBox.Multiline = true;
        _finalizeTextBox.ReadOnly = true;
        _finalizeTextBox.ScrollBars = ScrollBars.Vertical;
        _finalizeTextBox.Font = new Font("Consolas", 9F);
        layout.Controls.Add(_finalizeTextBox, 0, 0);

        J20MvpUi.ConfigurePrimaryButton(_finalizeButton, "Finalize Z-Reading");
        _finalizeButton.Dock = DockStyle.Fill;
        _finalizeButton.Margin = new Padding(12, 0, 0, 0);
        layout.Controls.Add(_finalizeButton, 1, 0);

        return panel;
    }

    private void WireEvents()
    {
        _previewButton.Click += async (_, _) => await PreviewAsync().ConfigureAwait(true);
        _finalizeButton.Click += async (_, _) => await FinalizeAsync().ConfigureAwait(true);
    }

    private async Task PreviewAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        SetBusy(true);

        try
        {
            OfficialReadingsOperationResult result =
                await _presenter.PreviewXReadingAsync(BuildSearchInput()).ConfigureAwait(true);

            ApplyViewModel(result.State);
        }
        finally
        {
            _isBusy = false;
            SetBusy(false);
        }
    }

    private async Task FinalizeAsync()
    {
        if (_isBusy)
        {
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            "Finalize Z-reading for the previewed range? This persists enriched Z-reading data and cannot be undone from this screen.",
            "Confirm Z-Reading Finalization",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        _isBusy = true;
        SetBusy(true);

        try
        {
            OfficialReadingsOperationResult result =
                await _presenter.FinalizeZReadingAsync().ConfigureAwait(true);

            ApplyViewModel(result.State);
        }
        finally
        {
            _isBusy = false;
            SetBusy(false);
        }
    }

    private OfficialReadingsSearchInput BuildSearchInput()
    {
        Guid? machineProfileId = Guid.TryParse(_machineProfileIdTextBox.Text.Trim(), out Guid parsedMachineProfileId) &&
            parsedMachineProfileId != Guid.Empty
                ? parsedMachineProfileId
                : null;

        DocumentKindOption? kindOption = _documentKindComboBox.SelectedItem as DocumentKindOption;

        return new OfficialReadingsSearchInput(
            new DateTimeOffset(_rangeStartPicker.Value),
            new DateTimeOffset(_rangeEndPicker.Value),
            kindOption?.Kind ?? ElectronicJournalDocumentKind.OfficialReceipt,
            machineProfileId,
            NormalizeOptional(_registrationScopeTextBox.Text));
    }

    private void ApplyViewModel(OfficialReadingsViewModel state)
    {
        _previewTextBox.Text = state.HasPreview ? state.PreviewText : "No X-reading preview loaded.";
        _finalizeTextBox.Text = state.HasFinalizeResult ? state.FinalizeResultText : "No Z-reading finalized yet.";
        _previewButton.Enabled = state.CanPreview;
        _finalizeButton.Enabled = state.CanFinalize;
        _statusLabel.Text = state.StatusMessage;
        _statusLabel.ForeColor = state.HasError
            ? J20MvpUi.Colors.Danger
            : J20MvpUi.Colors.TextMuted;
    }

    private void SetBusy(bool isBusy)
    {
        _previewButton.Enabled = !isBusy && _presenter.State.CanPreview;
        _finalizeButton.Enabled = !isBusy && _presenter.State.CanFinalize;
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
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record DocumentKindOption(string Label, ElectronicJournalDocumentKind Kind)
    {
        public override string ToString() => Label;
    }
}
