using Salryn.Host.Local.Composition;

namespace Salryn.App.WinForms.Composition;

// Kirch file guide: holder for Local Host product lookup composition.
// UI location: invisible infrastructure used by Checkout lookup wiring.
// Frontend note: no visual styling belongs here.

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
