namespace Salryn.Tests;

public static class J22OfficialOutputTextFileSourceSelectionBoundaryChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string sourceSelection = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileSourceSelectionBoundary.cs");
        string supplyBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFilePackageSupplyBoundary.cs");
        string uiBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowUiBoundary.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string checkoutPresenter = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "Checkout" + "Presenter.cs");
        string checkoutBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutLocalHostProductSearchBoundary.cs");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelectionRequest", "RMO-M1-B1 source-selection request must exist.");
        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelectionResult", "RMO-M1-B1 source-selection result must exist.");
        RequireContains(sourceSelection, "IOfficialOutputTextFileSourceSelectionBoundary", "RMO-M1-B1 source-selection boundary interface must exist.");
        RequireContains(sourceSelection, "public sealed class OfficialOutputTextFileSourceSelectionBoundary", "RMO-M1-B1 source-selection boundary implementation must exist.");
        RequireContains(sourceSelection, "public static class OfficialOutputTextFileSourceSelectionRules", "RMO-M1-B1 source-selection rules must exist.");

        RequireContains(sourceSelection, "OfficialOutputSourceReadRequest? SourceReadRequest", "RMO-M1-B1 request must carry source read request.");
        RequireContains(sourceSelection, "DateTimeOffset? RequestedAt", "RMO-M1-B1 request must carry requested timestamp.");
        RequireContains(sourceSelection, "Guid? RequestedByUserId", "RMO-M1-B1 request must carry requester user id.");
        RequireContains(sourceSelection, "string? TerminalId", "RMO-M1-B1 request must carry terminal id.");
        RequireContains(sourceSelection, "IReadOnlyDictionary<string, string>? Metadata", "RMO-M1-B1 request must carry metadata envelope.");
        RequireContains(sourceSelection, "OfficialPrintableDocumentCopyMode CopyMode", "RMO-M1-B1 request must carry copy mode.");

        RequireContains(sourceSelection, "OfficialOutputTextFileTrustedPackageProducerRequest", "RMO-M1-B1 must convert source selection into a producer request.");
        RequireContains(sourceSelection, "OfficialOutputSourceReadService.ValidateRequest(request.SourceReadRequest)", "RMO-M1-B1 must reuse source service identity validation.");
        RequireContains(sourceSelection, "OfficialOutputSourceReadService.NormalizeRequest(request.SourceReadRequest!)", "RMO-M1-B1 must normalize source identity through source service.");
        RequireContains(sourceSelection, "new OfficialOutputTextFileTrustedPackageProducerRequest(", "RMO-M1-B1 must build the producer request explicitly.");

        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelection.RequestedAtRequired", "RMO-M1-B1 must reject missing requested timestamp.");
        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelection.RequestedByUserIdRequired", "RMO-M1-B1 must reject missing requester user id.");
        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelection.InvalidRequestedByUserId", "RMO-M1-B1 must reject empty requester user id.");
        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelection.TerminalIdRequired", "RMO-M1-B1 must reject missing terminal id.");
        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelection.MetadataRequired", "RMO-M1-B1 must reject missing metadata.");
        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelection.UnsupportedCopyMode", "RMO-M1-B1 must reject unsupported copy mode.");
        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelection.DuplicateMetadataKey", "RMO-M1-B1 must reject duplicate metadata keys after trimming.");
        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelection.MetadataKeyRequired", "RMO-M1-B1 must reject blank metadata keys.");

        RequireContains(sourceSelection, "TerminalId = Normalize(request.TerminalId)", "RMO-M1-B1 must trim terminal id.");
        RequireContains(sourceSelection, "Metadata = NormalizeMetadata(request.Metadata!)", "RMO-M1-B1 must normalize metadata.");
        RequireContains(sourceSelection, "Normalize(pair.Key)", "RMO-M1-B1 must trim metadata keys.");
        RequireContains(sourceSelection, "Normalize(pair.Value)", "RMO-M1-B1 must trim metadata values.");
        RequireContains(sourceSelection, "OrderBy(static pair => pair.Key, StringComparer.Ordinal)", "RMO-M1-B1 must order metadata deterministically by ordinal key.");
        RequireContains(sourceSelection, "new Dictionary<string, string>(StringComparer.Ordinal)", "RMO-M1-B1 empty metadata must remain an explicit normalized envelope.");

        RequireContains(sourceSelection, "SourceReadRequest = OfficialOutputSourceReadService.NormalizeRequest(request.SourceReadRequest!)", "RMO-M1-B1 must preserve source identity semantics through source-service normalization.");
        RequireContains(sourceSelection, "normalizedRequest.SourceReadRequest", "RMO-M1-B1 must pass the normalized source identity to the producer request.");
        RequireContains(sourceSelection, "normalizedRequest.CopyMode", "RMO-M1-B1 must pass copy mode to the producer request.");

        RequireDoesNotContain(supplyBoundary, "OfficialOutputTextFileSourceSelection", "RMO-M1-B1 must not wire package supply to source selection yet.");
        RequireDoesNotContain(uiBoundary, "OfficialOutputTextFileSourceSelection", "RMO-M1-B1 must not wire UI boundary to source selection yet.");
        RequireDoesNotContain(appServices, "OfficialOutputTextFileSourceSelection", "RMO-M1-B1 must not wire AppServices to source selection yet.");
        RequireDoesNotContain(checkoutPresenter, "OfficialOutputTextFileSourceSelection", "RMO-M1-B1 must not wire checkout presenter to source selection.");
        RequireDoesNotContain(checkoutBoundary, "OfficialOutputTextFileSourceSelection", "RMO-M1-B1 must not wire checkout product search to source selection.");

        foreach (string marker in ForbiddenMarkers())
        {
            RequireDoesNotContain(sourceSelection, marker, "RMO-M1-B1 source-selection boundary must not contain forbidden marker: " + marker);
        }

        RequireContains(coreSmoke, "J22OfficialOutputTextFileSourceSelectionBoundaryChecks.Run();", "CoreSmoke must run RMO-M1-B1 source-selection checks.");
    }

    private static IEnumerable<string> ForbiddenMarkers()
    {
        yield return "Crystal" + "Decisions";
        yield return "." + "rpt";
        yield return "System.Windows." + "Forms";
        yield return "Microsoft.Data." + "Sql" + "ite";
        yield return "Sql" + "ite";
        yield return "SQL" + "ite";
        yield return "ESC" + "/" + "POS";
        yield return "Print" + "Document";
        yield return "Printer" + "Settings";
        yield return "System.Drawing." + "Printing";
        yield return "Pdf" + "Document";
        yield return "Export" + "ToDisk";
        yield return "Checkout" + "Presenter";
        yield return "Official" + "Checkout";
        yield return "Electronic" + "Journal" + "Append";
        yield return "Official" + "Document" + "Lifecycle";
        yield return "Official" + "ZReading" + "Service";
        yield return "Finalize" + "Async";
        yield return "Save" + "Async";
        yield return "IN" + "SERT";
        yield return "UP" + "DATE";
        yield return "DE" + "LETE";
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
