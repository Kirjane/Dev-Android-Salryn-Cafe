using System.Globalization;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Shifts;

// Kirch file guide: shift gate form before entering POS Checkout.
// UI location: modal shown from Shell when cashier opens POS Checkout.
// Frontend note: fields are opening cash and notes; do not bypass the presenter to enter checkout.

public sealed class CashierShiftGateForm : Form
{
    private readonly CashierShiftGatePresenter _presenter;

    // Kirch UI map: cashier shift gate controls.
    // UI side: modal before Checkout with user details, opening cash, notes, and enter-checkout actions.
    // Frontend safe zone: form layout, helper text, and button emphasis.
    // Do not change: entering checkout still requires presenter-approved open shift state.
    private Label _titleLabel = null!;
    private Label _userLabel = null!;
    private Label _statusLabel = null!;
    private Label _detailLabel = null!;
    private Label _messageLabel = null!;
    private TextBox _openingCashTextBox = null!;
    private TextBox _countedCashTextBox = null!;
    private TextBox _notesTextBox = null!;
    private Button _openShiftButton = null!;
    private Button _closeShiftButton = null!;
    private Button _enterCheckoutButton = null!;
    private Button _cancelButton = null!;

    public CashierShiftGateForm(CashierShiftGatePresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

        InitializeComponent();

        Shown += async (_, _) => await LoadStateAsync();
    }

    public CashierSessionReadModel? OpenSession { get; private set; }

    // Kirch: Builds the modal cashier shift gate shown before POS checkout.
    private void InitializeComponent()
    {
        Text = "SALRYN Cashier Shift";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 610);
        BackColor = Color.FromArgb(246, 247, 249);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(28)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        Controls.Add(root);

        _titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Cashier Shift Gate",
            Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _userLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Checking user...",
            ForeColor = Color.FromArgb(75, 85, 99),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Checking shift...",
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _detailLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = string.Empty,
            ForeColor = Color.FromArgb(75, 85, 99),
            TextAlign = ContentAlignment.TopLeft
        };

        root.Controls.Add(_titleLabel, 0, 0);
        root.Controls.Add(_userLabel, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        root.Controls.Add(_detailLabel, 0, 3);

        root.Controls.Add(BuildOpeningCashPanel(), 0, 4);
        root.Controls.Add(BuildCountedCashPanel(), 0, 5);
        root.Controls.Add(BuildNotesPanel(), 0, 6);

        _messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = string.Empty,
            ForeColor = Color.FromArgb(185, 28, 28),
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(0, 12, 0, 0)
        };

        root.Controls.Add(_messageLabel, 0, 7);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        _enterCheckoutButton = new Button
        {
            Width = 130,
            Height = 36,
            Text = "Enter Checkout",
            Enabled = false,
            BackColor = Color.FromArgb(31, 41, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };

        _enterCheckoutButton.FlatAppearance.BorderSize = 0;
        _enterCheckoutButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        _openShiftButton = new Button
        {
            Width = 110,
            Height = 36,
            Text = "Open Shift"
        };

        _openShiftButton.Click += async (_, _) => await OpenShiftAsync();

        _closeShiftButton = new Button
        {
            Width = 110,
            Height = 36,
            Text = "Close Shift",
            Enabled = false
        };

        _closeShiftButton.Click += async (_, _) => await CloseShiftAsync();

        _cancelButton = new Button
        {
            Width = 90,
            Height = 36,
            Text = "Cancel"
        };

        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        actions.Controls.Add(_enterCheckoutButton);
        actions.Controls.Add(_closeShiftButton);
        actions.Controls.Add(_openShiftButton);
        actions.Controls.Add(_cancelButton);

        root.Controls.Add(actions, 0, 8);
    }

    private Control BuildOpeningCashPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Opening cash",
            ForeColor = Color.FromArgb(55, 65, 81),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _openingCashTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = "0.00",
            Font = SystemFonts.MessageBoxFont
        };

        panel.Controls.Add(_openingCashTextBox, 0, 1);

        return panel;
    }

    private Control BuildCountedCashPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Counted cash",
            ForeColor = Color.FromArgb(55, 65, 81),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _countedCashTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = "0.00",
            Enabled = false,
            Font = SystemFonts.MessageBoxFont
        };

        panel.Controls.Add(_countedCashTextBox, 0, 1);

        return panel;
    }

    private Control BuildNotesPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Notes",
            ForeColor = Color.FromArgb(55, 65, 81),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _notesTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Font = SystemFonts.MessageBoxFont
        };

        panel.Controls.Add(_notesTextBox, 0, 1);

        return panel;
    }

    private async Task LoadStateAsync()
    {
        SetBusy(true, "Checking cashier shift...");

        Result<CashierShiftGateViewModel> result =
            await _presenter.LoadAsync();

        if (result.IsFailure)
        {
            SetBusy(false, result.Error);
            return;
        }

        Render(result.Value);
        SetBusy(false, string.Empty);
    }

    // Kirch workflow: open cashier shift.
    // UI side: Open Shift button sends opening cash and notes to the presenter.
    // Frontend note: validation display can improve, but shift creation must stay service-backed.
    private async Task OpenShiftAsync()
    {
        if (!decimal.TryParse(
            _openingCashTextBox.Text.Trim(),
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out decimal openingCash))
        {
            _messageLabel.Text = "Opening cash must be a valid number.";
            return;
        }

        SetBusy(true, "Opening cashier shift...");

        Result<CashierShiftGateViewModel> result =
            await _presenter.OpenShiftAsync(
                openingCash,
                _notesTextBox.Text);

        if (result.IsFailure)
        {
            SetBusy(false, result.Error);
            return;
        }

        Render(result.Value);
        SetBusy(false, "Shift is open. You can now enter checkout.");
    }

    // Kirch workflow: close cashier shift.
    // UI side: Close Shift button sends counted cash and notes to the presenter.
    // Frontend note: variance display comes from the closed session read-back, not UI calculation.
    private async Task CloseShiftAsync()
    {
        if (OpenSession is null)
        {
            _messageLabel.Text = "An open cashier shift is required before closing.";
            return;
        }

        if (!decimal.TryParse(
            _countedCashTextBox.Text.Trim(),
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out decimal countedCash))
        {
            _messageLabel.Text = "Counted cash must be a valid number.";
            return;
        }

        SetBusy(true, "Closing cashier shift...");

        Result<CashierShiftGateViewModel> result =
            await _presenter.CloseShiftAsync(
                OpenSession.Id,
                countedCash,
                _notesTextBox.Text);

        if (result.IsFailure)
        {
            SetBusy(false, result.Error);
            return;
        }

        Render(result.Value);
        SetBusy(false, "Shift is closed. Variance is shown above.");
    }

    private void Render(CashierShiftGateViewModel state)
    {
        OpenSession = state.OpenSession;

        _userLabel.Text = state.UserLine;
        _statusLabel.Text = state.StatusLine;
        _detailLabel.Text = state.DetailLine;

        _openingCashTextBox.Enabled = state.CanOpenShift;
        _countedCashTextBox.Enabled = state.CanCloseShift;
        _notesTextBox.Enabled = state.CanOpenShift || state.CanCloseShift;
        _openShiftButton.Enabled = state.CanOpenShift;
        _enterCheckoutButton.Enabled = state.CanEnterCheckout;
        _closeShiftButton.Enabled = state.CanCloseShift;

        if (state.OpenSession is not null)
        {
            _countedCashTextBox.Text = state.OpenSession.ExpectedCash.ToPesos().ToString("0.00", CultureInfo.CurrentCulture);
        }
        else if (state.ClosedSession is not null && state.ClosedSession.CountedCash.HasValue)
        {
            _countedCashTextBox.Text = state.ClosedSession.CountedCash.Value.ToPesos().ToString("0.00", CultureInfo.CurrentCulture);
        }
    }

    private void SetBusy(bool isBusy, string message)
    {
        _openShiftButton.Enabled = !isBusy && _openShiftButton.Enabled;
        _closeShiftButton.Enabled = !isBusy && _closeShiftButton.Enabled;
        _enterCheckoutButton.Enabled = !isBusy && _enterCheckoutButton.Enabled;
        _cancelButton.Enabled = !isBusy;
        _openingCashTextBox.Enabled = !isBusy && _openingCashTextBox.Enabled;
        _countedCashTextBox.Enabled = !isBusy && _countedCashTextBox.Enabled;
        _notesTextBox.Enabled = !isBusy && _notesTextBox.Enabled;
        _messageLabel.Text = message;
    }
}
