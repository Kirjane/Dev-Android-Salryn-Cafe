using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Settings;

// Kirch file guide: store profile/settings screen.
// UI location: opened from Shell > Settings.
// Frontend note: current fields are read-only; add editing only through a proper settings write service.

// Kirch: Minimal read-only Store Settings skeleton for June 20 MVP.
// Write/edit settings can be added after the demo path is stable.
public sealed class StoreSettingsForm : Form
{
    private readonly SalrynAppServices _services;

    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    // Kirch UI map: store settings field registry.
    // UI side: each text box displays one store/receipt header field in read-only mode.
    // Frontend safe zone: grouping, labels, helper text, and future edit affordances.
    // Do not change: editing needs a proper settings write service before enabling inputs.
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.OrdinalIgnoreCase);

    public StoreSettingsForm(SalrynAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Text = "SALRYN Store Settings";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 620);
        Size = new Size(840, 680);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadAsync().ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 4);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Store Settings",
            "Store profile and receipt header settings."), 0, 0);

        Panel card = J20MvpUi.Card();
        TableLayoutPanel form = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            BackColor = Color.White
        };

        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

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

        for (int row = 0; row < labels.Length; row++)
        {
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            AddField(form, row, labels[row]);
        }

        card.Controls.Add(form);
        root.Controls.Add(card, 0, 1);

        Label note = J20MvpUi.BodyText(
            "Review the store profile used for receipt headers and local system display." +
            "");
        note.Dock = DockStyle.Fill;
        root.Controls.Add(note, 0, 2);

        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        J20MvpUi.ConfigurePrimaryButton(_refreshButton, "Refresh");
        footer.Controls.Add(_refreshButton);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        footer.Controls.Add(_statusLabel);

        root.Controls.Add(footer, 0, 3);
    }

    private void AddField(TableLayoutPanel form, int row, string labelText)
    {
        Label label = new()
        {
            Dock = DockStyle.Fill,
            Text = labelText,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = J20MvpUi.Colors.TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        };

        TextBox textBox = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Segoe UI", 10F),
            BackColor = Color.White
        };

        form.Controls.Add(label, 0, row);
        form.Controls.Add(textBox, 1, row);

        _fields[labelText] = textBox;
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        _statusLabel.Text = "Loading store settings...";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;

        var result = await _services.StoreSettingsReadRepository.GetCurrentAsync().ConfigureAwait(true);

        if (result.IsFailure)
        {
            ClearFields("Unavailable");
            _statusLabel.Text = $"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}";
            _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
            return;
        }

        if (result.Value is null)
        {
            ClearFields("Not configured");
            _statusLabel.Text = "Store settings are not configured yet. First-run setup is required.";
            _statusLabel.ForeColor = J20MvpUi.Colors.Warning;
            return;
        }

        StoreSettingsReadModel settings = result.Value;

        Set("Store Name", settings.StoreName);
        Set("Business Name", settings.BusinessName);
        Set("Address", settings.Address);
        Set("Contact Number", settings.ContactNumber);
        Set("TIN", settings.Tin);
        Set("Currency", settings.CurrencyLabel);
        Set("Receipt Width", settings.ReceiptWidthMm + " mm");
        Set("Tax Mode", settings.TaxModeEnabled ? "Enabled" : "Disabled");
        Set("Shift Mode", settings.ShiftModeEnabled ? "Enabled" : "Disabled");

        _statusLabel.Text = "Store settings loaded.";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
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
                ? "--"
                : value.Trim();
        }
    }
}
