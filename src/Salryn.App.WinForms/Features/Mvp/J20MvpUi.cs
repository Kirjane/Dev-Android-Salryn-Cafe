namespace Salryn.App.WinForms.Features.Mvp;

// Kirch file guide: shared minimal UI helper for new MVP skeleton screens.
// UI location: used by Dashboard, Inventory, Sales History, Receipt Preview, and Settings.
// Frontend note: centralize temporary shared styling here until a fuller design system replaces it.

// Kirch: Shared minimal WinForms styling for the June 20 MVP skeleton.
// The frontend engineer may replace visual details later, but should keep forms thin and AppServices-driven.
public static class J20MvpUi
{
    // Kirch design map: shared visual tokens for skeleton screens.
    // UI side: new Dashboard/Inventory/Sales/Settings screens reuse these colors and helper factories.
    // Frontend safe zone: replace these values or migrate to a fuller design system later.
    // Do not change: this helper should stay UI-only and must not pull services or data.
    public static class Colors
    {
        // UI-A1: "Modern Local Retail Console" palette - professional, high contrast, calm retail ops.
        public static readonly Color AppBackground = Color.FromArgb(250, 250, 252);
        public static readonly Color CardBackground = Color.White;
        public static readonly Color Header = Color.FromArgb(30, 41, 59);
        public static readonly Color Border = Color.FromArgb(203, 213, 225);
        public static readonly Color TextStrong = Color.FromArgb(15, 23, 42);
        public static readonly Color TextMuted = Color.FromArgb(71, 85, 105);
        public static readonly Color Primary = Color.FromArgb(30, 41, 59);
        public static readonly Color Accent = Color.FromArgb(51, 65, 85);
        public static readonly Color Success = Color.FromArgb(16, 185, 129);
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    }

    public static TableLayoutPanel RootLayout(int rowCount)
    {
        return new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rowCount,
            Padding = new Padding(18),
            BackColor = Colors.AppBackground
        };
    }

    public static Panel Header(string title, string subtitle)
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 12, 18, 12),
            BackColor = Colors.Header
        };

        Label titleLabel = new()
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = title,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Label subtitleLabel = new()
        {
            Dock = DockStyle.Fill,
            Text = subtitle,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(209, 213, 219),
            TextAlign = ContentAlignment.MiddleLeft
        };

        panel.Controls.Add(subtitleLabel);
        panel.Controls.Add(titleLabel);

        return panel;
    }

    public static Panel Card()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 12, 12),
            BackColor = Colors.CardBackground,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    public static Panel MetricCard(string title, Label valueLabel, string subtitle)
    {
        Panel card = Card();

        Label titleLabel = new()
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = title,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Colors.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        };

        valueLabel.Dock = DockStyle.Top;
        valueLabel.Height = 54;
        valueLabel.Text = "--";
        valueLabel.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
        valueLabel.ForeColor = Colors.TextStrong;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;

        Label subtitleLabel = new()
        {
            Dock = DockStyle.Fill,
            Text = subtitle,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Colors.TextMuted,
            TextAlign = ContentAlignment.TopLeft
        };

        card.Controls.Add(subtitleLabel);
        card.Controls.Add(valueLabel);
        card.Controls.Add(titleLabel);

        return card;
    }

    public static Label BodyText(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
            ForeColor = Colors.TextMuted,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(2)
        };
    }

    public static DataGridView Grid()
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };

        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 244, 246);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Colors.TextStrong;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.DefaultCellStyle.Font = new Font("Segoe UI", 9.25F, FontStyle.Regular);

        return grid;
    }

    public static void ConfigurePrimaryButton(Button button, string text)
    {
        // Compatibility delegation to SalrynUi (Ledger Console)
        SalrynUi.ConfigurePrimaryButton(button, text);
    }

    public static void ConfigureSecondaryButton(Button button, string text)
    {
        // Compatibility delegation to SalrynUi (Ledger Console)
        SalrynUi.ConfigureSecondaryButton(button, text);
    }

    // Kirch B7 (Presentation & Operator UX): shared operator-facing copy so empty/loading/error
    // states read the same way on every screen instead of each form inventing its own wording.
    public static class StateText
    {
        public const string Loading = "Loading...";
        public const string NothingToShow = "Nothing to show yet.";
        public const string LoadFailedRetry =
            "Could not load this data. Please retry, then contact support if it keeps failing.";
    }

    // Kirch B7: one hover-guidance tooltip provider per form. Forms keep a single instance and
    // attach short hints to buttons/inputs so operators understand controls without a manual.
    public static ToolTip CreateToolTip()
    {
        return new ToolTip
        {
            AutoPopDelay = 12000,
            InitialDelay = 400,
            ReshowDelay = 120,
            ShowAlways = true
        };
    }

    // Kirch B7: small round "?" info button that opens a plain help popup with static guidance text,
    // replacing screens that previously relied only on inline explanatory labels.
    public static Button InfoButton(string helpTitle, string helpBody)
    {
        Button button = new()
        {
            Width = 30,
            Height = 30,
            Text = "?",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Colors.Primary,
            Margin = new Padding(8, 0, 0, 0),
            TabStop = false
        };

        button.FlatAppearance.BorderColor = Colors.Border;
        button.Click += (_, _) => MessageBox.Show(
            button.FindForm(),
            helpBody,
            helpTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        return button;
    }

    // Kirch B7: standardized confirmation popup for risky or irreversible operator actions.
    // Defaults to the safe "No" button so an accidental Enter does not trigger the action.
    public static bool ConfirmDestructiveAction(
        IWin32Window owner,
        string title,
        string message)
    {
        return MessageBox.Show(
            owner,
            message,
            title,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    // Kirch B7: standardized blocked-action popup so operator-facing failures (e.g. missing
    // permission or no selection) are clear and consistent rather than only a small status label.
    public static void ShowBlockedAction(
        IWin32Window owner,
        string title,
        string message)
    {
        MessageBox.Show(
            owner,
            message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // Kirch B7: consistent centered placeholder so screens never present an ugly blank panel.
    public static Label EmptyStateLabel(string message)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = message,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
            ForeColor = Colors.TextMuted,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Colors.CardBackground
        };
    }

    // UI-A1: Modern Local Retail Console nav button styling.
    // High contrast, clear active state, professional dense nav.
    public static void ConfigureNavButton(Button button, string text, bool isActive = false)
    {
        button.Width = 260;
        button.Height = 52;
        button.Margin = new Padding(0, 0, 0, 8);
        button.Text = text;
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(16, 0, 16, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI", 10F, isActive ? FontStyle.Bold : FontStyle.Regular);
        button.BackColor = isActive ? Color.FromArgb(241, 245, 249) : Color.White;
        button.ForeColor = isActive ? Colors.TextStrong : Color.FromArgb(51, 65, 85);
        button.FlatAppearance.BorderColor = isActive ? Color.FromArgb(148, 163, 184) : Colors.Border;
        button.FlatAppearance.BorderSize = 1;
    }

    // UI-A1: subtle left accent bar for cards to give visual weight without heavy borders.
    public static Panel CardWithAccent()
    {
        Panel outer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4, 0, 0, 0),
            BackColor = Colors.Border,
            Margin = new Padding(0, 0, 12, 12)
        };

        Panel inner = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            BackColor = Colors.CardBackground
        };

        outer.Controls.Add(inner);
        return outer;
    }

    // UI-A1: improved grid for dense but readable tables.
    public static DataGridView DenseGrid()
    {
        DataGridView grid = Grid();
        grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 252);
        grid.RowTemplate.Height = 28;
        return grid;
    }
}
