using System.Reflection;
using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Core.Services;
using Salryn.Host.Local.Bootstrap;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Handlers;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

public static class J22OfficialOutputTextFileWorkflowLocalHostHandlerChecks
{
    public static async Task RunAsync()
    {
        AssertHandlerContract();
        await AssertDirectHandlerSuccessAsync();
        await AssertDirectHandlerValidationFailureAsync();
        await AssertDirectHandlerPortFailureAsync();
        await AssertDefaultBootstrapStillFailsClosedAsync();
        await AssertExplicitBootstrapRegistersRealHandlerAsync();
        AssertSourceGuards();
    }

    private static void AssertHandlerContract()
    {
        Type handlerType = typeof(LocalHostOfficialOutputTextFileWorkflowHandler);

        if (!typeof(IHostOperationHandler<LocalHostOfficialOutputTextFileWorkflowRequest, LocalHostOfficialOutputTextFileWorkflowResponse>)
            .IsAssignableFrom(handlerType))
        {
            throw new InvalidOperationException("10C-B handler must implement the typed Local Host official output text-file workflow handler contract.");
        }

        ConstructorInfo? constructor = handlerType.GetConstructor(new[] { typeof(IOfficialOutputTextFileWorkflowExecutionPort) });

        if (constructor is null)
        {
            throw new InvalidOperationException("10C-B handler constructor must require the Core official output text-file workflow execution port.");
        }

        AssertResponseDoesNotExposeRawOutput();
    }

    private static async Task AssertDirectHandlerSuccessAsync()
    {
        OfficialExternalOutputWorkflowResult workflowResult = CreateWorkflowResult();
        FakeOfficialOutputTextFileWorkflowExecutionPort port = new(Result<OfficialExternalOutputWorkflowResult>.Success(workflowResult));
        LocalHostOfficialOutputTextFileWorkflowHandler handler = new(port);
        HostRequestEnvelope<LocalHostOfficialOutputTextFileWorkflowRequest> request = CreateHostRequest(CreateValidRequest());

        HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse> response =
            await handler.HandleAsync(request);

        if (!response.IsSuccess
            || response.Payload is null
            || !response.Payload.Succeeded
            || !response.Payload.Verified
            || response.Payload.References is null
            || response.Payload.References.WorkflowReference != workflowResult.WorkflowReference
            || response.Payload.References.PackageHash != workflowResult.PackageHash
            || response.Payload.RuntimeOutputRootPath != workflowResult.OutputRootPath
            || response.Error is not null)
        {
            throw new InvalidOperationException("10C-B handler must map successful Core workflow results to the Local Host response contract.");
        }

        if (port.CallCount != 1
            || port.LastRequest is null
            || port.LastRequest.OutputRootPath != "C:/salryn-output"
            || port.LastRequest.TerminalId != "terminal-001"
            || port.LastRequest.Metadata is null
            || port.LastRequest.Metadata.Keys.First() != "alpha"
            || port.LastRequest.Metadata["alpha"] != "first")
        {
            throw new InvalidOperationException("10C-B handler must normalize and map Local Host request fields into the Core workflow request.");
        }
    }

    private static async Task AssertDirectHandlerValidationFailureAsync()
    {
        FakeOfficialOutputTextFileWorkflowExecutionPort port = new(Result<OfficialExternalOutputWorkflowResult>.Success(CreateWorkflowResult()));
        LocalHostOfficialOutputTextFileWorkflowHandler handler = new(port);
        HostRequestEnvelope<LocalHostOfficialOutputTextFileWorkflowRequest> request = CreateHostRequest(CreateValidRequest() with
        {
            OutputRootPath = " "
        });

        HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse> response =
            await handler.HandleAsync(request);

        if (!response.IsSuccess
            || response.Payload is null
            || response.Payload.Succeeded
            || response.Payload.ErrorCode != "LocalHostOfficialOutputTextFileWorkflow.OutputRootRequired"
            || response.Payload.References is not null
            || port.CallCount != 0)
        {
            throw new InvalidOperationException("10C-B handler must validate Local Host requests before calling the Core execution port.");
        }
    }

    private static async Task AssertDirectHandlerPortFailureAsync()
    {
        Error failure = Error.Failure(
            "OfficialOutputTextFileWorkflow.PortFailure",
            "Controlled official output text-file workflow execution failed.");
        FakeOfficialOutputTextFileWorkflowExecutionPort port = new(Result<OfficialExternalOutputWorkflowResult>.Fail(failure));
        LocalHostOfficialOutputTextFileWorkflowHandler handler = new(port);

        HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse> response =
            await handler.HandleAsync(CreateHostRequest(CreateValidRequest()));

        if (!response.IsSuccess
            || response.Payload is null
            || response.Payload.Succeeded
            || response.Payload.Verified
            || response.Payload.ErrorCode != failure.Code
            || response.Payload.ErrorMessage != failure.Message
            || response.Payload.References is not null
            || response.Error is not null)
        {
            throw new InvalidOperationException("10C-B handler must map Core port failures to safe Local Host response status fields.");
        }
    }

    private static async Task AssertDefaultBootstrapStillFailsClosedAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        LocalHostRuntime runtime = new(profile);
        LocalHostHandlerBootstrapper bootstrapper = new();
        LocalHostHandlerBootstrapResult result = bootstrapper.RegisterDefaultHandlers(runtime);
        HostOperationCatalog catalog = HostOperationCatalog.CreateDefault();

        if (result.RegisteredHandlerCount != catalog.Descriptors.Count)
        {
            throw new InvalidOperationException("Default bootstrap must keep full handler count alignment.");
        }

        runtime.Start(DateTimeOffset.UtcNow);

        HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse> response =
            await runtime.DispatchAsync<LocalHostOfficialOutputTextFileWorkflowRequest, LocalHostOfficialOutputTextFileWorkflowResponse>(
                CreateHostRequest(CreateValidRequest(), profile));

        if (response.IsSuccess
            || response.Error?.Code != LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented
            || runtime.GetSnapshot().State != LocalHostRuntimeState.Running)
        {
            throw new InvalidOperationException("Default bootstrap must remain fail-closed for Local Host official output text-file workflow execution.");
        }
    }

    private static async Task AssertExplicitBootstrapRegistersRealHandlerAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        LocalHostRuntime runtime = new(profile);
        HostOperationCatalog catalog = HostOperationCatalog.CreateDefault();
        FakeOfficialOutputTextFileWorkflowExecutionPort port = new(Result<OfficialExternalOutputWorkflowResult>.Success(CreateWorkflowResult()));
        LocalHostHandlerBootstrapper bootstrapper = new();

        LocalHostHandlerBootstrapResult result = bootstrapper.RegisterDefaultHandlersWithOfficialOutputTextFileWorkflow(
            runtime,
            port);

        if (result.RegisteredHandlerCount != catalog.Descriptors.Count
            || !result.RegisteredOperationNames.Contains(HostOperationNames.OfficialOutputTextFileWorkflow))
        {
            throw new InvalidOperationException("Explicit 10C-B bootstrap must keep full handler count alignment and include the official output text-file workflow operation.");
        }

        runtime.Start(DateTimeOffset.UtcNow);

        HostResponseEnvelope<LocalHostOfficialOutputTextFileWorkflowResponse> response =
            await runtime.DispatchAsync<LocalHostOfficialOutputTextFileWorkflowRequest, LocalHostOfficialOutputTextFileWorkflowResponse>(
                CreateHostRequest(CreateValidRequest(), profile));

        if (!response.IsSuccess
            || response.Payload is null
            || !response.Payload.Succeeded
            || response.Payload.References?.WorkflowHash != "workflow-hash"
            || port.CallCount != 1
            || runtime.GetSnapshot().State != LocalHostRuntimeState.Running)
        {
            throw new InvalidOperationException("Explicit 10C-B bootstrap must dispatch official output text-file workflow requests to the real handler through the Core port.");
        }
    }

    private static void AssertSourceGuards()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string hostProject = Read(root, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string handler = Read(root, "src", "Salryn.Host.Local", "Handlers", "LocalHostOfficialOutputTextFileWorkflowHandler.cs");
        string bootstrap = Read(root, "src", "Salryn.Host.Local", "Bootstrap", "LocalHostHandlerBootstrapModels.cs");

        RequireContains(hostProject, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireDoesNotContain(hostProject, "Salryn.Infrastructure", "10C-B must not add a Host.Local reference to Infrastructure.");
        RequireDoesNotContain(hostProject, "Salryn.App.WinForms", "10C-B must not add a Host.Local reference to App.WinForms.");

        RequireContains(handler, "LocalHostOfficialOutputTextFileWorkflowRules.ValidateRequest", "10C-B handler must use Local Host official output text-file workflow validation rules.");
        RequireContains(handler, "LocalHostOfficialOutputTextFileWorkflowRules.NormalizeRequest", "10C-B handler must normalize through the Local Host official output text-file workflow rules.");
        RequireContains(handler, "OfficialExternalOutputWorkflowRequest workflowRequest = new", "10C-B handler must map to the Core workflow request.");
        RequireContains(handler, "_executionPort.RunAsync", "10C-B handler must call the Core official output text-file workflow execution port.");
        RequireContains(bootstrap, "RegisterDefaultHandlersWithOfficialOutputTextFileWorkflow", "10C-B bootstrap must expose explicit real handler registration.");
        RequireContains(bootstrap, "new LocalHostOfficialOutputTextFileWorkflowHandler(officialOutputTextFileWorkflow)", "10C-B bootstrap must register the real handler only when the Core port is supplied.");
        RequireContains(bootstrap, "LocalHostBootstrapNotImplementedHandler<LocalHostOfficialOutputTextFileWorkflowRequest, LocalHostOfficialOutputTextFileWorkflowResponse>", "10C-B default bootstrap must preserve the fail-closed placeholder.");

        RequireDoesNotContain(handler, "OfficialExternalOutputTextFileWorkflowRunner", "10C-B handler must not call the Infrastructure runner.");
        RequireDoesNotContain(handler, "SalrynAppServices", "10C-B handler must not call App.WinForms services.");
        RequireDoesNotContain(handler, "CompositionOfficialOutputTextFileWorkflowGatewayAdapter", "10C-B handler must not call App.WinForms composition.");

        foreach (string text in new[] { handler })
        {
            foreach (string forbidden in ForbiddenMarkers())
            {
                RequireDoesNotContain(
                    text,
                    forbidden,
                    "10C-B new handler file must not contain forbidden marker: " + forbidden);
            }
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
                    throw new InvalidOperationException("10C-B Local Host response must not expose raw output or unsupported output fields: " + property.Name);
                }
            }
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

    private static HostRequestEnvelope<LocalHostOfficialOutputTextFileWorkflowRequest> CreateHostRequest(
        LocalHostOfficialOutputTextFileWorkflowRequest payload)
    {
        StoreDeploymentProfile profile = CreateProfile();
        return CreateHostRequest(payload, profile);
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
                RequestedAt: DateTimeOffset.UtcNow),
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
                new[] { "hidden workflow text" },
                "hidden workflow text"));
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
        private readonly Result<OfficialExternalOutputWorkflowResult> _result;

        public FakeOfficialOutputTextFileWorkflowExecutionPort(
            Result<OfficialExternalOutputWorkflowResult> result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public OfficialExternalOutputWorkflowRequest? LastRequest { get; private set; }

        public Task<Result<OfficialExternalOutputWorkflowResult>> RunAsync(
            OfficialExternalOutputWorkflowRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;

            return Task.FromResult(_result);
        }
    }
}
