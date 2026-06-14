using Salryn.Host.Local.Composition;

namespace Salryn.App.WinForms.Composition;

// Kirch: Holds a validated Local Host composition result without starting or consuming runtime behavior.
public sealed class SalrynLocalHostCompositionHolder
{
    public SalrynLocalHostCompositionHolder(LocalHostBootstrapCompositionResult composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (!LocalHostBootstrapCompositionRules.IsCompositionValid(composition))
        {
            throw new InvalidOperationException("Invalid held app Local Host composition.");
        }

        Composition = composition;
    }

    public LocalHostBootstrapCompositionResult Composition { get; }
}
