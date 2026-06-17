namespace Salryn.App.WinForms.Features.Mvp;

// Kirch file guide: shared minimal UI helper for new MVP skeleton screens.
// UI location: used by Dashboard, Inventory, Sales History, Receipt Preview, and Settings.
// Frontend note: centralize temporary shared styling here until a fuller design system replaces it.

// Kirch: Shared minimal WinForms styling for the June 20 MVP skeleton.
// The frontend engineer may replace visual details later, but should keep forms thin and AppServices-driven.
public static class J20MvpUi
{
    public static class Colors
    {
        public static readonly Color AppBackground = Color.FromArgb(246, 247, 249);
        public static readonly Color CardBackground = Color.White;
        public static readonly Color Header = Color.FromArgb(31, 41, 55);
        public static readonly Color Border = Color.FromArgb(209, 213, 219);
        public static readonly Color TextStrong = Color.FromArgb(17, 24, 39);
        public static readonly Color TextMuted = Color.FromArgb(75, 85, 99);
        public static readonly Color Primary = Color.FromArgb(31, 41, 55);
        public static readonly Color Success = Color.FromArgb(22, 101, 52);
        public static readonly Color Warning = Color.FromArgb(180, 83, 9);
        public static readonly Color Danger = Color.FromArgb(185, 28, 28);
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
        button.Width = 150;
        button.Height = 36;
        button.Text = text;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.BackColor = Colors.Primary;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Colors.Primary;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    public static void ConfigureSecondaryButton(Button button, string text)
    {
        button.Width = 150;
        button.Height = 36;
        button.Text = text;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        button.BackColor = Color.White;
        button.ForeColor = Colors.TextStrong;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Colors.Border;
        button.Margin = new Padding(8, 0, 0, 0);
    }
}
