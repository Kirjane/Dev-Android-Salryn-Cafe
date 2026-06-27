using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J22OfficialOutputTextFileSourceIdentityBoundaryChecks
{
    private static readonly Guid JournalEntryId = Guid.Parse("71111111-1111-1111-1111-111111111111");
    private static readonly Guid SaleId = Guid.Parse("72222222-2222-2222-2222-222222222222");
    private static readonly Guid MachineProfileId = Guid.Parse("73333333-3333-3333-3333-333333333333");
    private static readonly Guid SequenceId = Guid.Parse("74444444-4444-4444-4444-444444444444");
    private static readonly Guid OriginalEventId = Guid.Parse("75555555-5555-5555-5555-555555555555");
    private static readonly Guid CashierUserId = Guid.Parse("76666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset RangeStart = new(2026, 6, 27, 0, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset RangeEnd = new(2026, 6, 28, 0, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset AppendedAt = new(2026, 6, 27, 10, 15, 0, TimeSpan.FromHours(8));

    public static async Task RunAsync()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string sourceIdentity = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileSourceIdentityBoundary.cs");
        string sourceSelection = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileSourceSelectionBoundary.cs");
        string packageSupply = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFilePackageSupplyBoundary.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string salesHistory = Read(root, "src", "Salryn.App.WinForms", "Features", "Sales", "SalesHistoryForm.cs");
        string checkoutPresenter = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "Checkout" + "Presenter.cs");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(
            sourceIdentity,
            sourceSelection,
            packageSupply,
            appServices,
            salesHistory,
            checkoutPresenter,
            coreSmoke);
        await VerifyCoreEvidenceFixtureAsync();
    }

    private static void VerifySourceGuards(
        string sourceIdentity,
        string sourceSelection,
        string packageSupply,
        string appServices,
        string salesHistory,
        string checkoutPresenter,
        string coreSmoke)
    {
        RequireContains(sourceIdentity, "OfficialOutputTextFileSourceIdentityListRequest", "RMO-M1-C1 source identity list request must exist.");
        RequireContains(sourceIdentity, "OfficialOutputTextFileSourceIdentityCandidate", "RMO-M1-C1 source identity candidate must exist.");
        RequireContains(sourceIdentity, "OfficialOutputTextFileSourceIdentitySelectionRequest", "RMO-M1-C1 source identity selection request must exist.");
        RequireContains(sourceIdentity, "IOfficialOutputTextFileSourceIdentityBoundary", "RMO-M1-C1 source identity boundary contract must exist.");
        RequireContains(sourceIdentity, "public sealed class OfficialOutputTextFileSourceIdentityBoundary", "RMO-M1-C1 source identity boundary implementation must exist.");
        RequireContains(sourceIdentity, "IOfficialEvidenceReadService _officialEvidenceRead", "RMO-M1-C1 source identity boundary must depend on official evidence read service.");
        RequireContains(sourceIdentity, "_officialEvidenceRead.ExportAsync", "RMO-M1-C1 source identity boundary must list candidates from official evidence.");
        RequireContains(sourceIdentity, "OfficialEvidenceEntryReadModel entry", "RMO-M1-C1 source identity candidate must be built from official evidence entries.");

        RequireContains(sourceIdentity, "Guid JournalEntryId", "RMO-M1-C1 candidate must expose journal entry id.");
        RequireContains(sourceIdentity, "Guid SaleId", "RMO-M1-C1 candidate must expose sale id for corroboration.");
        RequireContains(sourceIdentity, "Guid BirMachineProfileId", "RMO-M1-C1 candidate must expose machine profile id.");
        RequireContains(sourceIdentity, "ElectronicJournalDocumentKind DocumentKind", "RMO-M1-C1 candidate must expose document kind.");
        RequireContains(sourceIdentity, "string OfficialDocumentNumber", "RMO-M1-C1 candidate must expose official document number.");
        RequireContains(sourceIdentity, "DateTimeOffset? TransactionTimestamp", "RMO-M1-C1 candidate must expose transaction timestamp when available.");
        RequireContains(sourceIdentity, "string? TerminalId", "RMO-M1-C1 candidate must expose terminal id when available.");
        RequireContains(sourceIdentity, "string? RegistrationScopeCode", "RMO-M1-C1 candidate must expose registration scope when available.");
        RequireContains(sourceIdentity, "string SourceReference", "M1-E2 candidate must expose official source reference.");
        RequireContains(sourceIdentity, "string SnapshotPayloadHash", "M1-E2 candidate must expose snapshot payload hash trust fact.");
        RequireContains(sourceIdentity, "string SnapshotHashAlgorithm", "M1-E2 candidate must expose snapshot hash algorithm trust fact.");
        RequireContains(sourceIdentity, "bool SnapshotHashMatches", "M1-E2 candidate must expose snapshot hash match trust fact.");
        RequireContains(sourceIdentity, "SnapshotPayloadHash: NormalizeRequired(entry.SnapshotPayloadHash)", "M1-E2 candidate must carry normalized official evidence snapshot payload hash.");
        RequireContains(sourceIdentity, "SnapshotHashAlgorithm: NormalizeRequired(entry.SnapshotHashAlgorithm)", "M1-E2 candidate must carry normalized official evidence snapshot hash algorithm.");
        RequireContains(sourceIdentity, "SnapshotHashMatches: entry.SnapshotHashMatches", "M1-E2 candidate must carry official evidence snapshot hash match state.");

        RequireContains(sourceIdentity, "if (!candidate.SnapshotHashMatches)", "M1-E2 source identity must reject candidates whose snapshot hash does not match.");
        RequireContains(sourceIdentity, "OfficialOutputTextFileSourceIdentity.SnapshotHashMismatch", "M1-E2 source identity must expose a stable snapshot hash mismatch validation code.");
        RequireContains(sourceIdentity, "if (string.IsNullOrWhiteSpace(candidate.SnapshotPayloadHash))", "M1-E2 source identity must reject candidates missing snapshot payload hash.");
        RequireContains(sourceIdentity, "OfficialOutputTextFileSourceIdentity.SnapshotPayloadHashRequired", "M1-E2 source identity must expose a stable missing payload hash validation code.");
        RequireContains(sourceIdentity, "if (string.IsNullOrWhiteSpace(candidate.SnapshotHashAlgorithm))", "M1-E2 source identity must reject candidates missing snapshot hash algorithm.");
        RequireContains(sourceIdentity, "OfficialOutputTextFileSourceIdentity.SnapshotHashAlgorithmRequired", "M1-E2 source identity must expose a stable missing hash algorithm validation code.");

        RequireContains(sourceIdentity, "new OfficialOutputSourceReadRequest", "RMO-M1-C1 must convert selected candidate to output source read request.");
        RequireContains(sourceIdentity, "JournalEntryId: candidate.JournalEntryId", "RMO-M1-C1 source selection must use journal entry id as primary trusted identity.");
        RequireContains(sourceIdentity, "OfficialDocumentNumber: candidate.OfficialDocumentNumber", "RMO-M1-C1 source selection must include document number as corroborating identity.");
        RequireContains(sourceIdentity, "DocumentKind: candidate.DocumentKind", "RMO-M1-C1 source selection must include document kind as corroborating identity.");
        RequireContains(sourceIdentity, "BirMachineProfileId: candidate.BirMachineProfileId", "RMO-M1-C1 source selection must include machine profile id as corroborating identity.");
        RequireContains(sourceIdentity, "RegistrationScopeCode: candidate.RegistrationScopeCode", "RMO-M1-C1 source selection must include registration scope when present.");
        RequireContains(sourceIdentity, "OfficialOutputTextFileSourceSelectionRules.ValidateRequest(selectionRequest)", "RMO-M1-C1 must reuse source-selection validation.");
        RequireContains(sourceIdentity, "OfficialOutputTextFileSourceSelectionRules.NormalizeRequest(selectionRequest)", "RMO-M1-C1 must return normalized source-selection request.");
        RequireContains(sourceIdentity, "sourceIdentity", "RMO-M1-C1 must stamp metadata with official source identity.");
        RequireContains(sourceIdentity, "OfficialEvidenceRead", "RMO-M1-C1 metadata must identify official evidence as source.");
        RequireContains(sourceIdentity, "selectedJournalEntryId", "RMO-M1-C1 metadata must preserve selected journal entry id.");
        RequireContains(sourceIdentity, "selectedOfficialDocumentNumber", "RMO-M1-C1 metadata must preserve selected document number.");

        RequireDoesNotContain(sourceIdentity, "ReceiptNo", "RMO-M1-C1 must not introduce receipt-number-only source selection.");
        RequireDoesNotContain(sourceIdentity, "FindSaleBy", "RMO-M1-C1 must not resolve source identity through sales history reads.");
        RequireDoesNotContain(sourceIdentity, "SaleReadRepository", "RMO-M1-C1 must not depend on sale read repository.");
        RequireDoesNotContain(sourceIdentity, "Checkout" + "Presenter", "RMO-M1-C1 must not depend on checkout presenter state.");
        RequireDoesNotContain(sourceIdentity, "IOfficialOutputTextFilePackageSupplyBoundary", "RMO-M1-C1 source identity boundary must not produce packages.");
        RequireDoesNotContain(sourceIdentity, "IAppOfficialOutputTextFileWorkflowGateway", "RMO-M1-C1 source identity boundary must not execute output workflows.");
        RequireDoesNotContain(sourceIdentity, "OfficialOutputTextFileWorkflowUiBoundary", "RMO-M1-C1 source identity boundary must not depend on UI trigger shell.");

        foreach (string marker in ForbiddenMarkers())
        {
            RequireDoesNotContain(sourceIdentity, marker, "RMO-M1-C1 source identity boundary must not contain forbidden marker: " + marker);
        }

        RequireContains(sourceSelection, "OfficialOutputTextFileSourceSelectionRequest", "RMO-M1-C1 must build on the existing source-selection request.");
        RequireContains(packageSupply, "OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest", "RMO-M1-C1 must preserve package supply source-selection bridge.");
        RequireContains(appServices, "public IOfficialOutputTextFileSourceIdentityBoundary OfficialOutputTextFileSourceIdentity { get; }", "RMO-M1-C1 AppServices must expose source identity boundary.");
        RequireContains(appServices, "new OfficialOutputTextFileSourceIdentityBoundary(OfficialEvidenceRead)", "RMO-M1-C1 AppServices must wire source identity over official evidence read.");
        RequireContains(appServices, "public IOfficialEvidenceReadService OfficialEvidenceRead { get; }", "RMO-M1-C1 AppServices must keep official evidence read service exposed.");

        RequireDoesNotContain(salesHistory, "OfficialOutputTextFileSourceIdentity", "RMO-M1-C1 must not wire source identity into Sales History UI.");
        RequireDoesNotContain(checkoutPresenter, "OfficialOutputTextFileSourceIdentity", "RMO-M1-C1 must not wire source identity into checkout presenter.");
        RequireContains(coreSmoke, "J22OfficialOutputTextFileSourceIdentityBoundaryChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run RMO-M1-C1 source identity checks.");
        RequireDoesNotContain(coreSmoke, "TryRunAsync(", "CoreSmoke must not execute official output text-file workflow UI commands.");
    }

    private static async Task VerifyCoreEvidenceFixtureAsync()
    {
        OfficialEvidenceReadRequest evidenceRequest = CreateEvidenceRequest();
        OfficialEvidenceExportResult evidence = CreateEvidenceExport();
        FakeOfficialEvidenceReadService evidenceRead = new(Result<OfficialEvidenceExportResult>.Success(evidence));

        Result<OfficialEvidenceExportResult> result =
            await evidenceRead.ExportAsync(evidenceRequest);

        OfficialEvidenceExportResult export = RequireValue(result, "RMO-M1-C1 official evidence fixture");
        OfficialEvidenceEntryReadModel entry = export.Entries[0];

        RequireEqual(JournalEntryId, entry.JournalEntryId, "RMO-M1-C1 official evidence must carry journal entry id.");
        RequireEqual(SaleId, entry.SaleId, "RMO-M1-C1 official evidence must carry sale id as corroborating field.");
        RequireEqual(MachineProfileId, entry.BirMachineProfileId, "RMO-M1-C1 official evidence must carry machine profile id.");
        RequireEqual(ElectronicJournalDocumentKind.OfficialReceipt, entry.DocumentKind, "RMO-M1-C1 official evidence must carry document kind.");
        RequireEqual("OR-2026-IDENTITY-001", entry.OfficialDocumentNumber, "RMO-M1-C1 official evidence must carry official document number.");
        RequireEqual("snapshot-hash", entry.SnapshotPayloadHash, "M1-E2 official evidence must carry snapshot payload hash.");
        RequireEqual("SHA-256", entry.SnapshotHashAlgorithm, "M1-E2 official evidence must carry snapshot hash algorithm.");
        RequireEqual(true, entry.SnapshotHashMatches, "M1-E2 official evidence must carry snapshot hash match state.");
        RequireEqual("LOCAL_SINGLE_STORE", export.Manifest.RegistrationScopeCode, "RMO-M1-C1 official evidence manifest must carry registration scope when available.");
    }

    private static OfficialEvidenceReadRequest CreateEvidenceRequest()
    {
        return new OfficialEvidenceReadRequest(
            RangeStart,
            RangeEnd,
            MachineProfileId,
            RegistrationScopeCode: null,
            ElectronicJournalDocumentKind.OfficialReceipt);
    }

    private static OfficialEvidenceExportResult CreateEvidenceExport()
    {
        return new OfficialEvidenceExportResult(
            new OfficialEvidenceExportManifest(
                ExportGeneratedAt: AppendedAt,
                RangeStartedAt: RangeStart,
                RangeEndedAt: RangeEnd,
                BirMachineProfileId: MachineProfileId,
                RegistrationScopeCode: "LOCAL_SINGLE_STORE",
                DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
                EntryCount: 1,
                ManifestHash: "manifest-hash",
                HashAlgorithm: "SHA-256",
                AllSnapshotHashesMatch: true),
            new[]
            {
                new OfficialEvidenceEntryReadModel(
                    JournalEntryId,
                    SaleId,
                    MachineProfileId,
                    ElectronicJournalDocumentKind.OfficialReceipt,
                    "OR-2026-IDENTITY-001",
                    SequenceId,
                    SnapshotPayloadJson: "{\"schema\":\"fixture\"}",
                    SnapshotPayloadHash: "snapshot-hash",
                    SnapshotHashAlgorithm: "SHA-256",
                    SnapshotHashMatches: true,
                    RecomputedSnapshotPayloadHash: "snapshot-hash",
                    OriginalEventId,
                    AppendedAt,
                    CashierUserId,
                    TerminalId: "POS-IDENTITY-01")
            });
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
        yield return "File.Write";
        yield return "FileStream";
        yield return "StreamWriter";
        yield return "Process.Start";
        yield return "IAppOfficialOutputTextFileWorkflowGateway";
        yield return "OfficialOutputTextFileWorkflowUiBoundary";
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

    private static T RequireValue<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(area + " failed: " + result.ErrorDetail.Code + " " + result.Error);
        }

        return result.Value;
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

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message + " Expected: " + expected + ". Actual: " + actual + ".");
        }
    }

    private sealed class FakeOfficialEvidenceReadService : IOfficialEvidenceReadService
    {
        private readonly Result<OfficialEvidenceExportResult> _result;

        public FakeOfficialEvidenceReadService(Result<OfficialEvidenceExportResult> result)
        {
            _result = result;
        }

        public Task<Result<OfficialEvidenceExportResult>> ExportAsync(
            OfficialEvidenceReadRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
