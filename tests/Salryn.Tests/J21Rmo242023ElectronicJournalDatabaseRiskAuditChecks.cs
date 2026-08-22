namespace Salryn.Tests;

public static class J21Rmo242023ElectronicJournalDatabaseRiskAuditChecks
{
    // Kirch strict dev guide:
    // Run() is the CoreSmoke entry point; it proves J21-5D is audit-only and blocks premature DB hardening.
    // Syntax note: static lets Program.cs call this guard without constructing a test object.
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string auditText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_ELECTRONIC_JOURNAL_DATABASE_RISK_AUDIT.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_5D_ELECTRONIC_JOURNAL_DATABASE_RISK_AUDIT.md");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string migrationText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Migrations",
            "BirElectronicJournalSchemaMigration.cs");

        string factoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Initialization",
            "DefaultDatabaseInitializationFactory.cs");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(auditText, "Status: database risk audit only.", "J21-5D audit must be audit-only.");
        RequireContains(auditText, "Do not rewrite the committed J21-5C version 4 migration.", "J21-5D must block rewriting version 4.");
        RequireContains(auditText, "Use a new forward migration for any future schema hardening.", "J21-5D must require forward migration strategy.");

        RequireContains(auditText, "J21-5D does not add schema changes.", "J21-5D must block schema changes.");
        RequireContains(auditText, "J21-5D does not add a DB migration.", "J21-5D must block DB migration.");
        RequireContains(auditText, "J21-5D does not create triggers.", "J21-5D must block trigger creation.");
        RequireContains(auditText, "J21-5D does not create an append service.", "J21-5D must block append service.");
        RequireContains(auditText, "J21-5D does not wire checkout.", "J21-5D must block checkout wiring.");
        RequireContains(auditText, "J21-5D does not write sales.receipt_snapshot.", "J21-5D must block receipt_snapshot writes.");
        RequireContains(auditText, "J21-5D does not persist official documents.", "J21-5D must block official persistence.");
        RequireContains(auditText, "J21-5D does not render output.", "J21-5D must block renderer.");
        RequireContains(auditText, "J21-5D does not print output.", "J21-5D must block printer.");
        RequireContains(auditText, "J21-5D does not change sequence allocation.", "J21-5D must block sequence changes.");
        RequireContains(auditText, "J21-5D does not add a tax engine.", "J21-5D must block tax engine.");
        RequireContains(auditText, "J21-5D does not add X/Z reading.", "J21-5D must block X/Z.");
        RequireContains(auditText, "J21-5D does not add UI.", "J21-5D must block UI.");
        RequireContains(auditText, "J21-5D does not claim official output readiness.", "J21-5D must block official output claim.");

        RequireContains(auditText, "Risk 1 - Append-Only Enforcement", "J21-5D must audit append-only risk.");
        RequireContains(auditText, "The database does not yet block UPDATE or DELETE on journal tables.", "J21-5D must identify missing UPDATE/DELETE block.");
        RequireContains(auditText, "use a forward version 5 migration to add SQLite triggers that block UPDATE and DELETE", "J21-5D must identify future append-only trigger path.");

        RequireContains(auditText, "Risk 2 - Reversal Reason Requirement", "J21-5D must audit reversal reason risk.");
        RequireContains(auditText, "VOID, REFUND, and CANCEL events can currently exist without a reason.", "J21-5D must identify missing reversal reason requirement.");
        RequireContains(auditText, "Require reversal reasons in both DB triggers and service validation.", "J21-5D must decide DB/service split for reversal reason.");

        RequireContains(auditText, "Risk 3 - Preservation Batch Requirement", "J21-5D must audit preservation batch risk.");
        RequireContains(auditText, "EXPORT, PRESERVATION, and BACKUP_RECOVERY events can currently exist without a preservation batch.", "J21-5D must identify missing preservation batch requirement.");
        RequireContains(auditText, "Require preservation batch link in both DB triggers and service validation.", "J21-5D must decide DB/service split for preservation batch.");

        RequireContains(auditText, "Risk 4 - Snapshot Payload/Hash Requirement", "J21-5D must audit snapshot risk.");
        RequireContains(auditText, "Nullable is acceptable because no renderer or snapshot persistence exists yet.", "J21-5D must preserve current nullable snapshot rationale.");
        RequireContains(auditText, "Do not harden snapshot fields yet.", "J21-5D must defer snapshot hardening.");

        RequireContains(auditText, "Risk 5 - Atomicity", "J21-5D must audit atomicity.");
        RequireContains(auditText, "official sequence allocation", "J21-5D must mention sequence allocation in atomicity.");
        RequireContains(auditText, "journal anchor insert", "J21-5D must mention journal anchor insert in atomicity.");
        RequireContains(auditText, "ORIGINAL event insert", "J21-5D must mention original event in atomicity.");

        RequireContains(auditText, "Risk 6 - Boundary Protection", "J21-5D must audit boundary protection.");
        RequireContains(auditText, "sales.receipt_snapshot is not the RMO journal.", "J21-5D must protect sales.receipt_snapshot boundary.");
        RequireContains(auditText, "audit_logs is not the RMO electronic journal.", "J21-5D must protect audit_logs boundary.");
        RequireContains(auditText, "print_jobs is not journal evidence.", "J21-5D must protect print_jobs boundary.");
        RequireContains(auditText, "sequence allocation remains separate from journal append", "J21-5D must protect sequence/journal boundary.");

        RequireContains(handoffText, "Status: database risk audit only.", "J21-5D handoff must be audit-only.");
        RequireContains(handoffText, "No schema changes.", "J21-5D handoff must block schema changes.");
        RequireContains(handoffText, "No DB migration.", "J21-5D handoff must block DB migration.");
        RequireContains(handoffText, "No trigger creation.", "J21-5D handoff must block trigger creation.");
        RequireContains(handoffText, "No append service.", "J21-5D handoff must block append service.");
        RequireContains(handoffText, "Do not add version 5 yet.", "J21-5D handoff must block version 5.");
        RequireContains(handoffText, "Ask main chat before J21-5E.", "J21-5D handoff must require confirmation before J21-5E.");

        RequireContains(matrixText, "## J21-5D Electronic Journal Database Risk Audit", "Compliance matrix must include J21-5D section.");
        RequireContains(coreSmokeText, "J21Rmo242023ElectronicJournalDatabaseRiskAuditChecks.Run();", "CoreSmoke must run J21-5D checks.");

        RequireContains(migrationText, "public int Version => 4;", "J21-5D must verify J21-5C remains version 4.");
        RequireContains(migrationText, "reason TEXT NULL", "J21-5D must verify current reason field is nullable risk source.");
        RequireContains(migrationText, "preservation_batch_id TEXT NULL", "J21-5D must verify current preservation batch field is nullable risk source.");
        RequireContains(migrationText, "snapshot_payload_hash TEXT NULL", "J21-5D must verify current snapshot hash is nullable risk source.");
        RequireContains(factoryText, "new BirElectronicJournalSchemaMigration()", "J21-5D must verify version 4 migration remains registered.");

        RequireDoesNotContain(auditText, "CREATE TRIGGER", "J21-5D audit must not define trigger SQL.");
        RequireDoesNotContain(handoffText, "CREATE TRIGGER", "J21-5D handoff must not define trigger SQL.");
        RequireDoesNotContain(auditText, "CREATE TABLE", "J21-5D audit must not define schema SQL.");
        RequireDoesNotContain(handoffText, "CREATE TABLE", "J21-5D handoff must not define schema SQL.");
    }

    // Kirch strict dev guide:
    // FindRepositoryRoot() walks upward until it finds Salryn.sln so this guard works from bin output or repo root.
    // Syntax note: DirectoryInfo? allows current to become null while walking parent folders.
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

    // Kirch strict dev guide:
    // ReadRequiredFile() joins repo root with path parts and loads the file that this guard audits.
    // Syntax note: params string[] lets callers pass path pieces without manually creating an array.
    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    // Kirch strict dev guide:
    // RequireContains() proves a required audit finding or guardrail phrase exists.
    // Syntax note: StringComparison.Ordinal makes matching exact and culture-independent.
    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch strict dev guide:
    // RequireDoesNotContain() blocks accidental implementation SQL in audit-only documents.
    // Syntax note: throwing InvalidOperationException makes CoreSmoke fail immediately on scope creep.
    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}