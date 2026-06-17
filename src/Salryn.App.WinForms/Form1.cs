using Salryn.App.WinForms.Composition;

namespace Salryn.App.WinForms;

// Kirch file guide: legacy/default form wrapper.
// UI location: not the main POS workflow; Shell/Auth forms own the real app flow.
// Frontend note: avoid polishing this unless it is intentionally revived.

public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
    }

    public Form1(SalrynAppServices services)
        : this()
    {
        AppServices = services ?? throw new ArgumentNullException(nameof(services));
    }

    internal SalrynAppServices? AppServices { get; }
}
