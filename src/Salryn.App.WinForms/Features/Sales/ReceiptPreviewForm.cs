using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Sales;

// Kirch file guide: text receipt preview modal.
// UI location: opened from Sales History View Receipt.
// Frontend note: improve preview/print layout here, but do not claim accreditation or bypass sale detail data.

// Kirch: Receipt preview is intentionally text-based for the June 20 MVP.
// Printing here uses a normal Windows printer queue and is not official BIR/RMO output printing.
public sealed class ReceiptPreviewForm : Form
{
    private readonly SaleDetailReadModel _sale;
    private readonly StoreSettingsReadModel? _storeSettings;
    private string _receiptText = string.Empty;

    // Kirch UI map: receipt preview controls.
    // UI side: previewTextBox shows the receipt body; printButton sends the same text to a Windows printer queue.
    // Frontend safe zone: typography, spacing, print preview layout, and printer action placement.
    // Do not change: receipt data must come from SaleDetailReadModel and store settings.
    private readonly TextBox _previewTextBox = new();
    private readonly Button _closeButton = new();
    private readonly Button _printButton = new();
    private readonly Label _statusLabel = new();

    public ReceiptPreviewForm(
        SaleDetailReadModel sale,
        StoreSettingsReadModel? storeSettings)
    {
        _sale = sale ?? throw new ArgumentNullException(nameof(sale));
        _storeSettings = storeSettings;

        Text = "Receipt Preview";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 700);
        Size = new Size(640, 780);
        Font = new Font("Consolas", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        RenderReceipt();
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 4);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));

        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Receipt Preview",
            "Review receipt details before printing to a Windows printer queue."), 0, 0);

        _previewTextBox.Dock = DockStyle.Fill;
        _previewTextBox.Multiline = true;
        _previewTextBox.ReadOnly = true;
        _previewTextBox.ScrollBars = ScrollBars.Vertical;
        _previewTextBox.Font = new Font("Consolas", 9.5F);
        _previewTextBox.BackColor = Color.White;

        root.Controls.Add(_previewTextBox, 0, 1);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        root.Controls.Add(_statusLabel, 0, 2);

        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        J20MvpUi.ConfigurePrimaryButton(_closeButton, "Close");
        J20MvpUi.ConfigureSecondaryButton(_printButton, "Print");

        _closeButton.Click += (_, _) => Close();
        _printButton.Click += (_, _) => PrintReceipt();

        footer.Controls.Add(_closeButton);
        footer.Controls.Add(_printButton);

        root.Controls.Add(footer, 0, 3);
    }

    private void RenderReceipt()
    {
        _receiptText = string.IsNullOrWhiteSpace(_sale.ReceiptSnapshot)
            ? BuildFallbackReceiptText()
            : _sale.ReceiptSnapshot;

        _previewTextBox.Text = _receiptText;
        _statusLabel.Text = "Receipt ready.";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
    }

    private void PrintReceipt()
    {
        if (string.IsNullOrWhiteSpace(_receiptText))
        {
            SetError("Receipt text is empty.");
            return;
        }

        if (PrinterSettings.InstalledPrinters.Count == 0)
        {
            SetError("No installed Windows printer queue is available.");
            return;
        }

        try
        {
            using PrintDocument document = CreatePrintDocument(_receiptText);
            using PrintDialog dialog = new()
            {
                Document = document,
                AllowCurrentPage = false,
                AllowPrintToFile = false,
                AllowSelection = false,
                AllowSomePages = false,
                UseEXDialog = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                _statusLabel.Text = "Receipt print cancelled.";
                _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
                return;
            }

            document.Print();
            _statusLabel.Text = "Receipt sent to Windows printer queue.";
            _statusLabel.ForeColor = J20MvpUi.Colors.Success;
        }
        catch (InvalidPrinterException ex)
        {
            SetError("Printer is unavailable: " + ex.Message);
        }
        catch (Win32Exception ex)
        {
            SetError("Windows printing failed: " + ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            SetError("Receipt printing failed: " + ex.Message);
        }
    }

    private PrintDocument CreatePrintDocument(string receiptText)
    {
        string[] lines = NormalizeLines(receiptText);
        int lineIndex = 0;

        PrintDocument document = new()
        {
            DocumentName = "SALRYN Receipt " + _sale.ReceiptNo
        };

        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is null)
            {
                e.HasMorePages = false;
                return;
            }

            using Font printFont = new("Consolas", 9F);
            float lineHeight = printFont.GetHeight(e.Graphics);
            RectangleF bounds = e.MarginBounds;
            float y = bounds.Top;

            while (lineIndex < lines.Length)
            {
                if (y + lineHeight > bounds.Bottom)
                {
                    e.HasMorePages = true;
                    return;
                }

                e.Graphics.DrawString(
                    lines[lineIndex],
                    printFont,
                    Brushes.Black,
                    bounds.Left,
                    y);

                lineIndex++;
                y += lineHeight;
            }

            e.HasMorePages = false;
        };

        return document;
    }

    private static string[] NormalizeLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private string BuildFallbackReceiptText()
    {
        StringBuilder text = new();

        text.AppendLine(Center(TextOrDefault(_storeSettings?.StoreName, "SALRYN STORE")));
        text.AppendLine(Center(TextOrDefault(_storeSettings?.BusinessName, "Local POS MVP")));
        text.AppendLine(Center(TextOrDefault(_storeSettings?.Address, "Address not configured")));
        text.AppendLine(Center("TIN: " + TextOrDefault(_storeSettings?.Tin, "Not configured")));
        text.AppendLine(new string('-', 42));
        text.AppendLine($"Receipt No : {_sale.ReceiptNo}");
        text.AppendLine($"Date       : {_sale.SaleDate.ToLocalTime():yyyy-MM-dd HH:mm}");
        text.AppendLine($"Cashier    : {_sale.CashierDisplayName}");
        text.AppendLine(new string('-', 42));

        foreach (SaleItemDetailReadModel item in _sale.Items)
        {
            text.AppendLine(item.ProductNameSnapshot);
            text.AppendLine($"  {item.Quantity} {item.UnitNameSnapshot} x {item.UnitPrice} = {item.LineTotal}");
        }

        text.AppendLine(new string('-', 42));
        text.AppendLine($"Subtotal   : {_sale.Subtotal}");
        text.AppendLine($"Discount   : {_sale.Discount}");
        text.AppendLine($"Tax        : {_sale.Tax}");
        text.AppendLine($"TOTAL      : {_sale.Total}");
        text.AppendLine($"Paid       : {_sale.AmountPaid}");
        text.AppendLine($"Change     : {_sale.Change}");
        text.AppendLine(new string('-', 42));
        text.AppendLine("Receipt preview generated by SALRYN.");
        text.AppendLine("Please verify all transaction details.");
        text.AppendLine(Center("Thank you."));

        return text.ToString();
    }

    private void SetError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
    }

    private static string Center(string value)
    {
        const int width = 42;

        if (value.Length >= width)
        {
            return value;
        }

        int left = (width - value.Length) / 2;

        return new string(' ', left) + value;
    }

    private static string TextOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}
