namespace Salryn.App.WinForms.Features.Mvp;

/// <summary>
/// Salryn Ledger Console visual system and action UX helpers.
/// "receipt-paper surfaces, ledger-slate structure, dense readable tables,
/// green/blue/amber/red action semantics, clear destructive separation".
/// This is the authoritative UI token + helper layer for UI-A1R+ phases.
/// </summary>
public static class SalrynUi
{
    public static class Colors
    {
        // Ledger console palette - aligned to visual reference pack
        public static readonly Color AppBackground = Color.FromArgb(246, 248, 250);
        public static readonly Color Surface = Color.FromArgb(255, 255, 255);
        public static readonly Color SurfaceMuted = Color.FromArgb(248, 250, 252);
        public static readonly Color HeaderSlate = Color.FromArgb(15, 23, 42);   // deeper ink/navy
        public static readonly Color Border = Color.FromArgb(203, 213, 225);
        public static readonly Color BorderFocus = Color.FromArgb(59, 130, 246); // focused blue border
        public static readonly Color TextStrong = Color.FromArgb(15, 23, 42);
        public static readonly Color TextMuted = Color.FromArgb(71, 85, 105);
        public static readonly Color Primary = Color.FromArgb(30, 58, 138);   // deep blue for command actions
        public static readonly Color Success = Color.FromArgb(16, 185, 129);  // green for create/add
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);  // amber
        public static readonly Color Danger = Color.FromArgb(185, 28, 28);    // red for destructive
        public static readonly Color Required = Color.FromArgb(185, 28, 28);  // red asterisk
    }

    public static class Typography
    {
        public static readonly Font Title = new("Segoe UI", 18F, FontStyle.Bold);
        public static readonly Font Section = new("Segoe UI", 12F, FontStyle.Bold);
        public static readonly Font Body = new("Segoe UI", 10F, FontStyle.Regular);
        public static readonly Font Small = new("Segoe UI", 9F, FontStyle.Regular);
        public static readonly Font Button = new("Segoe UI", 10F, FontStyle.Bold);
    }

    public static class Spacing
    {
        public const int FormPadding = 18;
        public const int SectionGap = 12;
        public const int FieldHeight = 28;
        public const int ToolbarHeight = 52;
        public const int ButtonHeight = 36;
        public const int LabelColumnWidth = 170;
    }

    // --- Button helpers (action semantics) ---

    public static void ConfigurePrimaryButton(Button button, string text)
    {
        button.Text = text;
        button.Font = Typography.Button;
        button.BackColor = Colors.Primary;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Colors.Primary;
        button.Height = Spacing.ButtonHeight;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    public static void ConfigureSecondaryButton(Button button, string text)
    {
        button.Text = text;
        button.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        button.BackColor = Colors.Surface;
        button.ForeColor = Colors.TextStrong;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Colors.Border;
        button.Height = Spacing.ButtonHeight;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    public static void ConfigureSuccessButton(Button button, string text)
    {
        button.Text = text;
        button.Font = Typography.Button;
        button.BackColor = Colors.Success;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Colors.Success;
        button.Height = Spacing.ButtonHeight;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    public static void ConfigureDangerButton(Button button, string text)
    {
        button.Text = text;
        button.Font = Typography.Button;
        button.BackColor = Colors.Danger;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Colors.Danger;
        button.Height = Spacing.ButtonHeight;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    // --- Grid (dense ledger style) ---

    public static void ConfigureLedgerGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.BackgroundColor = Colors.Surface;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.EditMode = DataGridViewEditMode.EditProgrammatically;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.EnableHeadersVisualStyles = false;

        // Reference-aligned header and selection
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Colors.TextStrong;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 4, 6, 4);

        grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        grid.DefaultCellStyle.ForeColor = Colors.TextStrong;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = Colors.TextStrong;

        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 252);
        grid.RowTemplate.Height = 28;

        // Subtle focus ring feel on the grid container level is handled by parent
    }

    // --- Status helpers ---

    public static void SetStatusNeutral(Label label, string message)
    {
        label.Text = message;
        label.ForeColor = Colors.TextMuted;
    }

    public static void SetStatusSuccess(Label label, string message)
    {
        label.Text = message;
        label.ForeColor = Colors.Success;
    }

    public static void SetStatusWarning(Label label, string message)
    {
        label.Text = message;
        label.ForeColor = Colors.Warning;
    }

    public static void SetStatusError(Label label, string message)
    {
        label.Text = message;
        label.ForeColor = Colors.Danger;
    }

    // --- Action / Dialog helpers ---

    /// <summary>
    /// High-contrast dangerous action confirmation. "No" is the safe default.
    /// </summary>
    public static bool ConfirmDanger(
        IWin32Window owner,
        string title,
        string message,
        string confirmText = "Deactivate")
    {
        // Build a custom dialog to allow custom confirm text while keeping safe default on No.
        using var dlg = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Size = new Size(420, 180)
        };

        // Reference-aligned dangerous dialog
        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Text = title,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Colors.TextStrong,
            Padding = new Padding(4, 4, 4, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var msg = new Label
        {
            Dock = DockStyle.Fill,
            Text = message,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Typography.Body
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };

        var yes = new Button { Text = confirmText, Width = 110, Height = 32, DialogResult = DialogResult.Yes };
        SalrynUi.ConfigureDangerButton(yes, confirmText);
        yes.Width = 110; yes.Height = 32;

        var no = new Button { Text = "Cancel", Width = 90, Height = 32, DialogResult = DialogResult.No };
        SalrynUi.ConfigureSecondaryButton(no, "Cancel");
        no.Width = 90; no.Height = 32;

        buttons.Controls.Add(no);
        buttons.Controls.Add(yes);

        layout.Controls.Add(msg, 0, 0);
        layout.Controls.Add(buttons, 0, 2);

        dlg.Controls.Add(titleLabel);
        dlg.Controls.Add(layout);

        dlg.AcceptButton = no; // safe default
        dlg.CancelButton = no;

        return dlg.ShowDialog(owner) == DialogResult.Yes;
    }

    /// <summary>
    /// Reason prompt that returns false (and empty reason) on cancel or blank.
    /// Caller must still check for whitespace.
    /// </summary>
    public static bool PromptRequiredReason(
        IWin32Window owner,
        string title,
        string prompt,
        out string reason)
    {
        reason = string.Empty;

        using var dlg = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Size = new Size(460, 240)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = prompt,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Typography.Body
        };

        var input = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };

        var ok = new Button { Text = "OK", Width = 100, Height = 32, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(input, 0, 1);
        layout.Controls.Add(buttons, 0, 3);
        dlg.Controls.Add(layout);

        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;

        if (dlg.ShowDialog(owner) != DialogResult.OK)
        {
            reason = string.Empty;
            return false;
        }

        reason = input.Text?.Trim() ?? string.Empty;
        return true;
    }

    public static void ShowBlocked(IWin32Window owner, string title, string message)
    {
        MessageBox.Show(
            owner,
            message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public static void ShowError(IWin32Window owner, string title, string message)
    {
        MessageBox.Show(
            owner,
            message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}