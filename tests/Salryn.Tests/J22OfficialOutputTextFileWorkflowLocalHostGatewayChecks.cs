using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Host.Local.Composition;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

public static class J22OfficialOutputTextFileWorkflowLocalHostGatewayChecks
{
    public static async Task RunAsync()
    {
        await AssertCombinedLocalHostCompositionRegistersOfficialOutputTextFileWorkflowAsync();
        AssertSourceGuards();
    }

    private static async Task AssertCombinedLocalHostCompositionRegistersOfficialOutputTextFileWorkflowAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        FakeOfficialOutputTextFileWorkflowExecutionPort port = new(CreateWorkflowResult());
        LocalHostBootstrapComposer composer = new();

        LocalHostBootstrapCompositionResult composition = composer.ComposeWithProductLookup(
            profile,
            new FakeProductReadRepository(),
            port);

        if (!LocalHostBootstrapCompositionRules.IsCompositionValid(composition))
        {
            throw new InvalidOperationException("10D combined Local Host composition must remain operation/contract/handler aligned.");
        }

        composition.Runtime.Start(DateTimeOffset.Parse("2026-06-27T10:00:00+08:00"));

        HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse> response =
            await composition.Runtime.DispatchAsync<
                LocalHostOfficialOutputTextFileWorkflowRequest,
                LocalHostOfficialOutputTextFileWorkflowResponse>(
                CreateHostRequest(CreateValidRequest(), profile));

        if (!response.IsSuccess
            || response.Payload is null
            || !response.Payload.Succeeded
            || response.Payload.References?.WorkflowReference != "salryn://official-external-output-workflow/workflow-hash"
            || response.Payload.References.PackageHash != "package-hash"
            || response.Payload.RuntimeOutputRootPath != "C:/salryn-output"
            || port.CallCount != 1
            || port.LastRequest?.OutputRootPath != "C:/salryn-output")
        {
            throw new InvalidOperationException("10D combined Local Host composition must dispatch official output text-file workflow through the Core port.");
        }
    }

    private static void AssertSourceGuards()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string bridge = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string hostProject = Read(root, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string hostComposer = Read(root, "src", "Salryn.Host.Local", "Composition", "LocalHostBootstrapCompositionModels.cs");
        string bootstrap = Read(root, "src", "Salryn.Host.Local", "Bootstrap", "LocalHostHandlerBootstrapModels.cs");

        RequireContains(bridge, "public sealed class CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter", "10D app Local Host bridge must exist.");
        RequireContains(bridge, "IAppOfficialOutputTextFileWorkflowGateway", "10D bridge must implement the app official output text-file workflow gateway contract.");
        RequireContains(bridge, "AppOfficialOutputTextFileWorkflowRules.ValidateRequest", "10D bridge must validate app requests before dispatch.");
        RequireContains(bridge, "AppOfficialOutputTextFileWorkflowRules.NormalizeRequest", "10D bridge must normalize app requests before dispatch.");
        RequireContains(bridge, "HostOperationKind.Command", "10D bridge must dispatch the official output text-file workflow as a command.");
        RequireContains(bridge, "HostOperationNames.OfficialOutputTextFileWorkflow", "10D bridge must map to officialOutput.textFileWorkflow.");
        RequireContains(bridge, "new LocalHostOfficialOutputTextFileWorkflowRequest", "10D bridge must map app requests to the Local Host request contract.");
        RequireContains(bridge, "DispatchAsync<", "10D bridge must dispatch through the Local Host runtime holder.");
        RequireContains(bridge, "new AppOfficialOutputTextFileWorkflowReferenceSummary", "10D bridge must map Local Host references back to app reference summary fields.");
        RequireContains(bridge, "AppLocalHostOfficialOutputTextFileWorkflow.LocalHostUnavailable", "10D bridge must fail safely when Local Host dispatch is unavailable.");

        RequireContains(appServices, "public IAppOfficialOutputTextFileWorkflowGateway OfficialOutputTextFileWorkflow { get; }", "10D must keep the direct 10A workflow gateway.");
        RequireContains(appServices, "public IAppOfficialOutputTextFileWorkflowGateway LocalHostOfficialOutputTextFileWorkflow { get; }", "10D AppServices must expose the passive Local Host official output text-file workflow gateway.");
        RequireContains(appServices, "new CompositionOfficialOutputTextFileWorkflowGatewayAdapter", "10D must keep direct app workflow wiring intact.");
        RequireContains(appServices, "new CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);", "10D AppServices must construct the Local Host official output text-file workflow bridge.");
        RequireContains(appServices, "new OfficialExternalOutputTextFileWorkflowRunner()", "10D AppServices must inject the Infrastructure runner only from the app composition layer.");

        RequireContains(hostComposer, "IOfficialOutputTextFileWorkflowExecutionPort officialOutputTextFileWorkflow", "10D Host.Local composer must accept the Core official output text-file workflow port.");
        RequireContains(hostComposer, "RegisterDefaultHandlersWithProductLookupAndOfficialOutputTextFileWorkflow", "10D Host.Local composer must register ProductLookup and official output text-file workflow together.");
        RequireContains(bootstrap, "RegisterDefaultHandlersWithProductLookupAndOfficialOutputTextFileWorkflow", "10D Host.Local bootstrap must expose the combined handler registration path.");
        RequireContains(bootstrap, "new LocalHostOfficialOutputTextFileWorkflowHandler(officialOutputTextFileWorkflow)", "10D combined bootstrap must still construct the real handler through the Core port only.");

        RequireContains(hostProject, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must remain Core-referenced.");
        RequireDoesNotContain(hostProject, "Salryn.Infrastructure", "10D must not add a Host.Local reference to Infrastructure.");
        RequireDoesNotContain(hostProject, "Salryn.App.WinForms", "10D must not add a Host.Local reference to App.WinForms.");

        foreach (string forbidden in ForbiddenMarkers())
        {
            RequireDoesNotContain(bridge, forbidden, "10D app Local Host bridge must not contain forbidden marker: " + forbidden);
        }

        foreach (string rawOutput in RawOutputMarkers())
        {
            RequireDoesNotContain(bridge, rawOutput, "10D bridge must not expose raw official output payload data: " + rawOutput);
        }
    }

    private static IEnumerable<string> ForbiddenMarkers()
    {
        yield return "Crystal" + "Decisions";
        yield return "." + "rpt";
        yield return "System.Windows." + "Forms";
        yield return "Microsoft.Data." + "Sqlite";
        yield return "Sql" + "ite";
        yield return "SQL" + "ite";
        yield return "ESC" + "/" + "POS";
        yield return "Print" + "Document";
        yield return "Printer" + "Settings";
        yield return "System.Drawing." + "Printing";
        yield return "Pdf" + "Document";
        yield return "Export" + "ToDisk";
        yield return "Complete" + "Sale";
        yield return "Official" + "Checkout";
        yield return "Electronic" + "Journal" + "Append";
        yield return "Official" + "ZReading" + "Service";
        yield return "Finalize" + "Async";
        yield return "Save" + "Async";
        yield return "IN" + "SERT";
        yield return "UP" + "DATE";
        yield return "DE" + "LETE";
    }

    private static IEnumerable<string> RawOutputMarkers()
    {
        yield return "Text" + "Result";
        yield return "Payload" + "Text";
        yield return "Written" + "File";
        yield return "Written" + "Artifact";
    }

    private static HostRequestEnvelope<LocalHostOfficialOutputTextFileWorkflowRequest> CreateHostRequest(
        LocalHostOfficialOutputTextFileWorkflowRequest payload,
        StoreDeploymentProfile profile)
    {
        return new HostRequestEnvelope<LocalHostOfficialOutputTextFileWorkflowRequest>(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Command,
            OperationName: HostOperationNames.OfficialOutputTextFileWorkflow,
            Context: new HostRequestContext(
                TenantId: profile.Store.TenantId,
                StoreId: profile.Store.StoreId,
                UserId: Guid.NewGuid(),
                RoleId: Guid.NewGuid(),
                CashierSessionId: null,
                TerminalId: Guid.NewGuid(),
                DeviceKey: "terminal-device",
                RequestedAt: DateTimeOffset.Parse("2026-06-27T10:00:00+08:00")),
            Payload: payload);
    }

    private static LocalHostOfficialOutputTextFileWorkflowRequest CreateValidRequest()
    {
        return new LocalHostOfficialOutputTextFileWorkflowRequest(
            ExportPackage: CreateTrustedPackage(),
            OutputRootPath: " C:/salryn-output ",
            RequestedAt: DateTimeOffset.Parse("2026-06-27T10:00:00+08:00"),
            RequestedByUserId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            TerminalId: " terminal-001 ",
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [" alpha "] = " first "
            });
    }

    private static OfficialOutputExportPackage CreateTrustedPackage()
    {
        OfficialOutputArtifactManifestTextRenderResult manifestText = new(
            new[] { "manifest" },
            "manifest");
        OfficialOutputArtifactManifestPackage manifestPackage = new(
            Array.Empty<OfficialOutputArtifactManifestEntry>(),
            "manifest-hash",
            "salryn://official-output-manifest/manifest-hash",
            EntryCount: 0,
            CreatedAt: null,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal),
            TextResult: manifestText);
        OfficialOutputExportPackageTextRenderResult packageText = new(
            new[] { "package" },
            "package");

        return new OfficialOutputExportPackage(
            OfficialOutputExportPackageKind.PreviewTextBundle,
            manifestPackage,
            Array.Empty<OfficialOutputExportPackageItem>(),
            "package-hash",
            "salryn://official-output-export-package/package-hash",
            EntryCount: 0,
            PayloadCount: 0,
            TotalLineCount: 0,
            TextResult: packageText);
    }

    private static OfficialExternalOutputWorkflowResult CreateWorkflowResult()
    {
        return new OfficialExternalOutputWorkflowResult(
            PackageReference: "salryn://official-output-export-package/package-hash",
            PackageHash: "package-hash",
            DeliveryReference: "salryn://official-output-delivery/delivery-hash",
            DeliveryHash: "delivery-hash",
            DeliveryLedgerReference: "salryn://official-output-delivery-ledger/delivery-ledger-hash",
            DeliveryLedgerHash: "delivery-ledger-hash",
            DispatchPlanReference: "salryn://official-external-output-dispatch-plan/dispatch-hash",
            DispatchPlanHash: "dispatch-hash",
            ExecutionReference: "salryn://official-external-output-execution/execution-hash",
            ExecutionHash: "execution-hash",
            ExecutionLedgerReference: "salryn://official-external-output-execution-ledger/execution-ledger-hash",
            ExecutionLedgerHash: "execution-ledger-hash",
            VerificationReference: "salryn://official-external-output-verification/verification-hash",
            VerificationHash: "verification-hash",
            Verified: true,
            WorkflowHash: "workflow-hash",
            WorkflowReference: "salryn://official-external-output-workflow/workflow-hash",
            OutputRootPath: "C:/salryn-output",
            SideEffectMarker: OfficialExternalOutputWorkflowResultBuilder.WorkflowVerifiedMarker,
            CompletedAt: DateTimeOffset.Parse("2026-06-27T10:00:00+08:00"),
            TextResult: new OfficialExternalOutputWorkflowTextRenderResult(
                new[] { "internal workflow line" },
                "internal workflow line"));
    }

    private static StoreDeploymentProfile CreateProfile()
    {
        return new StoreDeploymentProfile(
            Store: new StoreIdentity(
                TenantId: Guid.NewGuid(),
                StoreId: Guid.NewGuid()),
            Host: new StoreHostIdentity(
                HostId: Guid.NewGuid(),
                MachineName: "field-node",
                Role: SalrynMachineRole.StandaloneHost),
            Mode: SalrynDeploymentMode.Standalone,
            Endpoint: null,
            Terminal: null);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;
        foreach (string part in parts) path = Path.Combine(path, part);
        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeOfficialOutputTextFileWorkflowExecutionPort : IOfficialOutputTextFileWorkflowExecutionPort
    {
        private readonly OfficialExternalOutputWorkflowResult _workflowResult;

        public FakeOfficialOutputTextFileWorkflowExecutionPort(
            OfficialExternalOutputWorkflowResult workflowResult)
        {
            _workflowResult = workflowResult;
        }

        public int CallCount { get; private set; }

        public OfficialExternalOutputWorkflowRequest? LastRequest { get; private set; }

        public Task<Result<OfficialExternalOutputWorkflowResult>> RunAsync(
            OfficialExternalOutputWorkflowRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;

            return Task.FromResult(Result<OfficialExternalOutputWorkflowResult>.Success(_workflowResult));
        }
    }

    private sealed class FakeProductReadRepository : IProductReadRepository
    {
        public Task<Result<ProductLookupReadModel?>> FindByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ProductLookupReadModel?>.Success(null));
        }

        public Task<Result<ProductDetailReadModel?>> FindDetailByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ProductDetailReadModel?>.Success(null));
        }

        public Task<Result<ProductLookupReadModel?>> FindByBarcodeAsync(
            Barcode barcode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<ProductLookupReadModel?>.Success(null));
        }

        public Task<Result<PagedResult<ProductListItemReadModel>>> SearchAsync(
            ProductSearchFilter filter,
            CancellationToken cancellationToken = default)
        {
            PagedResult<ProductListItemReadModel> result = new(
                Items: Array.Empty<ProductListItemReadModel>(),
                TotalCount: 0,
                PageNumber: filter.Page.PageNumber,
                PageSize: filter.Page.PageSize);

            return Task.FromResult(Result<PagedResult<ProductListItemReadModel>>.Success(result));
        }

        public Task<Result<IReadOnlyList<LowStockProductReadModel>>> GetLowStockAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<LowStockProductReadModel> empty = Array.Empty<LowStockProductReadModel>();
            return Task.FromResult(Result<IReadOnlyList<LowStockProductReadModel>>.Success(empty));
        }
    }
}
