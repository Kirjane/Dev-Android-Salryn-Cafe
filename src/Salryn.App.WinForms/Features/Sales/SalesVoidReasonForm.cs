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
        Size = new Size(520, 260);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout(receiptNo);
        WireEvents();
    }

    public string VoidReason => _reasonTextBox.Text.Trim();

    private void BuildLayout(string receiptNo)
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 4);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        Controls.Add(root);

        Label header = new()
        {
            Dock = DockStyle.Fill,
            Text = "Void sale " + receiptNo,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = J20MvpUi.Colors.TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        };

        root.Controls.Add(header, 0, 0);

        _reasonTextBox.Dock = DockStyle.Fill;
        _reasonTextBox.Multiline = true;
        _reasonTextBox.MaxLength = 500;
        _reasonTextBox.PlaceholderText = "Required reason...";
        _reasonTextBox.Font = new Font("Segoe UI", 10F);

        root.Controls.Add(_reasonTextBox, 0, 1);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        root.Controls.Add(_statusLabel, 0, 2);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        J20MvpUi.ConfigurePrimaryButton(_voidButton, "Void Sale");
        J20MvpUi.ConfigureSecondaryButton(_cancelButton, "Cancel");

        actions.Controls.Add(_voidButton);
        actions.Controls.Add(_cancelButton);

        root.Controls.Add(actions, 0, 3);
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
                _statusLabel.Text = "Void reason is required.";
                _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
    }
}
