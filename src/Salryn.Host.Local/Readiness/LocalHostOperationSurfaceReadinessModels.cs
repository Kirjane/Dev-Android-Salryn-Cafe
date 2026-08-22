using Salryn.Core.Contracts;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Composition;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Host.Local.Readiness;

// Kirch: Defines why the Local Host operation surface is not ready for future real handlers.
public enum LocalHostOperationSurfaceReadinessFailureReason
{
    MissingComposition = 1,
    MissingOperationCatalog = 2,
    MissingContractMap = 3,
    RuntimeNotStopped = 4,
    OperationCountMismatch = 5,
    ContractCountMismatch = 6,
    HandlerCountMismatch = 7,
    MissingContract = 8,
    OperationContractMismatch = 9,
    MissingBootstrappedHandler = 10
}

// Kirch: Describes one readiness issue without exposing transport, UI, or persistence concerns.
public sealed record LocalHostOperationSurfaceReadinessIssue(
    string OperationName,
    LocalHostOperationSurfaceReadinessFailureReason Reason,
    string Message);

// Kirch: Summarizes whether the local Host operation surface is ready for future handler replacement.
public sealed record LocalHostOperationSurfaceReadinessReport(
    bool IsReady,
    int ExpectedOperationCount,
    int ReadyOperationCount,
    IReadOnlyCollection<string> ReadyOperationNames,
    IReadOnlyCollection<LocalHostOperationSurfaceReadinessIssue> Issues);

// Kirch: Inspects the local Host operation surface after composition without implementing business handlers.
public sealed class LocalHostOperationSurfaceReadinessInspector
{
    // Kirch: Verifies catalog, contract map, bootstrap result, and runtime snapshot alignment.
    public LocalHostOperationSurfaceReadinessReport Inspect(
        LocalHostBootstrapCompositionResult? composition,
        HostOperationCatalog? operationCatalog,
        LocalHostOperationContractMap? contractMap)
    {
        List<LocalHostOperationSurfaceReadinessIssue> issues = new();
        List<string> readyOperationNames = new();

        if (composition is null)
        {
            issues.Add(CreateIssue(
                operationName: string.Empty,
                reason: LocalHostOperationSurfaceReadinessFailureReason.MissingComposition,
                message: "Local Host bootstrap composition is required."));

            return CreateReport(
                expectedOperationCount: 0,
                readyOperationNames,
                issues);
        }

        if (operationCatalog is null)
        {
            issues.Add(CreateIssue(
                operationName: string.Empty,
                reason: LocalHostOperationSurfaceReadinessFailureReason.MissingOperationCatalog,
                message: "Host operation catalog is required."));

            return CreateReport(
                expectedOperationCount: 0,
                readyOperationNames,
                issues);
        }

        if (contractMap is null)
        {
            issues.Add(CreateIssue(
                operationName: string.Empty,
                reason: LocalHostOperationSurfaceReadinessFailureReason.MissingContractMap,
                message: "Local Host operation contract map is required."));

            return CreateReport(
                expectedOperationCount: operationCatalog.Descriptors.Count,
                readyOperationNames,
                issues);
        }

        LocalHostRuntimeSnapshot snapshot = composition.Runtime.GetSnapshot();
        int expectedOperationCount = operationCatalog.Descriptors.Count;

        if (snapshot.State != LocalHostRuntimeState.Stopped)
        {
            issues.Add(CreateIssue(
                operationName: string.Empty,
                reason: LocalHostOperationSurfaceReadinessFailureReason.RuntimeNotStopped,
                message: "Local Host operation surface must be sealed while runtime is stopped."));
        }

        if (snapshot.RegisteredOperationCount != expectedOperationCount)
        {
            issues.Add(CreateIssue(
                operationName: string.Empty,
                reason: LocalHostOperationSurfaceReadinessFailureReason.OperationCountMismatch,
                message: "Registered operation count does not match the Host operation catalog."));
        }

        if (snapshot.RegisteredContractCount != expectedOperationCount)
        {
            issues.Add(CreateIssue(
                operationName: string.Empty,
                reason: LocalHostOperationSurfaceReadinessFailureReason.ContractCountMismatch,
                message: "Registered contract count does not match the Host operation catalog."));
        }

        if (snapshot.RegisteredHandlerCount != expectedOperationCount)
        {
            issues.Add(CreateIssue(
                operationName: string.Empty,
                reason: LocalHostOperationSurfaceReadinessFailureReason.HandlerCountMismatch,
                message: "Registered handler count does not match the Host operation catalog."));
        }

        foreach (HostOperationDescriptor descriptor in operationCatalog.Descriptors)
        {
            bool operationReady = true;
            LocalHostOperationContractDescriptor? contract = contractMap.Find(descriptor.OperationName);

            if (contract is null)
            {
                operationReady = false;
                issues.Add(CreateIssue(
                    operationName: descriptor.OperationName,
                    reason: LocalHostOperationSurfaceReadinessFailureReason.MissingContract,
                    message: "Local Host operation contract is missing."));
            }
            else if (!LocalHostOperationContractRules.MatchesDescriptor(contract, descriptor))
            {
                operationReady = false;
                issues.Add(CreateIssue(
                    operationName: descriptor.OperationName,
                    reason: LocalHostOperationSurfaceReadinessFailureReason.OperationContractMismatch,
                    message: "Local Host operation contract does not match the Core operation descriptor."));
            }

            if (!composition.BootstrapResult.RegisteredOperationNames.Contains(descriptor.OperationName))
            {
                operationReady = false;
                issues.Add(CreateIssue(
                    operationName: descriptor.OperationName,
                    reason: LocalHostOperationSurfaceReadinessFailureReason.MissingBootstrappedHandler,
                    message: "Local Host placeholder handler was not bootstrapped."));
            }

            if (operationReady)
            {
                readyOperationNames.Add(descriptor.OperationName);
            }
        }

        return CreateReport(
            expectedOperationCount,
            readyOperationNames,
            issues);
    }

    // Kirch: Creates a stable readiness issue for audit-friendly smoke checks.
    private static LocalHostOperationSurfaceReadinessIssue CreateIssue(
        string operationName,
        LocalHostOperationSurfaceReadinessFailureReason reason,
        string message)
    {
        return new LocalHostOperationSurfaceReadinessIssue(
            OperationName: operationName,
            Reason: reason,
            Message: message);
    }

    // Kirch: Creates a report where readiness is determined only by collected issues.
    private static LocalHostOperationSurfaceReadinessReport CreateReport(
        int expectedOperationCount,
        List<string> readyOperationNames,
        List<LocalHostOperationSurfaceReadinessIssue> issues)
    {
        return new LocalHostOperationSurfaceReadinessReport(
            IsReady: issues.Count == 0,
            ExpectedOperationCount: expectedOperationCount,
            ReadyOperationCount: readyOperationNames.Count,
            ReadyOperationNames: readyOperationNames.ToArray(),
            Issues: issues.ToArray());
    }
}

