using Salryn.Host.Local.Runtime;

namespace Salryn.App.WinForms.Composition;

// Kirch: Passive runtime lifecycle shell; observes held runtime status only while activation remains blocked for a later phase.
public sealed class SalrynLocalHostRuntimeLifecycleHolder
{
    private readonly SalrynLocalHostCompositionHolder localHostCompositionHolder;

    public SalrynLocalHostRuntimeLifecycleHolder(
        SalrynLocalHostCompositionHolder localHostCompositionHolder)
    {
        ArgumentNullException.ThrowIfNull(localHostCompositionHolder);

        this.localHostCompositionHolder = localHostCompositionHolder;
    }

    public LocalHostRuntimeSnapshot Snapshot =>
        localHostCompositionHolder.Composition.Runtime.GetSnapshot();

    public LocalHostRuntimeState State => Snapshot.State;

    public bool IsRunning => State == LocalHostRuntimeState.Running;

    public bool IsStopped => State == LocalHostRuntimeState.Stopped;

    public bool IsFaulted => State == LocalHostRuntimeState.Faulted;
}