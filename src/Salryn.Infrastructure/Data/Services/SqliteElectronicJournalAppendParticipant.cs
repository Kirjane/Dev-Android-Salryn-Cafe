using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data.Transactions;

namespace Salryn.Infrastructure.Data.Services;

// Kirch: why - persists the journal anchor and ORIGINAL event inside the caller-owned transaction only.
public sealed class SqliteElectronicJournalAppendParticipant :
    ISqliteSharedTransactionParticipant<ElectronicJournalAppendPersistenceRequest, ElectronicJournalAppendPersistenceResult>
{
    public Task<Result<ElectronicJournalAppendPersistenceResult>> ExecuteAsync(
        SqliteSharedTransactionContext context,
        ElectronicJournalAppendPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Fail(Error.Validation(
                "ElectronicJournalAppend.SqliteContextRequired",
                "Shared SQLite transaction context is required.")));
        }

        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Fail(validation.ErrorDetail));
        }

        try
        {
            InsertJournalEntry(context, request);
            InsertOriginalEvent(context, request);

            ElectronicJournalAppendPersistenceResult result = new(
                JournalEntryId: request.JournalEntryId,
                OriginalEventId: request.Contract.OriginalEvent.EventId,
                OfficialDocumentNumber: request.Contract.OfficialDocumentNumber,
                AppendedAt: request.AppendedAt);

            return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Result<ElectronicJournalAppendPersistenceResult>.Fail(Error.Unexpected(
                "ElectronicJournalAppend.SqliteAppendFailed",
                ex.Message)));
        }
    }

    private static void InsertJournalEntry(
        SqliteSharedTransactionContext context,
        ElectronicJournalAppendPersistenceRequest request)
    {
        ElectronicJournalAppendContract contract = request.Contract;
        ElectronicJournalSnapshotEvidence? snapshot = contract.SnapshotEvidence;

        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            INSERT INTO bir_electronic_journal_entries (
                id,
                bir_machine_profile_id,
                sale_id,
                official_document_sequence_id,
                official_document_number,
                document_kind,
                transaction_timestamp,
                cashier_user_id,
                terminal_id,
                snapshot_payload_json,
                snapshot_payload_hash,
                snapshot_hash_algorithm,
                created_at,
                metadata_json
            )
            VALUES (
                $id,
                $machineProfileId,
                $saleId,
                $sequenceId,
                $documentNumber,
                $documentKind,
                $transactionTimestamp,
                $cashierUserId,
                $terminalId,
                $snapshotPayloadJson,
                $snapshotPayloadHash,
                $snapshotHashAlgorithm,
                $createdAt,
                $metadataJson
            );
            """;
        command.Parameters.AddWithValue("$id", request.JournalEntryId.ToString("D"));
        command.Parameters.AddWithValue("$machineProfileId", contract.BirMachineProfileId.ToString("D"));
        command.Parameters.AddWithValue("$saleId", contract.SaleId.ToString("D"));
        command.Parameters.AddWithValue("$sequenceId", contract.OfficialDocumentSequenceId.ToString("D"));
        command.Parameters.AddWithValue("$documentNumber", contract.OfficialDocumentNumber);
        command.Parameters.AddWithValue("$documentKind", ToDatabaseDocumentKind(contract.DocumentKind));
        command.Parameters.AddWithValue("$transactionTimestamp", contract.TransactionTimestamp.ToString("O"));
        command.Parameters.AddWithValue("$cashierUserId", contract.CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$terminalId", string.IsNullOrWhiteSpace(contract.TerminalId) ? DBNull.Value : contract.TerminalId);
        command.Parameters.AddWithValue("$snapshotPayloadJson", snapshot is null ? DBNull.Value : snapshot.PayloadJson);
        command.Parameters.AddWithValue("$snapshotPayloadHash", snapshot is null ? DBNull.Value : snapshot.PayloadHash);
        command.Parameters.AddWithValue("$snapshotHashAlgorithm", snapshot is null ? DBNull.Value : snapshot.HashAlgorithm);
        command.Parameters.AddWithValue("$createdAt", request.AppendedAt.ToString("O"));
        command.Parameters.AddWithValue("$metadataJson", DBNull.Value);

        command.ExecuteNonQuery();
    }

    private static void InsertOriginalEvent(
        SqliteSharedTransactionContext context,
        ElectronicJournalAppendPersistenceRequest request)
    {
        ElectronicJournalOriginalEventContract originalEvent = request.Contract.OriginalEvent;

        using SqliteCommand command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            INSERT INTO bir_electronic_journal_entry_events (
                id,
                journal_entry_id,
                event_type,
                original_marker,
                reprint_marker,
                void_marker,
                refund_marker,
                cancel_marker,
                cashier_user_id,
                reason,
                preservation_batch_id,
                created_at,
                metadata_json
            )
            VALUES (
                $id,
                $journalEntryId,
                $eventType,
                1,
                0,
                0,
                0,
                0,
                $cashierUserId,
                NULL,
                NULL,
                $createdAt,
                NULL
            );
            """;
        command.Parameters.AddWithValue("$id", originalEvent.EventId.ToString("D"));
        command.Parameters.AddWithValue("$journalEntryId", request.JournalEntryId.ToString("D"));
        command.Parameters.AddWithValue("$eventType", OriginalEventType);
        command.Parameters.AddWithValue("$cashierUserId", originalEvent.CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", originalEvent.CreatedAt.ToString("O"));

        command.ExecuteNonQuery();
    }

    private static Result ValidateRequest(ElectronicJournalAppendPersistenceRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.PersistenceRequestRequired",
                "Electronic journal append persistence request is required."));
        }

        if (request.JournalEntryId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.JournalEntryIdRequired",
                "Electronic journal entry id is required."));
        }

        if (request.AppendedAt == default)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.AppendedAtRequired",
                "Electronic journal append timestamp is required."));
        }

        if (request.Contract is null)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.ContractRequired",
                "Electronic journal append contract is required."));
        }

        ElectronicJournalAppendContract contract = request.Contract;

        if (contract.BirMachineProfileId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.MachineProfileRequired",
                "Machine profile id is required."));
        }

        if (contract.SaleId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SaleRequired",
                "Sale id is required."));
        }

        if (contract.OfficialDocumentSequenceId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SequenceRequired",
                "Official document sequence id is required."));
        }

        if (string.IsNullOrWhiteSpace(contract.OfficialDocumentNumber))
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.OfficialDocumentNumberRequired",
                "Official document number is required."));
        }

        if (ToDatabaseDocumentKind(contract.DocumentKind).Length == 0)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.InvalidDocumentKind",
                "Electronic journal document kind must be official receipt or official invoice."));
        }

        if (contract.CashierUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.CashierRequired",
                "Cashier user id is required."));
        }

        if (contract.OriginalEvent is null)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.OriginalEventRequired",
                "ORIGINAL event contract is required."));
        }

        if (contract.OriginalEvent.EventId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.OriginalEventIdRequired",
                "ORIGINAL event id is required."));
        }

        if (contract.OriginalEvent.CashierUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.OriginalEventCashierRequired",
                "ORIGINAL event cashier id is required."));
        }

        if (contract.OriginalEvent.CashierUserId != contract.CashierUserId)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.OriginalEventCashierMismatch",
                "ORIGINAL event cashier must match the journal entry cashier."));
        }

        if (contract.SnapshotEvidence is not null)
        {
            return ValidateSnapshotEvidence(contract.SnapshotEvidence);
        }

        return Result.Success();
    }

    private static Result ValidateSnapshotEvidence(ElectronicJournalSnapshotEvidence snapshotEvidence)
    {
        if (string.IsNullOrWhiteSpace(snapshotEvidence.PayloadJson))
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SnapshotPayloadRequired",
                "Snapshot payload JSON is required when snapshot evidence is supplied."));
        }

        if (string.IsNullOrWhiteSpace(snapshotEvidence.PayloadHash))
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SnapshotHashRequired",
                "Snapshot payload hash is required when snapshot evidence is supplied."));
        }

        if (string.IsNullOrWhiteSpace(snapshotEvidence.HashAlgorithm))
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SnapshotHashAlgorithmRequired",
                "Snapshot hash algorithm is required when snapshot evidence is supplied."));
        }

        return Result.Success();
    }

    internal static string ToDatabaseDocumentKind(ElectronicJournalDocumentKind documentKind)
    {
        return documentKind switch
        {
            ElectronicJournalDocumentKind.OfficialReceipt => "OFFICIAL_RECEIPT",
            ElectronicJournalDocumentKind.OfficialInvoice => "OFFICIAL_INVOICE",
            _ => string.Empty
        };
    }

    internal const string OriginalEventType = "ORIGINAL";
}
