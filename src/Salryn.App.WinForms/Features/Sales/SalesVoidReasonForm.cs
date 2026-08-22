using Salryn.App.WinForms.Features.Mvp;

namespace Salryn.App.WinForms.Features.Sales;

// Kirch: UI-only reason prompt for controlled sale voids. Persistence stays in ISaleWriteRepository.
public sealed class SalesVoidReasonForm : Form
{
    private readonly TextBox _reasonTextBox = new();
    private readonly Button _voidButton = new();
    private readonly Button _cancelButton = new();
    private readonly Label _statusLabel = new();

    public SalesVoidReasonForm(string receiptNo)
    {
        Text = "Void Sale";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Size = new Size(520, 280);
        Font = new Font("Segoe UI", 9F);
        BackColor = SalrynUi.Colors.Surface;

        BuildLayout(receiptNo);
        WireEvents();
    }

    public string VoidReason => _reasonTextBox.Text.Trim();

    private void BuildLayout(string receiptNo)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        Controls.Add(root);

        // Dialog System reference: danger title treatment for a destructive action.
        Label header = new()
        {
            Dock = DockStyle.Fill,
            Text = "Void sale " + receiptNo,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = SalrynUi.Colors.Danger,
            TextAlign = ContentAlignment.MiddleLeft
        };

        root.Controls.Add(header, 0, 0);

        root.Controls.Add(SalrynUi.CreateFieldLabel("Reason", required: true), 0, 1);

        _reasonTextBox.Dock = DockStyle.Fill;
        _reasonTextBox.Multiline = true;
        _reasonTextBox.MaxLength = 500;
        _reasonTextBox.PlaceholderText = "Required reason...";
        _reasonTextBox.Font = new Font("Segoe UI", 10F);
        _reasonTextBox.BorderStyle = BorderStyle.FixedSingle;

        root.Controls.Add(_reasonTextBox, 0, 2);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        SalrynUi.SetStatusNeutral(_statusLabel, string.Empty);
        root.Controls.Add(_statusLabel, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = SalrynUi.Colors.Surface
        };

        // Void = danger action; Cancel = safe secondary.
        SalrynUi.ConfigureDangerButton(_voidButton, "Void Sale");
        SalrynUi.ConfigureSecondaryButton(_cancelButton, "Cancel");

        actions.Controls.Add(_voidButton);
        actions.Controls.Add(_cancelButton);

        root.Controls.Add(actions, 0, 4);
    }

    private void WireEvents()
    {
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        _voidButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_reasonTextBox.Text))
            {
                SalrynUi.SetStatusError(_statusLabel, "Void reason is required.");
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
    }
}
