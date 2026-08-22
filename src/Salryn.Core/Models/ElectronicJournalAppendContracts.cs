namespace Salryn.Core.Models;

// Kirch: J21-5F names the future append inputs and atomic boundary without adding behavior.
// No database access, checkout wiring, renderer, printer, sequence allocation call, or official output belongs here.
public sealed record ElectronicJournalAppendContract(
    Guid BirMachineProfileId,
    Guid SaleId,
    Guid OfficialDocumentSequenceId,
    string OfficialDocumentNumber,
    ElectronicJournalDocumentKind DocumentKind,
    DateTimeOffset TransactionTimestamp,
    Guid CashierUserId,
    string? TerminalId,
    ElectronicJournalSnapshotEvidence? SnapshotEvidence,
    ElectronicJournalOriginalEventContract OriginalEvent,
    IReadOnlyList<ElectronicJournalAtomicBoundaryStep> AtomicBoundarySteps,
    IReadOnlyList<string> DeferredComplianceNotes);

// Kirch: The journal contract uses receipt/invoice identity without rendering receipt text.
public enum ElectronicJournalDocumentKind
{
    OfficialReceipt = 1,
    OfficialInvoice = 2
}

// Kirch: Snapshot evidence is optional until renderer and snapshot persistence are approved.
public sealed record ElectronicJournalSnapshotEvidence(
    string PayloadJson,
    string PayloadHash,
    string HashAlgorithm);

// Kirch: ORIGINAL event identity is modeled separately because DB hardening allows only one ORIGINAL per entry.
public sealed record ElectronicJournalOriginalEventContract(
    Guid EventId,
    Guid CashierUserId,
    DateTimeOffset CreatedAt);

// Kirch: These steps define the future single-transaction boundary before any append service is built.
public enum ElectronicJournalAtomicBoundaryStep
{
    OfficialSequenceAllocation = 1,
    SnapshotPayloadHashPersistence = 2,
    JournalAnchorInsert = 3,
    OriginalEventInsert = 4,
    SaleOfficialSnapshotLink = 5
}
