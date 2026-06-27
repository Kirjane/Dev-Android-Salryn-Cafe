namespace Salryn.App.WinForms.Composition;

public sealed class CompositionOfficialOutputTextFileWorkflowReadinessGateway :
    IAppOfficialOutputTextFileWorkflowReadinessGateway
{
    private readonly IAppOfficialOutputTextFileWorkflowGateway? directGateway;
    private readonly IAppOfficialOutputTextFileWorkflowGateway? localHostGateway;
    private readonly SalrynLocalHostCompositionHolder? localHostRuntimeHolder;
    private readonly SalrynLocalHostRuntimeLifecycleHolder? localHostRuntimeLifecycleHolder;
    private readonly SalrynLocalHostRuntimeLifecycleResult? localHostRuntimeStartupResult;
    private readonly SalrynLocalHostRequestContextProvider? requestContextProvider;
    private readonly SalrynLocalHostSessionIdentityProvider? sessionIdentityProvider;

    public CompositionOfficialOutputTextFileWorkflowReadinessGateway(
        IAppOfficialOutputTextFileWorkflowGateway? directGateway = null,
        IAppOfficialOutputTextFileWorkflowGateway? localHostGateway = null,
        SalrynLocalHostCompositionHolder? localHostRuntimeHolder = null,
        SalrynLocalHostRuntimeLifecycleHolder? localHostRuntimeLifecycleHolder = null,
        SalrynLocalHostRuntimeLifecycleResult? localHostRuntimeStartupResult = null,
        SalrynLocalHostRequestContextProvider? requestContextProvider = null,
        SalrynLocalHostSessionIdentityProvider? sessionIdentityProvider = null)
    {
        this.directGateway = directGateway;
        this.localHostGateway = localHostGateway;
        this.localHostRuntimeHolder = localHostRuntimeHolder;
        this.localHostRuntimeLifecycleHolder = localHostRuntimeLifecycleHolder;
        this.localHostRuntimeStartupResult = localHostRuntimeStartupResult;
        this.requestContextProvider = requestContextProvider;
        this.sessionIdentityProvider = sessionIdentityProvider;
    }

    public AppOfficialOutputTextFileWorkflowReadinessResult CheckReadiness(
        AppOfficialOutputTextFileWorkflowReadinessRequest? request)
    {
        List<AppOfficialOutputTextFileWorkflowReadinessIssue> issues = new();

        bool directGatewayAvailable = directGateway is not null;
        AddIssueIfFalse(
            issues,
            directGatewayAvailable,
            "AppOfficialOutputTextFileWorkflowReadiness.DirectGatewayUnavailable",
            "Direct official output text-file workflow gateway is not available.");

        bool localHostGatewayAvailable = localHostGateway is not null;
        AddIssueIfFalse(
            issues,
            localHostGatewayAvailable,
            "AppOfficialOutputTextFileWorkflowReadiness.LocalHostGatewayUnavailable",
            "Local Host official output text-file workflow gateway is not available.");

        bool localHostRuntimeHolderAvailable = localHostRuntimeHolder is not null;
        AddIssueIfFalse(
            issues,
            localHostRuntimeHolderAvailable,
            "AppOfficialOutputTextFileWorkflowReadiness.LocalHostRuntimeHolderUnavailable",
            "Local Host runtime holder is not available.");

        bool localHostRuntimeStartupSucceeded =
            localHostRuntimeStartupResult?.IsSuccess == true &&
            localHostRuntimeLifecycleHolder?.IsRunning == true;
        AddIssueIfFalse(
            issues,
            localHostRuntimeStartupSucceeded,
            "AppOfficialOutputTextFileWorkflowReadiness.LocalHostRuntimeStartupUnavailable",
            localHostRuntimeStartupResult?.Message ?? "Local Host runtime startup status is not available.");

        bool requestContextProviderAvailable = requestContextProvider is not null;
        AddIssueIfFalse(
            issues,
            requestContextProviderAvailable,
            "AppOfficialOutputTextFileWorkflowReadiness.RequestContextProviderUnavailable",
            "Local Host request context provider is not available.");

        bool sessionIdentityProviderAvailable = sessionIdentityProvider is not null;
        AddIssueIfFalse(
            issues,
            sessionIdentityProviderAvailable,
            "AppOfficialOutputTextFileWorkflowReadiness.SessionIdentityProviderUnavailable",
            "Local Host session identity provider is not available.");

        bool authenticatedAppIdentityAvailable = false;
        bool requestContextCanBeCreated = false;

        if (sessionIdentityProvider is not null &&
            sessionIdentityProvider.TryGetAuthenticatedIdentity(
                out Guid userId,
                out Guid roleId))
        {
            authenticatedAppIdentityAvailable = true;
            requestContextCanBeCreated =
                requestContextProvider?.CanCreateAuthenticatedContext(userId, roleId) == true;
        }

        AddIssueIfFalse(
            issues,
            authenticatedAppIdentityAvailable,
            "AppOfficialOutputTextFileWorkflowReadiness.AuthenticatedIdentityUnavailable",
            "Authenticated app identity is not available for Local Host official output text-file workflow.");
        AddIssueIfFalse(
            issues,
            requestContextCanBeCreated,
            "AppOfficialOutputTextFileWorkflowReadiness.RequestContextUnavailable",
            "Local Host request context cannot be created from the current app identity.");

        bool outputRootSupplied =
            AppOfficialOutputTextFileWorkflowReadinessRules.HasExplicitOutputRoot(request?.OutputRootPath);
        AddIssueIfFalse(
            issues,
            outputRootSupplied,
            "AppOfficialOutputTextFileWorkflowReadiness.OutputRootRequired",
            "Explicit output root is required before official output text-file workflow can run.");

        bool trustedExportPackageSupplied =
            AppOfficialOutputTextFileWorkflowReadinessRules.IsTrustedExportPackage(request?.ExportPackage);
        AddIssueIfFalse(
            issues,
            trustedExportPackageSupplied,
            "AppOfficialOutputTextFileWorkflowReadiness.TrustedExportPackageRequired",
            "Trusted official output export package is required before official output text-file workflow can run.");

        AppOfficialOutputTextFileWorkflowReadinessState state = new(
            DirectGatewayAvailable: directGatewayAvailable,
            LocalHostGatewayAvailable: localHostGatewayAvailable,
            LocalHostRuntimeHolderAvailable: localHostRuntimeHolderAvailable,
            LocalHostRuntimeStartupSucceeded: localHostRuntimeStartupSucceeded,
            RequestContextProviderAvailable: requestContextProviderAvailable,
            SessionIdentityProviderAvailable: sessionIdentityProviderAvailable,
            AuthenticatedAppIdentityAvailable: authenticatedAppIdentityAvailable,
            RequestContextCanBeCreated: requestContextCanBeCreated,
            OutputRootSupplied: outputRootSupplied,
            TrustedExportPackageSupplied: trustedExportPackageSupplied,
            TextFileOnly: true);

        return issues.Count == 0
            ? AppOfficialOutputTextFileWorkflowReadinessResult.Available(state)
            : AppOfficialOutputTextFileWorkflowReadinessResult.Unavailable(state, issues.ToArray());
    }

    private static void AddIssueIfFalse(
        List<AppOfficialOutputTextFileWorkflowReadinessIssue> issues,
        bool condition,
        string code,
        string message)
    {
        if (condition)
        {
            return;
        }

        issues.Add(new AppOfficialOutputTextFileWorkflowReadinessIssue(code, message));
    }
}
