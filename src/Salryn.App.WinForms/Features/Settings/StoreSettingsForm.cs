using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.App.WinForms.Features.Settings;

// Kirch file guide: store profile/settings screen.
// UI location: opened from Shell > Settings.
// Frontend note: edits must stay behind AppServices and the settings write repository.

// Kirch: Store Settings supports a controlled edit/save flow for existing settings rows.
public sealed class StoreSettingsForm : Form
{
    private readonly SalrynAppServices _services;

    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _editButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _cancelButton = new();

    // Kirch B7: hover guidance for the settings actions.
    private readonly ToolTip _toolTip = J20MvpUi.CreateToolTip();
    // Kirch UI map: store settings field registry.
    // UI side: each text box displays one store/receipt header field and enters edit mode only through this form.
    // Frontend safe zone: grouping, labels, helper text, and future widget polish.
    // Do not change: saves must stay through StoreSettingsWriteRepository via AppServices.
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.OrdinalIgnoreCase);

    private StoreSettingsReadModel? _currentSettings;
    private bool _isEditing;
    private bool _isBusy;

    public StoreSettingsForm(SalrynAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Text = "SALRYN Store Settings";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 620);
        Size = new Size(840, 680);
        Font = new Font("Segoe UI", 9F);
        BackColor = SalrynUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
        SetEditMode(false);
        UpdateButtons();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadAsync().ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

        Controls.Add(root);

        root.Controls.Add(SalrynUi.CreatePageHeader(
            "Store Settings",
            "Store profile and receipt header settings."), 0, 0);

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SalrynUi.Colors.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 8, 12, 8)
        };

        TableLayoutPanel form = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            BackColor = SalrynUi.Colors.Surface
        };

        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        // Section title for the store profile / receipt settings area.
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        Label sectionTitle = SalrynUi.CreateSectionTitle("Store Profile & Receipt Settings");
        form.Controls.Add(sectionTitle, 0, 0);
        form.SetColumnSpan(sectionTitle, 2);

        string[] labels =
        {
            "Store Name",
            "Business Name",
            "Address",
            "Contact Number",
            "TIN",
            "Currency",
            "Receipt Width",
            "Tax Mode",
            "Shift Mode"
        };

        for (int i = 0; i < labels.Length; i++)
        {
            int row = i + 1;
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            AddField(form, row, labels[i]);
        }

        card.Controls.Add(form);
        root.Controls.Add(card, 0, 1);

        Label note = J20MvpUi.BodyText(
            "Edit store profile fields used for receipt headers and local system display. " +
            "Receipt width must be 58 or 80 millimeters; Tax Mode and Shift Mode accept Enabled/Disabled.");
        note.Dock = DockStyle.Fill;
        root.Controls.Add(note, 0, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        SalrynUi.StyleFooterStrip(footer);

        // Save is the affirmative action (success); the rest are secondary.
        SalrynUi.ConfigureSuccessButton(_saveButton, "Save");
        SalrynUi.ConfigureSecondaryButton(_cancelButton, "Cancel");
        SalrynUi.ConfigureSecondaryButton(_editButton, "Edit");
        SalrynUi.ConfigureSecondaryButton(_refreshButton, "Refresh");
        _toolTip.SetToolTip(_saveButton, "Save your changes to store settings.");
        _toolTip.SetToolTip(_cancelButton, "Discard changes and leave edit mode.");
        _toolTip.SetToolTip(_editButton, "Unlock the fields to edit store settings.");
        _toolTip.SetToolTip(_refreshButton, "Reload store settings from the database.");
        footer.Controls.Add(_saveButton);
        footer.Controls.Add(_cancelButton);
        footer.Controls.Add(_editButton);
        footer.Controls.Add(_refreshButton);
        footer.Controls.Add(J20MvpUi.InfoButton(
            "About Store Settings",
            "These fields appear on receipt headers and local system displays." + Environment.NewLine +
            Environment.NewLine +
            "• Receipt Width must be 58 or 80 millimeters." + Environment.NewLine +
            "• Tax Mode and Shift Mode accept Enabled or Disabled." + Environment.NewLine +
            Environment.NewLine +
            "Click Edit to unlock the fields, then Save to apply your changes."));

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        SalrynUi.SetStatusNeutral(_statusLabel, string.Empty);
        SalrynUi.ConfigureFooterStatus(_statusLabel);
        footer.Controls.Add(_statusLabel);

        root.Controls.Add(footer, 0, 3);
    }

    private void AddField(TableLayoutPanel form, int row, string labelText)
    {
        Label label = SalrynUi.CreateFieldLabel(labelText);

        TextBox textBox = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Segoe UI", 10F),
            BackColor = SalrynUi.Colors.Surface,
            BorderStyle = BorderStyle.FixedSingle
        };

        form.Controls.Add(label, 0, row);
        form.Controls.Add(textBox, 1, row);

        _fields[labelText] = textBox;
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await LoadAsync().ConfigureAwait(true);
        _editButton.Click += (_, _) =>
        {
            if (_currentSettings is null)
            {
                SetWarning("Store settings must be loaded before editing.");
                return;
            }

            SetEditMode(true);
            SetStatus("Editing store settings.");
        };
        _cancelButton.Click += (_, _) =>
        {
            if (_currentSettings is not null)
            {
                PopulateFields(_currentSettings);
            }

            SetEditMode(false);
            SetStatus("Store settings edit canceled.");
        };
        _saveButton.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        SetBusy(true);
        SetStatus("Loading store settings...");

        try
        {
            var result = await _services.StoreSettingsReadRepository.GetCurrentAsync().ConfigureAwait(true);

            if (result.IsFailure)
            {
                _currentSettings = null;
                ClearFields("Unavailable");
                SetError(result.ErrorDetail.Code + ": " + result.ErrorDetail.Message);
                SetEditMode(false);
                return;
            }

            if (result.Value is null)
            {
                _currentSettings = null;
                ClearFields("Not configured");
                SetWarning("Store settings are not configured yet. First-run setup is required.");
                SetEditMode(false);
                return;
            }

            _currentSettings = result.Value;
            PopulateFields(_currentSettings);
            SetEditMode(false);
            SetStatus("Store settings loaded.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveAsync()
    {
        if (!TryBuildUpdateRequest(out StoreSettingsUpdateRequest? request))
        {
            return;
        }

        SetBusy(true);
        SetStatus("Saving store settings...");

        try
        {
            Result result = await _services.StoreSettingsWriteRepository.UpdateAsync(request!).ConfigureAwait(true);

            if (result.IsFailure)
            {
                SetError(result.ErrorDetail.Code + ": " + result.ErrorDetail.Message);
                return;
            }

            SetEditMode(false);
            await LoadAsync().ConfigureAwait(true);
            SetSuccess("Store settings saved.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryBuildUpdateRequest(out StoreSettingsUpdateRequest? request)
    {
        request = null;

        if (_currentSettings is null)
        {
            SetWarning("Store settings must be loaded before saving.");
            return false;
        }

        string storeName = Field("Store Name");
        string currency = Field("Currency");

        if (string.IsNullOrWhiteSpace(storeName))
        {
            SetWarning("Store name is required.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            SetWarning("Currency is required.");
            return false;
        }

        if (!TryParseReceiptWidth(Field("Receipt Width"), out int receiptWidthMm))
        {
            SetWarning("Receipt width must be 58 or 80 millimeters.");
            return false;
        }

        if (!TryParseMode(Field("Tax Mode"), out bool taxModeEnabled))
        {
            SetWarning("Tax Mode must be Enabled or Disabled.");
            return false;
        }

        if (!TryParseMode(Field("Shift Mode"), out bool shiftModeEnabled))
        {
            SetWarning("Shift Mode must be Enabled or Disabled.");
            return false;
        }

        request = new StoreSettingsUpdateRequest(
            _currentSettings.Id,
            storeName,
            NullableField("Business Name"),
            NullableField("Address"),
            NullableField("Contact Number"),
            NullableField("TIN"),
            currency,
            receiptWidthMm,
            taxModeEnabled,
            shiftModeEnabled);

        return true;
    }

    private void PopulateFields(StoreSettingsReadModel settings)
    {
        Set("Store Name", settings.StoreName);
        Set("Business Name", settings.BusinessName);
        Set("Address", settings.Address);
        Set("Contact Number", settings.ContactNumber);
        Set("TIN", settings.Tin);
        Set("Currency", settings.CurrencyLabel);
        Set("Receipt Width", settings.ReceiptWidthMm.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set("Tax Mode", settings.TaxModeEnabled ? "Enabled" : "Disabled");
        Set("Shift Mode", settings.ShiftModeEnabled ? "Enabled" : "Disabled");
    }

    private void ClearFields(string value)
    {
        foreach (TextBox textBox in _fields.Values)
        {
            textBox.Text = value;
        }
    }

    private void Set(string key, string? value)
    {
        if (_fields.TryGetValue(key, out TextBox? textBox))
        {
            textBox.Text = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }
    }

    private string Field(string key)
    {
        return _fields.TryGetValue(key, out TextBox? textBox)
            ? textBox.Text.Trim()
            : string.Empty;
    }

    private string? NullableField(string key)
    {
        string value = Field(key);

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private void SetEditMode(bool isEditing)
    {
        _isEditing = isEditing;

        foreach (TextBox textBox in _fields.Values)
        {
            textBox.ReadOnly = !isEditing;
            textBox.BackColor = isEditing ? Color.White : Color.FromArgb(249, 250, 251);
        }

        UpdateButtons();
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _refreshButton.Enabled = !_isBusy && !_isEditing;
        _editButton.Enabled = !_isBusy && !_isEditing && _currentSettings is not null;
        _saveButton.Enabled = !_isBusy && _isEditing;
        _cancelButton.Enabled = !_isBusy && _isEditing;
    }

    private void SetStatus(string message)
    {
        SalrynUi.SetStatusNeutral(_statusLabel, message);
    }

    private void SetSuccess(string message)
    {
        SalrynUi.SetStatusSuccess(_statusLabel, message);
    }

    private void SetWarning(string message)
    {
        SalrynUi.SetStatusWarning(_statusLabel, message);
    }

    private void SetError(string message)
    {
        SalrynUi.SetStatusError(_statusLabel, message);
    }

    private static bool TryParseReceiptWidth(string value, out int receiptWidthMm)
    {
        string normalized = value.Replace("mm", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

        if (int.TryParse(normalized, out receiptWidthMm) && receiptWidthMm is 58 or 80)
        {
            return true;
        }

        receiptWidthMm = 0;
        return false;
    }

    private static bool TryParseMode(string value, out bool enabled)
    {
        string normalized = value.Trim();

        if (normalized.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("True", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Yes", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (normalized.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("False", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("No", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }
}
