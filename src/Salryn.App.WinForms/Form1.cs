using Salryn.App.WinForms.Composition;

namespace Salryn.App.WinForms;

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
