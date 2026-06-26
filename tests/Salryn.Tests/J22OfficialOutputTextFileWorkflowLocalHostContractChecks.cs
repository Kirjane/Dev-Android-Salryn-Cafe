using System.Reflection;
using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Core.Services;
using Salryn.Host.Local.Bootstrap;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

public static class J22OfficialOutputTextFileWorkflowLocalHostContractChecks
{
    public static async Task RunAsync()
    {
        HostOperationCatalog operationCatalog = HostOperationCatalog.CreateDefault();
        HostOperationDescriptor? descriptor = operationCatalog.Find(HostOperationNames.OfficialOutputTextFileWorkflow);

        if (descriptor is null
            || descriptor.OperationKind != HostOperationKind.Command
            || !descriptor.RequiresStoreIdentity
            || !descriptor.RequiresTerminalIdentity
            || !descriptor.RequiresAuthenticatedUser
            || descriptor.RequiresCashierSession
            || descriptor.AllowsReadOnlyMobileSnapshotAccess)
        {
            throw new InvalidOperationException("10B must catalog official output text-file workflow as an authenticated Local Host command without checkout session or mobile snapshot access.");
        }

        LocalHostOperationContractMap contractMap = LocalHostOperationContractMap.CreateDefault();
        LocalHostOperationContractDescriptor? contract = contractMap.Find(HostOperationNames.OfficialOutputTextFileWorkflow);

        if (contract is null
            || contract.OperationKind != HostOperationKind.Command
            || contract.PayloadType != typeof(LocalHostOfficialOutputTextFileWorkflowRequest)
            || contract.ResponseType != typeof(LocalHostOfficialOutputTextFileWorkflowResponse))
        {
            throw new InvalidOperationException("10B must map official output text-file workflow to the explicit Local Host request and response contracts.");
        }

        if (!LocalHostOperationContractRules.CoversCatalog(contractMap, operationCatalog))
        {
            throw new InvalidOperationException("10B Local Host operation contract map must still cover the full Core Host catalog.");
        }

        RequireProperty<LocalHostOfficialOutputTextFileWorkflowRequest>(
            nameof(LocalHostOfficialOutputTextFileWorkflowRequest.ExportPackage),
            typeof(OfficialOutputExportPackage));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowRequest>(
            nameof(LocalHostOfficialOutputTextFileWorkflowRequest.OutputRootPath),
            typeof(string));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowRequest>(
            nameof(LocalHostOfficialOutputTextFileWorkflowRequest.RequestedAt),
            typeof(DateTimeOffset?));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowRequest>(
            nameof(LocalHostOfficialOutputTextFileWorkflowRequest.RequestedByUserId),
            typeof(Guid?));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowRequest>(
            nameof(LocalHostOfficialOutputTextFileWorkflowRequest.TerminalId),
            typeof(string));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowRequest>(
            nameof(LocalHostOfficialOutputTextFileWorkflowRequest.Metadata),
            typeof(IReadOnlyDictionary<string, string>));

        foreach (string field in new[]
        {
            "PackageReference", "PackageHash", "DeliveryReference", "DeliveryHash",
            "DeliveryLedgerReference", "DeliveryLedgerHash", "DispatchPlanReference", "DispatchPlanHash",
            "ExecutionReference", "ExecutionHash", "ExecutionLedgerReference", "ExecutionLedgerHash",
            "VerificationReference", "VerificationHash", "WorkflowReference", "WorkflowHash"
        })
        {
            RequireProperty<LocalHostOfficialOutputTextFileWorkflowReferenceSummary>(field, typeof(string));
        }

        RequireProperty<LocalHostOfficialOutputTextFileWorkflowResponse>(
            nameof(LocalHostOfficialOutputTextFileWorkflowResponse.Succeeded),
            typeof(bool));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowResponse>(
            nameof(LocalHostOfficialOutputTextFileWorkflowResponse.Verified),
            typeof(bool));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowResponse>(
            nameof(LocalHostOfficialOutputTextFileWorkflowResponse.StatusCode),
            typeof(string));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowResponse>(
            nameof(LocalHostOfficialOutputTextFileWorkflowResponse.StatusMessage),
            typeof(string));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowResponse>(
            nameof(LocalHostOfficialOutputTextFileWorkflowResponse.ErrorCode),
            typeof(string));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowResponse>(
            nameof(LocalHostOfficialOutputTextFileWorkflowResponse.ErrorMessage),
            typeof(string));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowResponse>(
            nameof(LocalHostOfficialOutputTextFileWorkflowResponse.References),
            typeof(LocalHostOfficialOutputTextFileWorkflowReferenceSummary));
        RequireProperty<LocalHostOfficialOutputTextFileWorkflowResponse>(
            nameof(LocalHostOfficialOutputTextFileWorkflowResponse.RuntimeOutputRootPath),
            typeof(string));
        AssertResponseDoesNotExposeRawOutput();

        Result nullValidation = LocalHostOfficialOutputTextFileWorkflowRules.ValidateRequest(null);
        if (nullValidation.IsSuccess || nullValidation.ErrorDetail.Code != "LocalHostOfficialOutputTextFileWorkflow.RequestRequired")
        {
            throw new InvalidOperationException("10B must reject a missing Local Host official output text-file workflow request.");
        }

        LocalHostOfficialOutputTextFileWorkflowRequest validRequest = CreateValidRequest();
        Result requestValidation = LocalHostOfficialOutputTextFileWorkflowRules.ValidateRequest(validRequest);

        if (requestValidation.IsFailure)
        {
            throw new InvalidOperationException("10B must accept a complete Local Host official output text-file workflow request.");
        }

        LocalHostOfficialOutputTextFileWorkflowRequest normalized =
            LocalHostOfficialOutputTextFileWorkflowRules.NormalizeRequest(validRequest);

        if (normalized.OutputRootPath != "C:/salryn-output"
            || normalized.TerminalId != "terminal-001"
            || normalized.Metadata is null
            || normalized.Metadata.Keys.First() != "alpha"
            || normalized.Metadata["alpha"] != "first")
        {
            throw new InvalidOperationException("10B must normalize output root, terminal id, and metadata deterministically.");
        }

        LocalHostOfficialOutputTextFileWorkflowRequest duplicateMetadataRequest =
            validRequest with
            {
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["alpha"] = "one",
                    [" alpha "] = "two"
                }
            };

        Result duplicateMetadataValidation = LocalHostOfficialOutputTextFileWorkflowRules.ValidateRequest(duplicateMetadataRequest);
        if (duplicateMetadataValidation.IsSuccess
            || duplicateMetadataValidation.ErrorDetail.Code != "LocalHostOfficialOutputTextFileWorkflow.MetadataInvalid")
        {
            throw new InvalidOperationException("10B must reject duplicate Local Host metadata keys after trimming.");
        }

        await AssertPlaceholderDispatchAsync(validRequest);
        AssertSourceGuards();
    }

    private static async Task AssertPlaceholderDispatchAsync(
        LocalHostOfficialOutputTextFileWorkflowRequest payload)
    {
        StoreDeploymentProfile profile = CreateProfile();
        LocalHostRuntime runtime = new(profile);
        LocalHostHandlerBootstrapper bootstrapper = new();
        LocalHostHandlerBootstrapResult bootstrapResult = bootstrapper.RegisterDefaultHandlers(runtime);
        HostOperationCatalog catalog = HostOperationCatalog.CreateDefault();

        if (bootstrapResult.RegisteredHandlerCount != catalog.Descriptors.Count
            || !bootstrapResult.RegisteredOperationNames.Contains(HostOperationNames.OfficialOutputTextFileWorkflow))
        {
            throw new InvalidOperationException("10B must keep Local Host bootstrap handler counts aligned with the new official output text-file workflow operation.");
        }

        runtime.Start(DateTimeOffset.UtcNow);

        HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse> response =
            await runtime.DispatchAsync<LocalHostOfficialOutputTextFileWorkflowRequest, LocalHostOfficialOutputTextFileWorkflowResponse>(
                new HostRequestEnvelope<LocalHostOfficialOutputTextFileWorkflowRequest>(
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
                        RequestedAt: DateTimeOffset.UtcNow),
                    Payload: payload));

        if (response.IsSuccess
            || response.Error?.Code != LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented
            || runtime.GetSnapshot().State != LocalHostRuntimeState.Running)
        {
            throw new InvalidOperationException("10B Local Host official output text-file workflow must fail closed through the placeholder handler without faulting runtime.");
        }
    }

    private static void AssertResponseDoesNotExposeRawOutput()
    {
        string[] forbiddenNameFragments =
        {
            "Text" + "Result",
            "Payload" + "Text",
            "Written" + "File",
            "Written" + "Artifact",
            "Pdf",
            "Crystal",
            "Print",
            "Esc",
            "Pos"
        };

        foreach (PropertyInfo property in typeof(LocalHostOfficialOutputTextFileWorkflowResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Concat(typeof(LocalHostOfficialOutputTextFileWorkflowReferenceSummary)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)))
        {
            foreach (string forbidden in forbiddenNameFragments)
            {
                if (property.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("10B Local Host response must not expose raw output or unsupported output fields: " + property.Name);
                }
            }
        }
    }

    private static void AssertSourceGuards()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string hostProject = Read(root, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string hostContract = Read(root, "src", "Salryn.Host.Local", "Contracts", "LocalHostOfficialOutputTextFileWorkflowContracts.cs");
        string contractMap = Read(root, "src", "Salryn.Host.Local", "Contracts", "LocalHostOperationContractModels.cs");
        string bootstrap = Read(root, "src", "Salryn.Host.Local", "Bootstrap", "LocalHostHandlerBootstrapModels.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");

        RequireContains(hostProject, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must keep its Core reference.");
        RequireDoesNotContain(hostProject, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure for 10B.");
        RequireDoesNotContain(hostProject, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms for 10B.");

        RequireContains(contractMap, "HostOperationNames.OfficialOutputTextFileWorkflow", "10B contract map must include official output text-file workflow.");
        RequireContains(contractMap, "typeof(LocalHostOfficialOutputTextFileWorkflowRequest)", "10B contract map must use the explicit request type.");
        RequireContains(contractMap, "typeof(LocalHostOfficialOutputTextFileWorkflowResponse)", "10B contract map must use the explicit response type.");

        RequireContains(bootstrap, "HostOperationNames.OfficialOutputTextFileWorkflow", "10B bootstrap must register a fail-closed handler slot for the new operation.");
        RequireContains(bootstrap, "LocalHostBootstrapNotImplementedHandler<LocalHostOfficialOutputTextFileWorkflowRequest, LocalHostOfficialOutputTextFileWorkflowResponse>", "10B bootstrap must keep runtime execution deferred through the placeholder handler.");
        RequireDoesNotContain(bootstrap, "OfficialExternalOutputTextFileWorkflowRunner", "Host.Local bootstrap must not call the Infrastructure text-file workflow runner.");
        RequireDoesNotContain(bootstrap, "CompositionOfficialOutputTextFileWorkflowGatewayAdapter", "Host.Local bootstrap must not call App.WinForms composition.");
        RequireDoesNotContain(bootstrap, "OfficialTextFileExternalOutputAdapter", "Host.Local bootstrap must not call the text-file adapter.");

        RequireContains(appServices, "CompositionOfficialOutputTextFileWorkflowGatewayAdapter", "10A app workflow gateway must remain the app-owned execution surface.");
        RequireDoesNotContain(appServices, "CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter", "10D bridge must remain deferred when 10C execution is not dependency-safe.");

        if (Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "Salryn.Host.Local", "Handlers"), "*.cs", SearchOption.AllDirectories)
            .Any(static file => Path.GetFileName(file).Contains("LocalHostOfficialOutputTextFileWorkflowHandler", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("10C real Local Host official output text-file workflow handler must remain deferred.");
        }

        string appBridge = Path.Combine(
            root.FullName,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "CompositionLocalHostOfficialOutputTextFileWorkflowGatewayAdapter.cs");

        if (File.Exists(appBridge))
        {
            throw new InvalidOperationException("10D app Local Host official output text-file workflow bridge must remain deferred.");
        }

        foreach (string forbidden in ForbiddenMarkers())
        {
            RequireDoesNotContain(hostContract, forbidden, "10B Local Host official output text-file workflow contract must not contain forbidden marker: " + forbidden);
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
                ["zeta"] = "last",
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

    private static void RequireProperty<T>(string name, Type expectedType)
    {
        PropertyInfo? property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        if (property is null || property.PropertyType != expectedType)
        {
            throw new InvalidOperationException(typeof(T).Name + " must expose property " + name + " as " + expectedType.Name + ".");
        }
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
}
