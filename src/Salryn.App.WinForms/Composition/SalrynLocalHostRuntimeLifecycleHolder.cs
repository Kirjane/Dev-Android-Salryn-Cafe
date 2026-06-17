using System;
using Salryn.Host.Local.Runtime;

namespace Salryn.App.WinForms.Composition;

// Kirch: Captures a controlled lifecycle attempt without letting adapters, providers, Program, or Shell own runtime activation.
public sealed record SalrynLocalHostRuntimeLifecycleResult(
    bool IsSuccess,
    string Message,
    LocalHostRuntimeSnapshot Snapshot)
{
    public LocalHostRuntimeState State => Snapshot.State;
}

// Kirch: Local Host runtime lifecycle owner; observes status and exposes controlled start/stop without transport or UI routing.
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

    public SalrynLocalHostRuntimeLifecycleResult TryStart(DateTimeOffset startedAt)
    {
        LocalHostRuntimeSnapshot before = Snapshot;

        if (before.State == LocalHostRuntimeState.Faulted)
        {
            return new SalrynLocalHostRuntimeLifecycleResult(
                IsSuccess: false,
                Message: "Local Host runtime is faulted and must be stopped before start.",
                Snapshot: before);
        }

        try
        {
            localHostCompositionHolder.Composition.Runtime.Start(startedAt);

            LocalHostRuntimeSnapshot after = Snapshot;

            if (after.State != LocalHostRuntimeState.Running)
            {
                return new SalrynLocalHostRuntimeLifecycleResult(
                    IsSuccess: false,
                    Message: "Local Host runtime did not enter running state.",
                    Snapshot: after);
            }

            return new SalrynLocalHostRuntimeLifecycleResult(
                IsSuccess: true,
                Message: "Local Host runtime is running.",
                Snapshot: after);
        }
        catch (InvalidOperationException exception)
        {
            return new SalrynLocalHostRuntimeLifecycleResult(
                IsSuccess: false,
                Message: exception.Message,
                Snapshot: Snapshot);
        }
    }

    public SalrynLocalHostRuntimeLifecycleResult TryStop()
    {
        try
        {
            localHostCompositionHolder.Composition.Runtime.Stop();

            LocalHostRuntimeSnapshot after = Snapshot;

            if (after.State != LocalHostRuntimeState.Stopped)
            {
                return new SalrynLocalHostRuntimeLifecycleResult(
                    IsSuccess: false,
                    Message: "Local Host runtime did not enter stopped state.",
                    Snapshot: after);
            }

            return new SalrynLocalHostRuntimeLifecycleResult(
                IsSuccess: true,
                Message: "Local Host runtime is stopped.",
                Snapshot: after);
        }
        catch (InvalidOperationException exception)
        {
            return new SalrynLocalHostRuntimeLifecycleResult(
                IsSuccess: false,
                Message: exception.Message,
                Snapshot: Snapshot);
        }
    }
}