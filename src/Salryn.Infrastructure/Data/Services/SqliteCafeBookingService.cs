using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteCafeBookingService :
    SqliteRepositoryBase,
    ICafeBookingService
{
    private readonly SqliteSyncOutboxStore _outbox;

    public SqliteCafeBookingService(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
        _outbox = new SqliteSyncOutboxStore(connectionFactory);
    }

    public Result<CafeBookingMutationResult> Create(
        CafeBookingCreateMutation mutation)
    {
        string? validation = ValidateCreate(mutation);
        if (validation is not null)
            return Fail<CafeBookingMutationResult>(
                Error.Validation("CafeBooking.CreateInvalid", validation));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection, transaction, mutation.StoreId, mutation.ActorUserId,
                SalrynPermissions.BookingManage);
            if (authority.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, authority.ErrorDetail);

            Result<CafeBookingMutationResult>? replay =
                TryReplayRevision(
                    connection, transaction, mutation.OperationId, mutation.DeviceId,
                    mutation.BookingId, 0, mutation.ActorUserId,
                    mutation.OccurredAtUtc, mutation.Configuration, mutation.RevisionId,
                    CafeBookingOperationContract.Created);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (OutboxOperationExists(connection, transaction, mutation.OperationId))
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.OperationIdConflict",
                        "OperationId already exists without matching C9 Booking operation evidence."));

            if (BookingExists(connection, transaction, mutation.BookingId))
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.BookingIdConflict",
                        "BookingId is already retained by another Booking operation."));

            if (RevisionExists(connection, transaction, mutation.RevisionId))
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.RevisionIdConflict",
                        "RevisionId is already retained."));

            Result<CustomerSnapshot> customer =
                ReadCustomer(connection, transaction, mutation.Configuration.CustomerId);
            if (customer.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, customer.ErrorDetail);

            Result<MenuAuthority> menu =
                ReadMenuAuthority(
                    connection, transaction,
                    mutation.Configuration.MenuPublicationRevisionId,
                    mutation.StoreId);
            if (menu.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, menu.ErrorDetail);

            Result<IReadOnlyList<CafeBookingLineSnapshot>> lines =
                ResolveLines(menu.Value, mutation.Configuration.Lines, mutation.BookingId);
            if (lines.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, lines.ErrorDetail);

            long quotedTotal = checked(lines.Value.Sum(x => x.LineTotalCents));
            string? configurationError =
                ValidateConfiguration(mutation.Configuration, quotedTotal);
            if (configurationError is not null)
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Validation("CafeBooking.ConfigurationInvalid", configurationError));

            CafeBookingRevisionSnapshot revision = BuildRevision(
                mutation.BookingId,
                mutation.RevisionId,
                revisionNumber: 1,
                bookingVersion: 1,
                previousRevisionId: null,
                customer.Value,
                mutation.Configuration,
                quotedTotal,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                lines.Value);

            BookingRevisionCommittedV1 payload = ToRevisionContract(
                mutation.OperationId,
                mutation.BookingId,
                mutation.StoreId,
                expectedVersion: 0,
                fromStatus: null,
                CafeBookingStatuses.Draft,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                CafeBookingOperationContract.Created,
                revision);

            string json = CafeBookingCanonicalJson.Serialize(payload);
            EnsureCanonical(CafeBookingCanonicalJson.ParseRevision(json));
            string payloadHash = CafeBookingCanonicalJson.ComputeHash(json);

            InsertBooking(
                connection, transaction, mutation.BookingId, mutation.StoreId,
                revision.RevisionId, mutation.ActorUserId, mutation.OccurredAtUtc);
            InsertRevision(connection, transaction, revision);
            InsertOperation(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                mutation.DeviceId, mutation.StoreId,
                CafeBookingOperationContract.BookingRevisionCommittedV1,
                payloadHash, 0, 1, revision.RevisionId,
                CafeBookingStatuses.Draft, CafeBookingFulfillmentStates.NotStarted,
                mutation.ActorUserId, mutation.OccurredAtUtc);
            InsertEvent(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                0, 1, CafeBookingActions.Created, null, CafeBookingStatuses.Draft,
                CafeBookingFulfillmentStates.NotStarted,
                CafeBookingFulfillmentStates.NotStarted,
                revision.RevisionId, mutation.ActorUserId, mutation.OccurredAtUtc,
                null, payloadHash);
            Enqueue(
                connection, transaction, mutation.DeviceId, mutation.StoreId,
                mutation.ActorUserId, mutation.OperationId, mutation.BookingId, 0,
                CafeBookingOperationContract.BookingRevisionCommittedV1,
                mutation.OccurredAtUtc, json);

            transaction.Commit();
            return Result<CafeBookingMutationResult>.Success(
                new CafeBookingMutationResult(
                    mutation.BookingId, 1, revision.RevisionId,
                    CafeBookingStatuses.Draft,
                    CafeBookingFulfillmentStates.NotStarted,
                    false));
        }
        catch (BookingValidationException ex)
        {
            return Fail<CafeBookingMutationResult>(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Fail<CafeBookingMutationResult>(
                Error.Conflict("CafeBooking.CreateConstraintConflict", ex.Message));
        }
        catch (Exception ex) when (ExpectedException(ex))
        {
            return Fail<CafeBookingMutationResult>(
                Error.Unexpected("CafeBooking.CreateFailed", ex.Message));
        }
    }

    public Result<CafeBookingMutationResult> Revise(
        CafeBookingReviseMutation mutation)
    {
        string? validation = ValidateRevise(mutation);
        if (validation is not null)
            return Fail<CafeBookingMutationResult>(
                Error.Validation("CafeBooking.ReviseInvalid", validation));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection, transaction, mutation.StoreId, mutation.ActorUserId,
                SalrynPermissions.BookingManage);
            if (authority.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, authority.ErrorDetail);

            Result<CafeBookingMutationResult>? replay =
                TryReplayRevision(
                    connection, transaction, mutation.OperationId, mutation.DeviceId,
                    mutation.BookingId, mutation.ExpectedVersion, mutation.ActorUserId,
                    mutation.OccurredAtUtc, mutation.Configuration, mutation.RevisionId,
                    CafeBookingOperationContract.Revised);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (OutboxOperationExists(connection, transaction, mutation.OperationId))
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.OperationIdConflict",
                        "OperationId already exists without matching C9 Booking operation evidence."));

            BookingRow booking = RequireBooking(
                connection, transaction, mutation.BookingId, mutation.StoreId,
                mutation.ExpectedVersion);

            if (booking.Status is not (CafeBookingStatuses.Draft or CafeBookingStatuses.Confirmed) ||
                booking.FulfillmentState != CafeBookingFulfillmentStates.NotStarted)
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.RevisionFrozen",
                        "Only Draft or Confirmed / NotStarted Booking may be revised."));

            if (RevisionExists(connection, transaction, mutation.RevisionId))
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.RevisionIdConflict",
                        "RevisionId is already retained."));

            CafeBookingRevisionSnapshot current =
                ReadRevision(connection, transaction, booking.CurrentRevisionId);

            if (!string.Equals(
                    current.MenuPublicationRevisionId,
                    mutation.Configuration.MenuPublicationRevisionId.Trim(),
                    StringComparison.Ordinal))
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.MenuPublicationRebaseForbidden",
                        "Existing Booking must remain bound to its original MenuPublicationRevisionId."));

            Result<CustomerSnapshot> customer =
                ReadCustomer(connection, transaction, mutation.Configuration.CustomerId);
            if (customer.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, customer.ErrorDetail);

            Result<MenuAuthority> menu =
                ReadMenuAuthority(
                    connection, transaction,
                    current.MenuPublicationRevisionId,
                    mutation.StoreId);
            if (menu.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, menu.ErrorDetail);

            Result<IReadOnlyList<CafeBookingLineSnapshot>> lines =
                ResolveLines(menu.Value, mutation.Configuration.Lines, mutation.BookingId);
            if (lines.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, lines.ErrorDetail);

            long quotedTotal = checked(lines.Value.Sum(x => x.LineTotalCents));
            string? configurationError =
                ValidateConfiguration(mutation.Configuration, quotedTotal);
            if (configurationError is not null)
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Validation("CafeBooking.ConfigurationInvalid", configurationError));

            DepositTotals totals = ReadDepositTotals(
                connection, transaction, mutation.BookingId);
            if (quotedTotal < totals.PreFulfillmentCredit)
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.RevisionBelowRetainedDeposit",
                        "Booking quote cannot be revised below retained unrefunded deposit. Refund deposit first."));

            int resultingVersion = checked(mutation.ExpectedVersion + 1);
            int revisionNumber = ReadNextRevisionNumber(
                connection, transaction, mutation.BookingId);

            CafeBookingRevisionSnapshot revision = BuildRevision(
                mutation.BookingId,
                mutation.RevisionId,
                revisionNumber,
                resultingVersion,
                booking.CurrentRevisionId,
                customer.Value,
                mutation.Configuration,
                quotedTotal,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                lines.Value);

            BookingRevisionCommittedV1 payload = ToRevisionContract(
                mutation.OperationId,
                mutation.BookingId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                booking.Status,
                CafeBookingStatuses.Draft,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                CafeBookingOperationContract.Revised,
                revision);

            string json = CafeBookingCanonicalJson.Serialize(payload);
            EnsureCanonical(CafeBookingCanonicalJson.ParseRevision(json));
            string payloadHash = CafeBookingCanonicalJson.ComputeHash(json);

            InsertRevision(connection, transaction, revision);

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_bookings
                    SET current_version=$version,
                        current_revision_id=$revision,
                        status='Draft',
                        fulfillment_state='NotStarted',
                        updated_by_user_id=$actor,
                        updated_at_utc=$at
                    WHERE booking_id=$booking
                      AND store_id=$store
                      AND current_version=$expected
                      AND status IN ('Draft','Confirmed')
                      AND fulfillment_state='NotStarted';
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$revision", mutation.RevisionId.ToString("D"));
                update.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                update.Parameters.AddWithValue("$at", Utc(mutation.OccurredAtUtc));
                update.Parameters.AddWithValue("$booking", mutation.BookingId.ToString("D"));
                update.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);
                if (update.ExecuteNonQuery() != 1)
                    throw new BookingValidationException(
                        "CafeBooking.ConcurrentRevisionConflict",
                        "Booking lost ExpectedVersion ownership during revision.");
            }

            InsertOperation(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                mutation.DeviceId, mutation.StoreId,
                CafeBookingOperationContract.BookingRevisionCommittedV1,
                payloadHash, mutation.ExpectedVersion, resultingVersion,
                revision.RevisionId, CafeBookingStatuses.Draft,
                CafeBookingFulfillmentStates.NotStarted,
                mutation.ActorUserId, mutation.OccurredAtUtc);
            InsertEvent(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                mutation.ExpectedVersion, resultingVersion,
                CafeBookingActions.Revised, booking.Status, CafeBookingStatuses.Draft,
                booking.FulfillmentState, CafeBookingFulfillmentStates.NotStarted,
                revision.RevisionId, mutation.ActorUserId, mutation.OccurredAtUtc,
                null, payloadHash);
            Enqueue(
                connection, transaction, mutation.DeviceId, mutation.StoreId,
                mutation.ActorUserId, mutation.OperationId, mutation.BookingId,
                mutation.ExpectedVersion,
                CafeBookingOperationContract.BookingRevisionCommittedV1,
                mutation.OccurredAtUtc, json);

            transaction.Commit();
            return Result<CafeBookingMutationResult>.Success(
                new CafeBookingMutationResult(
                    mutation.BookingId, resultingVersion, revision.RevisionId,
                    CafeBookingStatuses.Draft,
                    CafeBookingFulfillmentStates.NotStarted,
                    false));
        }
        catch (BookingValidationException ex)
        {
            return Fail<CafeBookingMutationResult>(Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Fail<CafeBookingMutationResult>(
                Error.Conflict("CafeBooking.ReviseConstraintConflict", ex.Message));
        }
        catch (Exception ex) when (ExpectedException(ex))
        {
            return Fail<CafeBookingMutationResult>(
                Error.Unexpected("CafeBooking.ReviseFailed", ex.Message));
        }
    }

    public Result<CafeBookingMutationResult> AdvanceStatus(
        CafeBookingStatusMutation mutation)
    {
        if (!ValidIdentity(
                mutation.OperationId, mutation.DeviceId, mutation.BookingId,
                mutation.StoreId, mutation.ActorUserId) ||
            mutation.ExpectedVersion <= 0 ||
            mutation.TargetStatus is not (
                CafeBookingStatuses.Confirmed or
                CafeBookingStatuses.ReadyForFulfillment))
        {
            return Fail<CafeBookingMutationResult>(
                Error.Validation(
                    "CafeBooking.StatusInvalid",
                    "C9B status mutation requires stable identities, positive ExpectedVersion and Confirmed/ReadyForFulfillment target."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection, transaction, mutation.StoreId, mutation.ActorUserId,
                SalrynPermissions.BookingManage);
            if (authority.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, authority.ErrorDetail);

            Result<CafeBookingMutationResult>? replay =
                TryReplayStatus(connection, transaction, mutation);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (OutboxOperationExists(connection, transaction, mutation.OperationId))
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict("CafeBooking.OperationIdConflict", "OperationId already exists."));

            BookingRow booking = RequireBooking(
                connection, transaction, mutation.BookingId, mutation.StoreId,
                mutation.ExpectedVersion);

            string requiredFrom = mutation.TargetStatus == CafeBookingStatuses.Confirmed
                ? CafeBookingStatuses.Draft
                : CafeBookingStatuses.Confirmed;

            if (booking.Status != requiredFrom ||
                booking.FulfillmentState != CafeBookingFulfillmentStates.NotStarted)
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.StatusTransitionInvalid",
                        "Booking status transition is not valid from the retained current state."));

            CafeBookingRevisionSnapshot current =
                ReadRevision(connection, transaction, booking.CurrentRevisionId);

            if (mutation.TargetStatus == CafeBookingStatuses.ReadyForFulfillment)
            {
                DepositTotals totals = ReadDepositTotals(
                    connection, transaction, mutation.BookingId);
                if (totals.PreFulfillmentCredit < current.DepositRequirementCents)
                    return Rollback<CafeBookingMutationResult>(
                        transaction,
                        Error.Conflict(
                            "CafeBooking.DepositRequirementNotMet",
                            "ReadyForFulfillment requires retained deposit credit to meet DepositRequirementCents."));
            }

            int resultingVersion = checked(mutation.ExpectedVersion + 1);
            BookingStatusAdvancedV1 payload = new(
                CafeBookingOperationContract.BookingStatusAdvancedV1,
                mutation.OperationId,
                mutation.BookingId,
                booking.CurrentRevisionId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                resultingVersion,
                booking.Status,
                mutation.TargetStatus,
                booking.FulfillmentState,
                CafeBookingFulfillmentStates.NotStarted,
                mutation.ActorUserId,
                mutation.OccurredAtUtc.ToUniversalTime());

            string json = CafeBookingCanonicalJson.Serialize(payload);
            EnsureCanonical(CafeBookingCanonicalJson.ParseStatus(json));
            string payloadHash = CafeBookingCanonicalJson.ComputeHash(json);

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_bookings
                    SET current_version=$version,
                        status=$status,
                        updated_by_user_id=$actor,
                        updated_at_utc=$at
                    WHERE booking_id=$booking
                      AND store_id=$store
                      AND current_version=$expected
                      AND status=$from
                      AND fulfillment_state='NotStarted';
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$status", mutation.TargetStatus);
                update.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                update.Parameters.AddWithValue("$at", Utc(mutation.OccurredAtUtc));
                update.Parameters.AddWithValue("$booking", mutation.BookingId.ToString("D"));
                update.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);
                update.Parameters.AddWithValue("$from", booking.Status);
                if (update.ExecuteNonQuery() != 1)
                    throw new BookingValidationException(
                        "CafeBooking.ConcurrentStatusConflict",
                        "Booking lost ExpectedVersion ownership during status transition.");
            }

            InsertOperation(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                mutation.DeviceId, mutation.StoreId,
                CafeBookingOperationContract.BookingStatusAdvancedV1,
                payloadHash, mutation.ExpectedVersion, resultingVersion,
                booking.CurrentRevisionId, mutation.TargetStatus,
                CafeBookingFulfillmentStates.NotStarted,
                mutation.ActorUserId, mutation.OccurredAtUtc);
            InsertEvent(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                mutation.ExpectedVersion, resultingVersion,
                mutation.TargetStatus, booking.Status, mutation.TargetStatus,
                booking.FulfillmentState, CafeBookingFulfillmentStates.NotStarted,
                booking.CurrentRevisionId, mutation.ActorUserId, mutation.OccurredAtUtc,
                null, payloadHash);
            Enqueue(
                connection, transaction, mutation.DeviceId, mutation.StoreId,
                mutation.ActorUserId, mutation.OperationId, mutation.BookingId,
                mutation.ExpectedVersion,
                CafeBookingOperationContract.BookingStatusAdvancedV1,
                mutation.OccurredAtUtc, json);

            transaction.Commit();
            return Result<CafeBookingMutationResult>.Success(
                new CafeBookingMutationResult(
                    mutation.BookingId, resultingVersion, booking.CurrentRevisionId,
                    mutation.TargetStatus, CafeBookingFulfillmentStates.NotStarted,
                    false));
        }
        catch (BookingValidationException ex)
        {
            return Fail<CafeBookingMutationResult>(Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (ExpectedException(ex))
        {
            return Fail<CafeBookingMutationResult>(
                Error.Unexpected("CafeBooking.StatusFailed", ex.Message));
        }
    }

    public Result<CafeBookingMutationResult> Cancel(
        CafeBookingCancelMutation mutation)
    {
        string reason = Clean(mutation.Reason) ?? string.Empty;
        if (!ValidIdentity(
                mutation.OperationId, mutation.DeviceId, mutation.BookingId,
                mutation.StoreId, mutation.ActorUserId) ||
            mutation.ExpectedVersion <= 0 ||
            reason.Length == 0)
        {
            return Fail<CafeBookingMutationResult>(
                Error.Validation(
                    "CafeBooking.CancelInvalid",
                    "Cancellation requires stable identities, positive ExpectedVersion and reason."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection, transaction, mutation.StoreId, mutation.ActorUserId,
                SalrynPermissions.BookingCancel);
            if (authority.IsFailure)
                return Rollback<CafeBookingMutationResult>(transaction, authority.ErrorDetail);

            Result<CafeBookingMutationResult>? replay =
                TryReplayCancel(connection, transaction, mutation, reason);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (OutboxOperationExists(connection, transaction, mutation.OperationId))
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict("CafeBooking.OperationIdConflict", "OperationId already exists."));

            BookingRow booking = RequireBooking(
                connection, transaction, mutation.BookingId, mutation.StoreId,
                mutation.ExpectedVersion);

            bool preOrderCancellation =
                booking.FulfillmentState == CafeBookingFulfillmentStates.NotStarted &&
                booking.Status is (
                    CafeBookingStatuses.Draft or
                    CafeBookingStatuses.Confirmed or
                    CafeBookingStatuses.ReadyForFulfillment);

            Guid? boundOrderId = null;
            bool orderBoundCancellation =
                booking.FulfillmentState == CafeBookingFulfillmentStates.OrderBound &&
                booking.Status == CafeBookingStatuses.ReadyForFulfillment &&
                TryReadCancelledBoundOrder(
                    connection,
                    transaction,
                    mutation.BookingId,
                    out boundOrderId);

            if (!preOrderCancellation && !orderBoundCancellation)
                return Rollback<CafeBookingMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.CancelNotAllowed",
                        "Cancellation is allowed before OrderBound, or after OrderBound only when the exact bound C4 Order is already Cancelled and no final Sale exists."));

            int resultingVersion = checked(mutation.ExpectedVersion + 1);
            BookingCancelledV1 payload = new(
                CafeBookingOperationContract.BookingCancelledV1,
                mutation.OperationId, mutation.BookingId, booking.CurrentRevisionId,
                mutation.StoreId, mutation.ExpectedVersion, resultingVersion,
                booking.Status, CafeBookingStatuses.Cancelled,
                booking.FulfillmentState, booking.FulfillmentState,
                reason, mutation.ActorUserId, mutation.OccurredAtUtc.ToUniversalTime());

            string json = CafeBookingCanonicalJson.Serialize(payload);
            EnsureCanonical(CafeBookingCanonicalJson.ParseCancellation(json));
            string payloadHash = CafeBookingCanonicalJson.ComputeHash(json);

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_bookings
                    SET current_version=$version,
                        status='Cancelled',
                        fulfillment_state=$fulfillment,
                        updated_by_user_id=$actor,
                        updated_at_utc=$at,
                        cancelled_by_user_id=$actor,
                        cancelled_at_utc=$at,
                        cancellation_reason=$reason
                    WHERE booking_id=$booking
                      AND store_id=$store
                      AND current_version=$expected
                      AND status IN ('Draft','Confirmed','ReadyForFulfillment')
                      AND fulfillment_state=$fulfillment;
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$fulfillment", booking.FulfillmentState);
                update.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                update.Parameters.AddWithValue("$at", Utc(mutation.OccurredAtUtc));
                update.Parameters.AddWithValue("$reason", reason);
                update.Parameters.AddWithValue("$booking", mutation.BookingId.ToString("D"));
                update.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);
                if (update.ExecuteNonQuery() != 1)
                    throw new BookingValidationException(
                        "CafeBooking.ConcurrentCancelConflict",
                        "Booking lost ExpectedVersion ownership during cancellation.");
            }

            InsertOperation(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                mutation.DeviceId, mutation.StoreId,
                CafeBookingOperationContract.BookingCancelledV1,
                payloadHash, mutation.ExpectedVersion, resultingVersion,
                booking.CurrentRevisionId, CafeBookingStatuses.Cancelled,
                booking.FulfillmentState,
                mutation.ActorUserId, mutation.OccurredAtUtc);
            InsertEvent(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                mutation.ExpectedVersion, resultingVersion,
                CafeBookingActions.Cancelled, booking.Status, CafeBookingStatuses.Cancelled,
                booking.FulfillmentState, booking.FulfillmentState,
                booking.CurrentRevisionId, mutation.ActorUserId, mutation.OccurredAtUtc,
                reason, payloadHash,
                orderId: boundOrderId);
            Enqueue(
                connection, transaction, mutation.DeviceId, mutation.StoreId,
                mutation.ActorUserId, mutation.OperationId, mutation.BookingId,
                mutation.ExpectedVersion,
                CafeBookingOperationContract.BookingCancelledV1,
                mutation.OccurredAtUtc, json);

            transaction.Commit();
            return Result<CafeBookingMutationResult>.Success(
                new CafeBookingMutationResult(
                    mutation.BookingId, resultingVersion, booking.CurrentRevisionId,
                    CafeBookingStatuses.Cancelled,
                    booking.FulfillmentState,
                    false));
        }
        catch (BookingValidationException ex)
        {
            return Fail<CafeBookingMutationResult>(Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Fail<CafeBookingMutationResult>(
                Error.Conflict("CafeBooking.CancelConstraintConflict", ex.Message));
        }
        catch (Exception ex) when (ExpectedException(ex))
        {
            return Fail<CafeBookingMutationResult>(
                Error.Unexpected("CafeBooking.CancelFailed", ex.Message));
        }
    }

    public Result<CafeBookingDepositMutationResult> ReceiveDeposit(
        CafeBookingDepositReceiveMutation mutation)
    {
        string reference = Clean(mutation.Reference) ?? string.Empty;
        if (!ValidDepositIdentity(
                mutation.OperationId, mutation.DepositFactId, mutation.CashMovementId,
                mutation.DeviceId, mutation.StoreId, mutation.BookingId,
                mutation.BookingRevisionId, mutation.ActorUserId, mutation.CashierSessionId) ||
            mutation.AmountCents <= 0 ||
            reference.Length == 0)
        {
            return Fail<CafeBookingDepositMutationResult>(
                Error.Validation(
                    "CafeBooking.DepositReceiveInvalid",
                    "Deposit receive requires stable identities, positive amount and reference."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection, transaction, mutation.StoreId, mutation.ActorUserId,
                SalrynPermissions.BookingDepositReceive,
                SalrynPermissions.CashIn);
            if (authority.IsFailure)
                return Rollback<CafeBookingDepositMutationResult>(transaction, authority.ErrorDetail);

            Result session = ValidateSession(
                connection, transaction, mutation.CashierSessionId, mutation.ActorUserId);
            if (session.IsFailure)
                return Rollback<CafeBookingDepositMutationResult>(transaction, session.ErrorDetail);

            Result<CafeBookingDepositMutationResult>? replay =
                TryReplayDepositReceive(connection, transaction, mutation, reference);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (OutboxOperationExists(connection, transaction, mutation.OperationId))
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict("CafeBooking.OperationIdConflict", "OperationId already exists."));

            BookingRow booking = RequireBookingAnyVersion(
                connection, transaction, mutation.BookingId, mutation.StoreId);

            if (booking.Status is not (
                    CafeBookingStatuses.Confirmed or
                    CafeBookingStatuses.ReadyForFulfillment) ||
                booking.FulfillmentState != CafeBookingFulfillmentStates.NotStarted)
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.DepositReceiveStatusInvalid",
                        "Deposit receive is allowed only for Confirmed/ReadyForFulfillment NotStarted Booking."));

            if (booking.CurrentRevisionId != mutation.BookingRevisionId)
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.DepositRevisionStale",
                        "Deposit receive must reference the current accepted Booking revision."));

            if (DepositFactExists(connection, transaction, mutation.DepositFactId))
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict("CafeBooking.DepositFactIdConflict", "DepositFactId already exists."));
            if (CashMovementExists(connection, transaction, mutation.CashMovementId))
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict("CafeBooking.CashMovementIdConflict", "CashMovementId already exists."));

            BookingDepositReceivedV1 payload = new(
                CafeBookingOperationContract.BookingDepositReceivedV1,
                mutation.OperationId, mutation.DepositFactId, mutation.BookingId,
                mutation.BookingRevisionId, mutation.StoreId, mutation.ActorUserId,
                mutation.CashierSessionId, mutation.CashMovementId,
                mutation.AmountCents, reference, mutation.OccurredAtUtc.ToUniversalTime(),
                CafeBookingOperationContract.CashIn, mutation.AmountCents,
                CafeBookingCashReferenceTypes.DepositReceipt, mutation.DepositFactId);

            string json = CafeBookingCanonicalJson.Serialize(payload);
            EnsureCanonical(CafeBookingCanonicalJson.ParseDepositReceive(json));
            string payloadHash = CafeBookingCanonicalJson.ComputeHash(json);

            Result cash = SqliteCashMovementPersistence.Insert(
                connection, transaction,
                new CashMovement
                {
                    Id = mutation.CashMovementId,
                    CashierSessionId = mutation.CashierSessionId,
                    ActorUserId = mutation.ActorUserId,
                    MovementType = CashMovementType.CashIn,
                    Delta = Money.FromCents(mutation.AmountCents),
                    ReferenceType = CafeBookingCashReferenceTypes.DepositReceipt,
                    ReferenceId = mutation.DepositFactId,
                    Reason = reference,
                    CreatedAt = mutation.OccurredAtUtc.ToUniversalTime()
                });
            if (cash.IsFailure)
                return Rollback<CafeBookingDepositMutationResult>(transaction, cash.ErrorDetail);

            InsertDepositFact(
                connection, transaction,
                mutation.DepositFactId, mutation.OperationId, mutation.BookingId,
                mutation.StoreId, mutation.BookingRevisionId, "Received",
                mutation.ActorUserId, mutation.AmountCents, mutation.OccurredAtUtc,
                reference, mutation.CashierSessionId, mutation.CashMovementId, payloadHash);

            InsertOperation(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                mutation.DeviceId, mutation.StoreId,
                CafeBookingOperationContract.BookingDepositReceivedV1,
                payloadHash, booking.CurrentVersion, booking.CurrentVersion,
                booking.CurrentRevisionId, booking.Status, booking.FulfillmentState,
                mutation.ActorUserId, mutation.OccurredAtUtc);

            Enqueue(
                connection, transaction, mutation.DeviceId, mutation.StoreId,
                mutation.ActorUserId, mutation.OperationId, mutation.BookingId,
                booking.CurrentVersion,
                CafeBookingOperationContract.BookingDepositReceivedV1,
                mutation.OccurredAtUtc, json);

            transaction.Commit();
            return Result<CafeBookingDepositMutationResult>.Success(
                new CafeBookingDepositMutationResult(
                    mutation.BookingId, mutation.BookingRevisionId,
                    mutation.DepositFactId, mutation.CashMovementId,
                    mutation.AmountCents, false));
        }
        catch (BookingValidationException ex)
        {
            return Fail<CafeBookingDepositMutationResult>(Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Fail<CafeBookingDepositMutationResult>(
                Error.Conflict("CafeBooking.DepositReceiveConstraintConflict", ex.Message));
        }
        catch (Exception ex) when (ExpectedException(ex))
        {
            return Fail<CafeBookingDepositMutationResult>(
                Error.Unexpected("CafeBooking.DepositReceiveFailed", ex.Message));
        }
    }

    public Result<CafeBookingDepositMutationResult> RefundDeposit(
        CafeBookingDepositRefundMutation mutation)
    {
        string reason = Clean(mutation.Reason) ?? string.Empty;
        string reference = Clean(mutation.Reference) ?? string.Empty;
        if (!ValidDepositIdentity(
                mutation.OperationId, mutation.DepositFactId, mutation.CashMovementId,
                mutation.DeviceId, mutation.StoreId, mutation.BookingId,
                mutation.BookingRevisionId, mutation.ActorUserId, mutation.CashierSessionId) ||
            mutation.AmountCents <= 0 || reason.Length == 0 || reference.Length == 0 ||
            mutation.Allocations is null || mutation.Allocations.Count == 0 ||
            mutation.Allocations.Any(x =>
                x.ReceivedDepositFactId == Guid.Empty || x.AmountCents <= 0) ||
            mutation.Allocations.Select(x => x.ReceivedDepositFactId).Distinct().Count() !=
                mutation.Allocations.Count ||
            mutation.Allocations.Sum(x => x.AmountCents) != mutation.AmountCents)
        {
            return Fail<CafeBookingDepositMutationResult>(
                Error.Validation(
                    "CafeBooking.DepositRefundInvalid",
                    "Deposit refund requires stable identities, amount, reason/reference and exact positive unique allocations summing to refund amount."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection, transaction, mutation.StoreId, mutation.ActorUserId,
                SalrynPermissions.BookingDepositRefund,
                SalrynPermissions.CashOut);
            if (authority.IsFailure)
                return Rollback<CafeBookingDepositMutationResult>(transaction, authority.ErrorDetail);

            Result session = ValidateSession(
                connection, transaction, mutation.CashierSessionId, mutation.ActorUserId);
            if (session.IsFailure)
                return Rollback<CafeBookingDepositMutationResult>(transaction, session.ErrorDetail);

            Result<CafeBookingDepositMutationResult>? replay =
                TryReplayDepositRefund(connection, transaction, mutation, reason, reference);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (OutboxOperationExists(connection, transaction, mutation.OperationId))
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict("CafeBooking.OperationIdConflict", "OperationId already exists."));

            BookingRow booking = RequireBookingAnyVersion(
                connection, transaction, mutation.BookingId, mutation.StoreId);

            if (booking.Status is not (
                    CafeBookingStatuses.Draft or
                    CafeBookingStatuses.Confirmed or
                    CafeBookingStatuses.ReadyForFulfillment or
                    CafeBookingStatuses.Cancelled) ||
                booking.FulfillmentState is not (
                    CafeBookingFulfillmentStates.NotStarted or
                    CafeBookingFulfillmentStates.OrderBound))
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.DepositRefundStatusInvalid",
                        "C9 deposit refund is allowed only before final SaleBound fulfillment."));

            DepositTotals retained = ReadDepositTotals(
                connection,
                transaction,
                mutation.BookingId);
            if (retained.Applied != 0)
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.DepositAlreadyApplied",
                        "Applied Booking deposit belongs to the final C5 Sale and cannot be refunded through C9 deposit authority."));

            if (booking.CurrentRevisionId != mutation.BookingRevisionId)
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.DepositRevisionStale",
                        "Deposit refund must reference the current accepted Booking revision."));

            if (DepositFactExists(connection, transaction, mutation.DepositFactId))
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict("CafeBooking.DepositFactIdConflict", "DepositFactId already exists."));
            if (CashMovementExists(connection, transaction, mutation.CashMovementId))
                return Rollback<CafeBookingDepositMutationResult>(
                    transaction,
                    Error.Conflict("CafeBooking.CashMovementIdConflict", "CashMovementId already exists."));

            foreach (CafeBookingDepositAllocationMutation allocation in mutation.Allocations)
            {
                ReceivedDeposit? received = ReadReceivedDeposit(
                    connection, transaction, allocation.ReceivedDepositFactId);
                if (received is null ||
                    received.BookingId != mutation.BookingId ||
                    received.StoreId != mutation.StoreId)
                    return Rollback<CafeBookingDepositMutationResult>(
                        transaction,
                        Error.Conflict(
                            "CafeBooking.DepositAllocationSourceInvalid",
                            "Refund allocation must reference a Received fact from the same Booking and Store."));

                long consumed = ReadAllocatedTotal(
                    connection, transaction, allocation.ReceivedDepositFactId);
                if (checked(consumed + allocation.AmountCents) > received.AmountCents)
                    return Rollback<CafeBookingDepositMutationResult>(
                        transaction,
                        Error.Conflict(
                            "CafeBooking.DepositOverRefund",
                            "Refund allocation would exceed retained unconsumed Received deposit authority."));
            }

            IReadOnlyList<BookingDepositAllocationV1> allocations =
                mutation.Allocations
                    .OrderBy(x => x.ReceivedDepositFactId)
                    .Select(x => new BookingDepositAllocationV1(
                        x.ReceivedDepositFactId, x.AmountCents))
                    .ToArray();

            BookingDepositRefundedV1 payload = new(
                CafeBookingOperationContract.BookingDepositRefundedV1,
                mutation.OperationId, mutation.DepositFactId, mutation.BookingId,
                mutation.BookingRevisionId, mutation.StoreId, mutation.ActorUserId,
                mutation.CashierSessionId, mutation.CashMovementId,
                mutation.AmountCents, reason, reference,
                mutation.OccurredAtUtc.ToUniversalTime(),
                CafeBookingOperationContract.CashOut, -mutation.AmountCents,
                CafeBookingCashReferenceTypes.DepositRefund, mutation.DepositFactId,
                allocations);

            string json = CafeBookingCanonicalJson.Serialize(payload);
            EnsureCanonical(CafeBookingCanonicalJson.ParseDepositRefund(json));
            string payloadHash = CafeBookingCanonicalJson.ComputeHash(json);

            Result cash = SqliteCashMovementPersistence.Insert(
                connection, transaction,
                new CashMovement
                {
                    Id = mutation.CashMovementId,
                    CashierSessionId = mutation.CashierSessionId,
                    ActorUserId = mutation.ActorUserId,
                    MovementType = CashMovementType.CashOut,
                    Delta = Money.FromCents(-mutation.AmountCents),
                    ReferenceType = CafeBookingCashReferenceTypes.DepositRefund,
                    ReferenceId = mutation.DepositFactId,
                    Reason = reason,
                    CreatedAt = mutation.OccurredAtUtc.ToUniversalTime()
                });
            if (cash.IsFailure)
                return Rollback<CafeBookingDepositMutationResult>(transaction, cash.ErrorDetail);

            InsertDepositFact(
                connection, transaction,
                mutation.DepositFactId, mutation.OperationId, mutation.BookingId,
                mutation.StoreId, mutation.BookingRevisionId, "Refunded",
                mutation.ActorUserId, mutation.AmountCents, mutation.OccurredAtUtc,
                reference, mutation.CashierSessionId, mutation.CashMovementId, payloadHash);

            foreach (BookingDepositAllocationV1 allocation in allocations)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO cafe_booking_deposit_allocations(
                        action_fact_id,received_fact_id,amount_cents)
                    VALUES($action,$received,$amount);
                    """;
                insert.Parameters.AddWithValue("$action", mutation.DepositFactId.ToString("D"));
                insert.Parameters.AddWithValue("$received", allocation.ReceivedDepositFactId.ToString("D"));
                insert.Parameters.AddWithValue("$amount", allocation.AmountCents);
                insert.ExecuteNonQuery();
            }

            InsertOperation(
                connection, transaction, mutation.OperationId, mutation.BookingId,
                mutation.DeviceId, mutation.StoreId,
                CafeBookingOperationContract.BookingDepositRefundedV1,
                payloadHash, booking.CurrentVersion, booking.CurrentVersion,
                booking.CurrentRevisionId, booking.Status, booking.FulfillmentState,
                mutation.ActorUserId, mutation.OccurredAtUtc);

            Enqueue(
                connection, transaction, mutation.DeviceId, mutation.StoreId,
                mutation.ActorUserId, mutation.OperationId, mutation.BookingId,
                booking.CurrentVersion,
                CafeBookingOperationContract.BookingDepositRefundedV1,
                mutation.OccurredAtUtc, json);

            transaction.Commit();
            return Result<CafeBookingDepositMutationResult>.Success(
                new CafeBookingDepositMutationResult(
                    mutation.BookingId, mutation.BookingRevisionId,
                    mutation.DepositFactId, mutation.CashMovementId,
                    mutation.AmountCents, false));
        }
        catch (BookingValidationException ex)
        {
            return Fail<CafeBookingDepositMutationResult>(Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Fail<CafeBookingDepositMutationResult>(
                Error.Conflict("CafeBooking.DepositRefundConstraintConflict", ex.Message));
        }
        catch (Exception ex) when (ExpectedException(ex))
        {
            return Fail<CafeBookingDepositMutationResult>(
                Error.Unexpected("CafeBooking.DepositRefundFailed", ex.Message));
        }
    }

    public Result<CafeBookingDetail?> Get(
        Guid storeId,
        Guid actorUserId,
        Guid bookingId)
    {
        if (storeId == Guid.Empty || actorUserId == Guid.Empty || bookingId == Guid.Empty)
            return Fail<CafeBookingDetail?>(
                Error.Validation(
                    "CafeBooking.ReadIdentityInvalid",
                    "Store, actor and BookingId are required."));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection, transaction, storeId, actorUserId,
                SalrynPermissions.BookingView);
            if (authority.IsFailure)
                return Rollback<CafeBookingDetail?>(transaction, authority.ErrorDetail);

            BookingRow? booking = ReadBooking(connection, transaction, bookingId);
            if (booking is null || booking.StoreId != storeId)
            {
                transaction.Commit();
                return Result<CafeBookingDetail?>.Success(null);
            }

            CafeBookingRevisionSnapshot revision =
                ReadRevision(connection, transaction, booking.CurrentRevisionId);
            CafeBookingSummary summary =
                BuildSummary(connection, transaction, booking, revision);
            transaction.Commit();
            return Result<CafeBookingDetail?>.Success(
                new CafeBookingDetail(summary, revision));
        }
        catch (Exception ex) when (ExpectedException(ex))
        {
            return Fail<CafeBookingDetail?>(
                Error.Unexpected("CafeBooking.ReadFailed", ex.Message));
        }
    }

    public Result<IReadOnlyList<CafeBookingSummary>> List(
        Guid storeId,
        Guid actorUserId,
        string? status = null,
        string? bookingMode = null,
        DateTimeOffset? eventStartUtc = null,
        DateTimeOffset? eventEndUtc = null,
        Guid? customerId = null)
    {
        if (storeId == Guid.Empty || actorUserId == Guid.Empty)
            return Fail<IReadOnlyList<CafeBookingSummary>>(
                Error.Validation("CafeBooking.ListIdentityInvalid", "Store and actor are required."));

        string? normalizedStatus = Clean(status);
        string? normalizedMode = Clean(bookingMode);
        if (normalizedStatus is not null && !CafeBookingStatuses.IsKnown(normalizedStatus))
            return Fail<IReadOnlyList<CafeBookingSummary>>(
                Error.Validation("CafeBooking.StatusFilterInvalid", "Unknown Booking status filter."));
        if (normalizedMode is not null && !CafeBookingModes.IsKnown(normalizedMode))
            return Fail<IReadOnlyList<CafeBookingSummary>>(
                Error.Validation("CafeBooking.ModeFilterInvalid", "Unknown Booking mode filter."));
        if (eventStartUtc.HasValue && eventEndUtc.HasValue &&
            eventEndUtc.Value <= eventStartUtc.Value)
            return Fail<IReadOnlyList<CafeBookingSummary>>(
                Error.Validation("CafeBooking.DateFilterInvalid", "Event end filter must be after start filter."));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection, transaction, storeId, actorUserId,
                SalrynPermissions.BookingView);
            if (authority.IsFailure)
                return Rollback<IReadOnlyList<CafeBookingSummary>>(transaction, authority.ErrorDetail);

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT b.booking_id
                FROM cafe_bookings b
                INNER JOIN cafe_booking_revisions r
                  ON r.revision_id=b.current_revision_id
                WHERE b.store_id=$store
                  AND ($status IS NULL OR b.status=$status)
                  AND ($mode IS NULL OR r.booking_mode=$mode)
                  AND ($from IS NULL OR r.event_start_utc >= $from)
                  AND ($to IS NULL OR r.event_start_utc < $to)
                  AND ($customer IS NULL OR r.customer_id=$customer)
                ORDER BY r.event_start_utc,b.booking_id;
                """;
            command.Parameters.AddWithValue("$store", storeId.ToString("D"));
            command.Parameters.AddWithValue("$status", Db(normalizedStatus));
            command.Parameters.AddWithValue("$mode", Db(normalizedMode));
            command.Parameters.AddWithValue("$from", eventStartUtc.HasValue ? Utc(eventStartUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$to", eventEndUtc.HasValue ? Utc(eventEndUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$customer", customerId.HasValue ? customerId.Value.ToString("D") : DBNull.Value);

            List<Guid> ids = new();
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                    ids.Add(Guid.Parse(reader.GetString(0)));
            }

            List<CafeBookingSummary> result = new();
            foreach (Guid id in ids)
            {
                BookingRow row = ReadBooking(connection, transaction, id)!;
                CafeBookingRevisionSnapshot revision =
                    ReadRevision(connection, transaction, row.CurrentRevisionId);
                result.Add(BuildSummary(connection, transaction, row, revision));
            }

            transaction.Commit();
            return Result<IReadOnlyList<CafeBookingSummary>>.Success(result);
        }
        catch (Exception ex) when (ExpectedException(ex))
        {
            return Fail<IReadOnlyList<CafeBookingSummary>>(
                Error.Unexpected("CafeBooking.ListFailed", ex.Message));
        }
    }

    private Result<CafeBookingMutationResult>? TryReplayRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deviceId,
        Guid bookingId,
        int expectedVersion,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        CafeBookingConfigurationMutation configuration,
        Guid revisionId,
        string configurationAction)
    {
        OperationRow? operation = ReadOperation(connection, transaction, operationId);
        if (operation is null)
            return null;

        SyncOutboxItem? outbox = _outbox.Find(connection, transaction, operationId);
        if (outbox is null)
            return Fail<CafeBookingMutationResult>(
                Error.Unexpected(
                    "CafeBooking.ReplayOutboxMissing",
                    "Retained C9 Booking operation is missing its atomic semantic outbox evidence."));

        CafeBookingCanonicalParseResult<BookingRevisionCommittedV1> parsed =
            CafeBookingCanonicalJson.ParseRevision(outbox.Envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null ||
            operation.DeviceId != deviceId ||
            operation.BookingId != bookingId ||
            operation.ExpectedVersion != expectedVersion ||
            !string.Equals(operation.OperationType,
                CafeBookingOperationContract.BookingRevisionCommittedV1,
                StringComparison.Ordinal) ||
            !string.Equals(operation.PayloadHash, outbox.Envelope.PayloadHash, StringComparison.Ordinal) ||
            !RevisionRequestMatches(
                parsed.Payload, operationId, bookingId, expectedVersion,
                actorUserId, occurredAtUtc, configuration, revisionId, configurationAction))
        {
            return Fail<CafeBookingMutationResult>(
                Error.Conflict(
                    "CafeBooking.OperationPayloadConflict",
                    "OperationId already exists with different Booking revision evidence."));
        }

        return Result<CafeBookingMutationResult>.Success(
            new CafeBookingMutationResult(
                operation.BookingId, operation.ResultingVersion,
                operation.ResultRevisionId ??
                    throw new BookingValidationException(
                        "CafeBooking.ReplayRevisionMissing",
                        "Retained revision operation is missing ResultRevisionId."),
                operation.ResultStatus, operation.ResultFulfillmentState, true));
    }

    private Result<CafeBookingMutationResult>? TryReplayStatus(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingStatusMutation mutation)
    {
        OperationRow? op = ReadOperation(connection, transaction, mutation.OperationId);
        if (op is null) return null;
        SyncOutboxItem? outbox = _outbox.Find(connection, transaction, mutation.OperationId);
        if (outbox is null)
            return Fail<CafeBookingMutationResult>(
                Error.Unexpected("CafeBooking.ReplayOutboxMissing", "Retained C9 Booking operation is missing semantic outbox."));

        CafeBookingCanonicalParseResult<BookingStatusAdvancedV1> parsed =
            CafeBookingCanonicalJson.ParseStatus(outbox.Envelope.PayloadJson);
        BookingStatusAdvancedV1? p = parsed.Payload;
        if (!parsed.IsValid || p is null ||
            op.DeviceId != mutation.DeviceId ||
            p.OperationId != mutation.OperationId ||
            p.BookingId != mutation.BookingId ||
            p.StoreId != mutation.StoreId ||
            p.ExpectedVersion != mutation.ExpectedVersion ||
            p.ActorUserId != mutation.ActorUserId ||
            p.OccurredAtUtc != mutation.OccurredAtUtc.ToUniversalTime() ||
            p.ToStatus != mutation.TargetStatus ||
            !string.Equals(op.PayloadHash, outbox.Envelope.PayloadHash, StringComparison.Ordinal))
            return Fail<CafeBookingMutationResult>(
                Error.Conflict("CafeBooking.OperationPayloadConflict", "OperationId already exists with different status evidence."));

        return Result<CafeBookingMutationResult>.Success(
            new CafeBookingMutationResult(
                op.BookingId, op.ResultingVersion,
                op.ResultRevisionId ?? p.BookingRevisionId,
                op.ResultStatus, op.ResultFulfillmentState, true));
    }

    private Result<CafeBookingMutationResult>? TryReplayCancel(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingCancelMutation mutation,
        string reason)
    {
        OperationRow? op = ReadOperation(connection, transaction, mutation.OperationId);
        if (op is null) return null;
        SyncOutboxItem? outbox = _outbox.Find(connection, transaction, mutation.OperationId);
        if (outbox is null)
            return Fail<CafeBookingMutationResult>(
                Error.Unexpected("CafeBooking.ReplayOutboxMissing", "Retained C9 Booking operation is missing semantic outbox."));

        CafeBookingCanonicalParseResult<BookingCancelledV1> parsed =
            CafeBookingCanonicalJson.ParseCancellation(outbox.Envelope.PayloadJson);
        BookingCancelledV1? p = parsed.Payload;
        if (!parsed.IsValid || p is null ||
            op.DeviceId != mutation.DeviceId ||
            p.OperationId != mutation.OperationId ||
            p.BookingId != mutation.BookingId ||
            p.StoreId != mutation.StoreId ||
            p.ExpectedVersion != mutation.ExpectedVersion ||
            p.ActorUserId != mutation.ActorUserId ||
            p.OccurredAtUtc != mutation.OccurredAtUtc.ToUniversalTime() ||
            p.Reason != reason ||
            !string.Equals(op.PayloadHash, outbox.Envelope.PayloadHash, StringComparison.Ordinal))
            return Fail<CafeBookingMutationResult>(
                Error.Conflict("CafeBooking.OperationPayloadConflict", "OperationId already exists with different cancellation evidence."));

        return Result<CafeBookingMutationResult>.Success(
            new CafeBookingMutationResult(
                op.BookingId, op.ResultingVersion,
                op.ResultRevisionId ?? p.BookingRevisionId,
                op.ResultStatus, op.ResultFulfillmentState, true));
    }

    private Result<CafeBookingDepositMutationResult>? TryReplayDepositReceive(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingDepositReceiveMutation m,
        string reference)
    {
        OperationRow? op = ReadOperation(connection, transaction, m.OperationId);
        if (op is null) return null;
        SyncOutboxItem? outbox = _outbox.Find(connection, transaction, m.OperationId);
        if (outbox is null)
            return Fail<CafeBookingDepositMutationResult>(
                Error.Unexpected("CafeBooking.ReplayOutboxMissing", "Retained deposit operation is missing semantic outbox."));

        CafeBookingCanonicalParseResult<BookingDepositReceivedV1> parsed =
            CafeBookingCanonicalJson.ParseDepositReceive(outbox.Envelope.PayloadJson);
        BookingDepositReceivedV1? p = parsed.Payload;
        if (!parsed.IsValid || p is null || op.DeviceId != m.DeviceId ||
            p.OperationId != m.OperationId || p.DepositFactId != m.DepositFactId ||
            p.BookingId != m.BookingId || p.BookingRevisionId != m.BookingRevisionId ||
            p.StoreId != m.StoreId || p.ActorUserId != m.ActorUserId ||
            p.CashierSessionId != m.CashierSessionId ||
            p.CashMovementId != m.CashMovementId || p.AmountCents != m.AmountCents ||
            p.Reference != reference ||
            p.OccurredAtUtc != m.OccurredAtUtc.ToUniversalTime() ||
            !string.Equals(op.PayloadHash, outbox.Envelope.PayloadHash, StringComparison.Ordinal))
            return Fail<CafeBookingDepositMutationResult>(
                Error.Conflict("CafeBooking.OperationPayloadConflict", "OperationId already exists with different deposit-receive evidence."));

        return Result<CafeBookingDepositMutationResult>.Success(
            new CafeBookingDepositMutationResult(
                m.BookingId, m.BookingRevisionId, m.DepositFactId,
                m.CashMovementId, m.AmountCents, true));
    }

    private Result<CafeBookingDepositMutationResult>? TryReplayDepositRefund(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingDepositRefundMutation m,
        string reason,
        string reference)
    {
        OperationRow? op = ReadOperation(connection, transaction, m.OperationId);
        if (op is null) return null;
        SyncOutboxItem? outbox = _outbox.Find(connection, transaction, m.OperationId);
        if (outbox is null)
            return Fail<CafeBookingDepositMutationResult>(
                Error.Unexpected("CafeBooking.ReplayOutboxMissing", "Retained deposit operation is missing semantic outbox."));

        CafeBookingCanonicalParseResult<BookingDepositRefundedV1> parsed =
            CafeBookingCanonicalJson.ParseDepositRefund(outbox.Envelope.PayloadJson);
        BookingDepositRefundedV1? p = parsed.Payload;
        IReadOnlyList<BookingDepositAllocationV1> requested =
            m.Allocations.OrderBy(x => x.ReceivedDepositFactId)
                .Select(x => new BookingDepositAllocationV1(x.ReceivedDepositFactId, x.AmountCents))
                .ToArray();

        bool allocationsMatch =
            p is not null &&
            p.Allocations.SequenceEqual(requested);

        if (!parsed.IsValid || p is null || op.DeviceId != m.DeviceId ||
            p.OperationId != m.OperationId || p.DepositFactId != m.DepositFactId ||
            p.BookingId != m.BookingId || p.BookingRevisionId != m.BookingRevisionId ||
            p.StoreId != m.StoreId || p.ActorUserId != m.ActorUserId ||
            p.CashierSessionId != m.CashierSessionId ||
            p.CashMovementId != m.CashMovementId || p.AmountCents != m.AmountCents ||
            p.Reason != reason || p.Reference != reference || !allocationsMatch ||
            p.OccurredAtUtc != m.OccurredAtUtc.ToUniversalTime() ||
            !string.Equals(op.PayloadHash, outbox.Envelope.PayloadHash, StringComparison.Ordinal))
            return Fail<CafeBookingDepositMutationResult>(
                Error.Conflict("CafeBooking.OperationPayloadConflict", "OperationId already exists with different deposit-refund evidence."));

        return Result<CafeBookingDepositMutationResult>.Success(
            new CafeBookingDepositMutationResult(
                m.BookingId, m.BookingRevisionId, m.DepositFactId,
                m.CashMovementId, m.AmountCents, true));
    }

    private static bool RevisionRequestMatches(
        BookingRevisionCommittedV1 p,
        Guid operationId,
        Guid bookingId,
        int expectedVersion,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        CafeBookingConfigurationMutation c,
        Guid revisionId,
        string action)
    {
        BookingRevisionSnapshotV1 r = p.Revision;
        if (p.OperationId != operationId || p.BookingId != bookingId ||
            p.ExpectedVersion != expectedVersion || p.ActorUserId != actorUserId ||
            p.OccurredAtUtc != occurredAtUtc.ToUniversalTime() ||
            p.ConfigurationAction != action || r.RevisionId != revisionId ||
            r.CustomerId != c.CustomerId ||
            r.BookingMode != c.BookingMode.Trim() ||
            r.EventName != c.EventName.Trim() ||
            r.EventReference != Clean(c.EventReference) ||
            r.EventStartUtc != c.EventStartUtc.ToUniversalTime() ||
            r.EventEndUtc != c.EventEndUtc?.ToUniversalTime() ||
            r.Location != Clean(c.Location) ||
            r.SetupAtUtc != c.SetupAtUtc?.ToUniversalTime() ||
            r.ServiceWindowStartUtc != c.ServiceWindowStartUtc?.ToUniversalTime() ||
            r.ServiceWindowEndUtc != c.ServiceWindowEndUtc?.ToUniversalTime() ||
            r.Headcount != c.Headcount ||
            r.PackageLabel != Clean(c.PackageLabel) ||
            r.MenuPublicationRevisionId != c.MenuPublicationRevisionId.Trim() ||
            r.DepositRequirementCents != c.DepositRequirementCents ||
            r.SpecialInstructions != Clean(c.SpecialInstructions) ||
            r.Notes != Clean(c.Notes) ||
            r.IntendedServiceMode != c.IntendedServiceMode.Trim() ||
            r.Lines.Count != c.Lines.Count)
            return false;

        Dictionary<Guid,BookingLineSnapshotV1> retained =
            r.Lines.ToDictionary(x => x.BookingLineId);
        foreach (CafeBookingLineMutation line in c.Lines)
        {
            if (!retained.TryGetValue(line.BookingLineId, out BookingLineSnapshotV1? snap) ||
                snap.ProductId != line.ProductId ||
                snap.VariantId != Clean(line.VariantId) ||
                snap.QuantityX1000 != line.QuantityX1000 ||
                snap.LineNote != Clean(line.LineNote) ||
                snap.DisplayOrder != line.DisplayOrder)
                return false;

            string[] retainedOptions = snap.ModifierSelections
                .Select(x => x.OptionId)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            string[] requestedOptions = line.ModifierOptionIds
                .Select(x => x.Trim())
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            if (!retainedOptions.SequenceEqual(requestedOptions, StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    private static Result ValidateAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid actorUserId,
        params string[] permissions)
    {
        using (SqliteCommand store = connection.CreateCommand())
        {
            store.Transaction = transaction;
            store.CommandText = "SELECT COUNT(*) FROM deployment_identity WHERE store_id=$store;";
            store.Parameters.AddWithValue("$store", storeId.ToString("D"));
            if (Convert.ToInt64(store.ExecuteScalar() ?? 0L) != 1)
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.StoreAuthorityMismatch",
                        "Booking StoreId does not match local deployment authority."));
        }

        foreach (string permission in permissions)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(*)
                FROM users u
                INNER JOIN role_permissions rp ON rp.role_id=u.role_id
                WHERE u.id=$actor AND u.status=1 AND rp.permission=$permission;
                """;
            command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            command.Parameters.AddWithValue("$permission", permission);
            if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 1)
                return Result.Fail(
                    Error.Forbidden(
                        "CafeBooking.PermissionRequired",
                        "Booking operation requires permission: " + permission));
        }

        return Result.Success();
    }

    private static Result ValidateSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        Guid actorUserId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cashier_sessions
            WHERE id=$session AND user_id=$actor AND status=$open;
            """;
        command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$open", Convert.ToInt32(CashierSessionStatus.Open));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeBooking.OpenCashierSessionRequired",
                    "Booking deposit cash requires the actor's exact open cashier session."));
    }

    private static Result<CustomerSnapshot> ReadCustomer(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid customerId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT full_name,phone,address
            FROM customers
            WHERE id=$customer AND is_active=1
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return Result<CustomerSnapshot>.Fail(
                Error.Conflict(
                    "CafeBooking.CustomerMissing",
                    "Booking requires an active existing local customer."));

        return Result<CustomerSnapshot>.Success(
            new CustomerSnapshot(
                customerId,
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
    }

    private static Result<MenuAuthority> ReadMenuAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        Guid storeId)
    {
        string id = (revisionId ?? string.Empty).Trim();
        using (SqliteCommand revision = connection.CreateCommand())
        {
            revision.Transaction = transaction;
            revision.CommandText = """
                SELECT COUNT(*)
                FROM menu_revisions
                WHERE revision_id=$revision
                  AND store_id=$store
                  AND status IN ('Published','Superseded');
                """;
            revision.Parameters.AddWithValue("$revision", id);
            revision.Parameters.AddWithValue("$store", storeId.ToString("D"));
            if (Convert.ToInt64(revision.ExecuteScalar() ?? 0L) != 1)
                return Result<MenuAuthority>.Fail(
                    Error.Conflict(
                        "CafeBooking.MenuPublicationMissing",
                        "Booking MenuPublicationRevisionId must be an accepted immutable publication for this Store."));
        }

        Dictionary<Guid,MenuItem> items = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT product_id,name,COALESCE(unit_name,'Unit'),
                       selling_price_cents,is_temporarily_unavailable
                FROM menu_revision_items
                WHERE revision_id=$revision;
                """;
            command.Parameters.AddWithValue("$revision", id);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                Guid productId = Guid.Parse(reader.GetString(0));
                items[productId] = new MenuItem(
                    productId, reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3), reader.GetInt32(4) != 0);
            }
        }

        Dictionary<string,MenuVariant> variants = new(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT variant_id,product_id,name,selling_price_cents,is_temporarily_unavailable
                FROM menu_revision_variants
                WHERE revision_id=$revision;
                """;
            command.Parameters.AddWithValue("$revision", id);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                MenuVariant row = new(
                    reader.GetString(0), Guid.Parse(reader.GetString(1)),
                    reader.GetString(2), reader.GetInt64(3), reader.GetInt32(4) != 0);
                variants[row.Id] = row;
            }
        }

        Dictionary<string,MenuGroup> groups = new(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT group_id,name,is_required,min_selections,max_selections,display_order
                FROM menu_revision_modifier_groups
                WHERE revision_id=$revision;
                """;
            command.Parameters.AddWithValue("$revision", id);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                MenuGroup row = new(
                    reader.GetString(0), reader.GetString(1),
                    reader.GetInt32(2) != 0, reader.GetInt32(3),
                    reader.GetInt32(4), reader.GetInt32(5));
                groups[row.Id] = row;
            }
        }

        Dictionary<string,MenuOption> options = new(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT option_id,group_id,name,price_delta_cents,display_order
                FROM menu_revision_modifier_options
                WHERE revision_id=$revision;
                """;
            command.Parameters.AddWithValue("$revision", id);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                MenuOption row = new(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3), reader.GetInt32(4));
                options[row.Id] = row;
            }
        }

        return Result<MenuAuthority>.Success(
            new MenuAuthority(
                items, variants, groups, options,
                ReadGroupLinks(connection, transaction,
                    "menu_revision_item_modifier_groups", "product_id", id),
                ReadGroupLinks(connection, transaction,
                    "menu_revision_variant_modifier_groups", "variant_id", id)));
    }

    private static Result<IReadOnlyList<CafeBookingLineSnapshot>> ResolveLines(
        MenuAuthority menu,
        IReadOnlyList<CafeBookingLineMutation> mutations,
        Guid bookingId)
    {
        if (mutations is null || mutations.Count == 0)
            return Result<IReadOnlyList<CafeBookingLineSnapshot>>.Fail(
                Error.Validation("CafeBooking.LinesRequired", "Booking package requires at least one line."));

        if (mutations.Any(x =>
                x.BookingLineId == Guid.Empty || x.ProductId == Guid.Empty ||
                x.QuantityX1000 <= 0 ||
                x.ModifierOptionIds is null ||
                x.ModifierOptionIds.Any(string.IsNullOrWhiteSpace)) ||
            mutations.Select(x => x.BookingLineId).Distinct().Count() != mutations.Count)
            return Result<IReadOnlyList<CafeBookingLineSnapshot>>.Fail(
                Error.Validation(
                    "CafeBooking.LineInvalid",
                    "Booking lines require unique stable IDs, ProductId, positive quantity and valid modifier IDs."));

        List<CafeBookingLineSnapshot> lines = new();
        foreach (CafeBookingLineMutation m in
            mutations.OrderBy(x => x.DisplayOrder).ThenBy(x => x.BookingLineId))
        {
            MenuItem item = menu.Items.TryGetValue(m.ProductId, out MenuItem? found)
                ? found
                : throw new BookingValidationException(
                    "CafeBooking.ProductNotInPublication",
                    "Product is not in bound MenuPublication.");
            if (item.IsUnavailable)
                throw new BookingValidationException(
                    "CafeBooking.ProductUnavailable",
                    "Unavailable Product cannot be added to Booking revision.");

            MenuVariant? variant = null;
            string? variantId = Clean(m.VariantId);
            if (variantId is not null)
            {
                if (!menu.Variants.TryGetValue(variantId, out variant) ||
                    variant.ProductId != m.ProductId)
                    throw new BookingValidationException(
                        "CafeBooking.VariantMismatch",
                        "Variant does not belong to selected Product in bound MenuPublication.");
                if (variant.IsUnavailable)
                    throw new BookingValidationException(
                        "CafeBooking.VariantUnavailable",
                        "Unavailable Variant cannot be added to Booking revision.");
            }

            HashSet<string> allowedGroups = new(StringComparer.Ordinal);
            if (menu.ItemGroupLinks.TryGetValue(m.ProductId.ToString("D"), out HashSet<string>? itemGroups))
                allowedGroups.UnionWith(itemGroups);
            if (variant is not null &&
                menu.VariantGroupLinks.TryGetValue(variant.Id, out HashSet<string>? variantGroups))
                allowedGroups.UnionWith(variantGroups);

            string[] optionIds = m.ModifierOptionIds.Select(x => x.Trim()).ToArray();
            if (optionIds.Distinct(StringComparer.Ordinal).Count() != optionIds.Length)
                throw new BookingValidationException(
                    "CafeBooking.ModifierDuplicate",
                    "Modifier option identities must be unique per Booking line.");

            List<MenuOption> selected = new();
            foreach (string optionId in optionIds)
            {
                if (!menu.Options.TryGetValue(optionId, out MenuOption? option))
                    throw new BookingValidationException(
                        "CafeBooking.ModifierMissing",
                        "Modifier option is not in bound MenuPublication.");
                if (!allowedGroups.Contains(option.GroupId))
                    throw new BookingValidationException(
                        "CafeBooking.ModifierNotAllowed",
                        "Modifier option group is not linked to selected Product/Variant.");
                selected.Add(option);
            }

            foreach (string groupId in allowedGroups)
            {
                if (!menu.Groups.TryGetValue(groupId, out MenuGroup? group))
                    throw new BookingValidationException(
                        "CafeBooking.ModifierGroupMissing",
                        "Modifier group is missing from bound MenuPublication.");
                int count = selected.Count(x => x.GroupId == groupId);
                if (count < group.MinSelections || count > group.MaxSelections ||
                    (group.IsRequired && count < 1))
                    throw new BookingValidationException(
                        "CafeBooking.ModifierConstraint",
                        "Modifier selections violate published min/max authority.");
            }

            long basePrice = item.PriceCents;
            long selectedBase = variant?.PriceCents ?? basePrice;
            long quotedUnit = checked(selectedBase + selected.Sum(x => x.PriceDeltaCents));
            if (quotedUnit < 0)
                throw new BookingValidationException(
                    "CafeBooking.NegativeQuote",
                    "Published variant/modifier evidence cannot derive negative quote.");

            long lineTotal = checked((long)Math.Round(
                quotedUnit * (m.QuantityX1000 / 1000m),
                0, MidpointRounding.AwayFromZero));

            IReadOnlyList<CafeBookingModifierSelectionSnapshot> modifiers =
                selected.Select(option =>
                {
                    MenuGroup group = menu.Groups[option.GroupId];
                    return new CafeBookingModifierSelectionSnapshot(
                        group.Id, group.Name, option.Id, option.Name,
                        option.PriceDeltaCents, option.DisplayOrder);
                })
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.GroupId, StringComparer.Ordinal)
                .ThenBy(x => x.OptionId, StringComparer.Ordinal)
                .ToArray();

            lines.Add(new CafeBookingLineSnapshot(
                m.BookingLineId, m.ProductId, item.Name, item.UnitName,
                m.QuantityX1000, basePrice,
                variant?.Id, variant?.Name, variant?.PriceCents,
                quotedUnit, lineTotal, m.DisplayOrder, Clean(m.LineNote), modifiers));
        }

        return Result<IReadOnlyList<CafeBookingLineSnapshot>>.Success(lines);
    }

    private static string? ValidateConfiguration(
        CafeBookingConfigurationMutation c,
        long quotedTotal)
    {
        string? bookingMode = c.BookingMode?.Trim();
        string? intendedServiceMode = c.IntendedServiceMode?.Trim();

        if (c.CustomerId == Guid.Empty ||
            !CafeBookingModes.IsKnown(bookingMode) ||
            string.IsNullOrWhiteSpace(c.EventName) ||
            c.EventStartUtc == default ||
            string.IsNullOrWhiteSpace(c.MenuPublicationRevisionId) ||
            !CafeOrderServiceModes.IsSupported(intendedServiceMode) ||
            c.DepositRequirementCents < 0 ||
            c.DepositRequirementCents > quotedTotal)
            return "Booking customer/mode/event/menu/service/deposit requirement is invalid.";

        DateTimeOffset start = c.EventStartUtc.ToUniversalTime();
        DateTimeOffset? end = c.EventEndUtc?.ToUniversalTime();
        DateTimeOffset? setup = c.SetupAtUtc?.ToUniversalTime();
        DateTimeOffset? serviceStart = c.ServiceWindowStartUtc?.ToUniversalTime();
        DateTimeOffset? serviceEnd = c.ServiceWindowEndUtc?.ToUniversalTime();

        if (end.HasValue && end.Value <= start)
            return "EventEndUtc must be after EventStartUtc.";

        if (bookingMode == CafeBookingModes.CoffeeCart &&
            (!end.HasValue || string.IsNullOrWhiteSpace(c.Location) ||
             intendedServiceMode != CafeOrderServiceModes.Takeout))
            return "CoffeeCart requires EventEndUtc, Location and Takeout.";

        if (setup.HasValue && setup.Value > start)
            return "SetupAtUtc cannot be after EventStartUtc.";
        if (serviceStart.HasValue && setup.HasValue && serviceStart.Value < setup.Value)
            return "ServiceWindowStartUtc cannot precede SetupAtUtc.";
        if (serviceEnd.HasValue && !serviceStart.HasValue)
            return "ServiceWindowEndUtc requires ServiceWindowStartUtc.";
        if (serviceEnd.HasValue && serviceStart.HasValue &&
            serviceEnd.Value <= serviceStart.Value)
            return "ServiceWindowEndUtc must be after ServiceWindowStartUtc.";
        if (end.HasValue && serviceStart.HasValue && serviceStart.Value > end.Value)
            return "ServiceWindowStartUtc cannot be after EventEndUtc.";
        if (end.HasValue && serviceEnd.HasValue && serviceEnd.Value > end.Value)
            return "ServiceWindowEndUtc cannot be after EventEndUtc.";

        if (c.Headcount.HasValue && c.Headcount.Value <= 0)
            return "Headcount must be positive when supplied.";

        return null;
    }

    private static CafeBookingRevisionSnapshot BuildRevision(
        Guid bookingId,
        Guid revisionId,
        int revisionNumber,
        int bookingVersion,
        Guid? previousRevisionId,
        CustomerSnapshot customer,
        CafeBookingConfigurationMutation c,
        long quotedTotal,
        Guid actorUserId,
        DateTimeOffset at,
        IReadOnlyList<CafeBookingLineSnapshot> lines)
    {
        string contentHash = ComputeRevisionHash(
            bookingId, revisionId, revisionNumber, bookingVersion,
            previousRevisionId, customer, c, quotedTotal, actorUserId, at, lines);

        return new CafeBookingRevisionSnapshot(
            revisionId, bookingId, revisionNumber, bookingVersion, previousRevisionId,
            customer.CustomerId, customer.Name, Clean(customer.Phone), Clean(customer.Address),
            c.BookingMode.Trim(), c.EventName.Trim(), Clean(c.EventReference),
            c.EventStartUtc.ToUniversalTime(), c.EventEndUtc?.ToUniversalTime(),
            Clean(c.Location), c.SetupAtUtc?.ToUniversalTime(),
            c.ServiceWindowStartUtc?.ToUniversalTime(),
            c.ServiceWindowEndUtc?.ToUniversalTime(), c.Headcount,
            Clean(c.PackageLabel), c.MenuPublicationRevisionId.Trim(),
            quotedTotal, c.DepositRequirementCents, Clean(c.SpecialInstructions),
            Clean(c.Notes), c.IntendedServiceMode.Trim(), actorUserId,
            at.ToUniversalTime(), contentHash, lines);
    }

    private static string ComputeRevisionHash(
        Guid bookingId,
        Guid revisionId,
        int revisionNumber,
        int bookingVersion,
        Guid? previousRevisionId,
        CustomerSnapshot customer,
        CafeBookingConfigurationMutation c,
        long quotedTotal,
        Guid actorUserId,
        DateTimeOffset at,
        IReadOnlyList<CafeBookingLineSnapshot> lines)
    {
        StringBuilder b = new();
        b.Append("salryn-c9-booking-revision-v1|")
            .Append(bookingId.ToString("D")).Append('|')
            .Append(revisionId.ToString("D")).Append('|')
            .Append(revisionNumber).Append('|')
            .Append(bookingVersion).Append('|')
            .Append(previousRevisionId?.ToString("D") ?? "").Append('|')
            .Append(customer.CustomerId.ToString("D")).Append('|')
            .Append(customer.Name.Trim()).Append('|')
            .Append(Clean(customer.Phone) ?? "").Append('|')
            .Append(Clean(customer.Address) ?? "").Append('|')
            .Append(c.BookingMode.Trim()).Append('|')
            .Append(c.EventName.Trim()).Append('|')
            .Append(Clean(c.EventReference) ?? "").Append('|')
            .Append(Utc(c.EventStartUtc)).Append('|')
            .Append(c.EventEndUtc.HasValue ? Utc(c.EventEndUtc.Value) : "").Append('|')
            .Append(Clean(c.Location) ?? "").Append('|')
            .Append(c.SetupAtUtc.HasValue ? Utc(c.SetupAtUtc.Value) : "").Append('|')
            .Append(c.ServiceWindowStartUtc.HasValue ? Utc(c.ServiceWindowStartUtc.Value) : "").Append('|')
            .Append(c.ServiceWindowEndUtc.HasValue ? Utc(c.ServiceWindowEndUtc.Value) : "").Append('|')
            .Append(c.Headcount?.ToString(CultureInfo.InvariantCulture) ?? "").Append('|')
            .Append(Clean(c.PackageLabel) ?? "").Append('|')
            .Append(c.MenuPublicationRevisionId.Trim()).Append('|')
            .Append(quotedTotal).Append('|')
            .Append(c.DepositRequirementCents).Append('|')
            .Append(Clean(c.SpecialInstructions) ?? "").Append('|')
            .Append(Clean(c.Notes) ?? "").Append('|')
            .Append(c.IntendedServiceMode.Trim()).Append('|')
            .Append(actorUserId.ToString("D")).Append('|')
            .Append(Utc(at));

        foreach (CafeBookingLineSnapshot line in lines
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.BookingLineId))
        {
            b.Append("|L|").Append(line.BookingLineId.ToString("D")).Append('|')
                .Append(line.ProductId.ToString("D")).Append('|')
                .Append(line.ProductNameSnapshot).Append('|')
                .Append(line.UnitNameSnapshot).Append('|')
                .Append(line.QuantityX1000).Append('|')
                .Append(line.BaseUnitPriceCents).Append('|')
                .Append(line.VariantId ?? "").Append('|')
                .Append(line.VariantNameSnapshot ?? "").Append('|')
                .Append(line.VariantUnitPriceCents?.ToString(CultureInfo.InvariantCulture) ?? "").Append('|')
                .Append(line.QuotedUnitPriceCents).Append('|')
                .Append(line.LineTotalCents).Append('|')
                .Append(line.DisplayOrder).Append('|')
                .Append(line.LineNote ?? "");

            foreach (CafeBookingModifierSelectionSnapshot m in line.ModifierSelections)
                b.Append("|M|").Append(m.GroupId).Append('|')
                    .Append(m.GroupNameSnapshot).Append('|')
                    .Append(m.OptionId).Append('|')
                    .Append(m.OptionNameSnapshot).Append('|')
                    .Append(m.PriceDeltaCents).Append('|')
                    .Append(m.DisplayOrder);
        }

        return Sha(b.ToString());
    }

    private static BookingRevisionCommittedV1 ToRevisionContract(
        Guid operationId,
        Guid bookingId,
        Guid storeId,
        int expectedVersion,
        string? fromStatus,
        string toStatus,
        Guid actorUserId,
        DateTimeOffset at,
        string action,
        CafeBookingRevisionSnapshot revision) =>
        new(
            CafeBookingOperationContract.BookingRevisionCommittedV1,
            action, operationId, bookingId, storeId,
            expectedVersion, checked(expectedVersion + 1),
            fromStatus, toStatus,
            CafeBookingFulfillmentStates.NotStarted,
            CafeBookingFulfillmentStates.NotStarted,
            actorUserId, at.ToUniversalTime(),
            ToContract(revision));

    private static BookingRevisionSnapshotV1 ToContract(CafeBookingRevisionSnapshot r) =>
        new(
            r.RevisionId, r.BookingId, r.RevisionNumber, r.BookingVersion,
            r.PreviousRevisionId, r.CustomerId, r.CustomerNameSnapshot,
            r.CustomerPhoneSnapshot, r.CustomerAddressSnapshot,
            r.BookingMode, r.EventName, r.EventReference,
            r.EventStartUtc, r.EventEndUtc, r.Location, r.SetupAtUtc,
            r.ServiceWindowStartUtc, r.ServiceWindowEndUtc, r.Headcount,
            r.PackageLabel, r.MenuPublicationRevisionId, r.QuotedTotalCents,
            r.DepositRequirementCents, r.SpecialInstructions, r.Notes,
            r.IntendedServiceMode, r.ActorUserId, r.CreatedAtUtc, r.ContentHash,
            r.Lines.Select(line => new BookingLineSnapshotV1(
                line.BookingLineId, line.ProductId, line.ProductNameSnapshot,
                line.UnitNameSnapshot, line.QuantityX1000, line.BaseUnitPriceCents,
                line.VariantId, line.VariantNameSnapshot, line.VariantUnitPriceCents,
                line.QuotedUnitPriceCents, line.LineTotalCents, line.DisplayOrder,
                line.LineNote,
                line.ModifierSelections.Select(m => new BookingModifierSelectionV1(
                    m.GroupId, m.GroupNameSnapshot, m.OptionId, m.OptionNameSnapshot,
                    m.PriceDeltaCents, m.DisplayOrder)).ToArray())).ToArray());

    private static void InsertBooking(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId,
        Guid storeId,
        Guid revisionId,
        Guid actor,
        DateTimeOffset at)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_bookings(
                booking_id,store_id,current_version,current_revision_id,
                status,fulfillment_state,created_by_user_id,created_at_utc,
                updated_by_user_id,updated_at_utc,
                cancelled_by_user_id,cancelled_at_utc,cancellation_reason)
            VALUES(
                $booking,$store,1,$revision,
                'Draft','NotStarted',$actor,$at,
                $actor,$at,NULL,NULL,NULL);
            """;
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$actor", actor.ToString("D"));
        command.Parameters.AddWithValue("$at", Utc(at));
        command.ExecuteNonQuery();
    }

    private static void InsertRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingRevisionSnapshot r)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cafe_booking_revisions(
                    revision_id,booking_id,revision_number,booking_version,previous_revision_id,
                    customer_id,customer_name_snapshot,customer_phone_snapshot,customer_address_snapshot,
                    booking_mode,event_name,event_reference,event_start_utc,event_end_utc,location,
                    setup_at_utc,service_window_start_utc,service_window_end_utc,headcount,package_label,
                    menu_publication_revision_id,quoted_total_cents,deposit_requirement_cents,
                    special_instructions,notes,fulfillment_service_mode,actor_user_id,created_at_utc,content_hash)
                VALUES(
                    $revision,$booking,$number,$version,$previous,
                    $customer,$name,$phone,$address,
                    $mode,$event,$eventRef,$start,$end,$location,
                    $setup,$serviceStart,$serviceEnd,$headcount,$package,
                    $menu,$quoted,$deposit,$instructions,$notes,$serviceMode,$actor,$at,$hash);
                """;
            command.Parameters.AddWithValue("$revision", r.RevisionId.ToString("D"));
            command.Parameters.AddWithValue("$booking", r.BookingId.ToString("D"));
            command.Parameters.AddWithValue("$number", r.RevisionNumber);
            command.Parameters.AddWithValue("$version", r.BookingVersion);
            command.Parameters.AddWithValue("$previous", Db(r.PreviousRevisionId?.ToString("D")));
            command.Parameters.AddWithValue("$customer", r.CustomerId.ToString("D"));
            command.Parameters.AddWithValue("$name", r.CustomerNameSnapshot);
            command.Parameters.AddWithValue("$phone", Db(r.CustomerPhoneSnapshot));
            command.Parameters.AddWithValue("$address", Db(r.CustomerAddressSnapshot));
            command.Parameters.AddWithValue("$mode", r.BookingMode);
            command.Parameters.AddWithValue("$event", r.EventName);
            command.Parameters.AddWithValue("$eventRef", Db(r.EventReference));
            command.Parameters.AddWithValue("$start", Utc(r.EventStartUtc));
            command.Parameters.AddWithValue("$end", r.EventEndUtc.HasValue ? Utc(r.EventEndUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$location", Db(r.Location));
            command.Parameters.AddWithValue("$setup", r.SetupAtUtc.HasValue ? Utc(r.SetupAtUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$serviceStart", r.ServiceWindowStartUtc.HasValue ? Utc(r.ServiceWindowStartUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$serviceEnd", r.ServiceWindowEndUtc.HasValue ? Utc(r.ServiceWindowEndUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$headcount", r.Headcount ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$package", Db(r.PackageLabel));
            command.Parameters.AddWithValue("$menu", r.MenuPublicationRevisionId);
            command.Parameters.AddWithValue("$quoted", r.QuotedTotalCents);
            command.Parameters.AddWithValue("$deposit", r.DepositRequirementCents);
            command.Parameters.AddWithValue("$instructions", Db(r.SpecialInstructions));
            command.Parameters.AddWithValue("$notes", Db(r.Notes));
            command.Parameters.AddWithValue("$serviceMode", r.IntendedServiceMode);
            command.Parameters.AddWithValue("$actor", r.ActorUserId.ToString("D"));
            command.Parameters.AddWithValue("$at", Utc(r.CreatedAtUtc));
            command.Parameters.AddWithValue("$hash", r.ContentHash);
            command.ExecuteNonQuery();
        }

        foreach (CafeBookingLineSnapshot line in r.Lines)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_booking_revision_lines(
                        revision_id,booking_line_id,product_id,product_name_snapshot,unit_name_snapshot,
                        quantity_x1000,base_unit_price_cents,variant_id,variant_name_snapshot,
                        variant_unit_price_cents,quoted_unit_price_cents,line_total_cents,display_order,line_note)
                    VALUES(
                        $revision,$line,$product,$name,$unit,
                        $quantity,$base,$variant,$variantName,
                        $variantPrice,$quoted,$total,$display,$note);
                    """;
                command.Parameters.AddWithValue("$revision", r.RevisionId.ToString("D"));
                command.Parameters.AddWithValue("$line", line.BookingLineId.ToString("D"));
                command.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
                command.Parameters.AddWithValue("$name", line.ProductNameSnapshot);
                command.Parameters.AddWithValue("$unit", line.UnitNameSnapshot);
                command.Parameters.AddWithValue("$quantity", line.QuantityX1000);
                command.Parameters.AddWithValue("$base", line.BaseUnitPriceCents);
                command.Parameters.AddWithValue("$variant", Db(line.VariantId));
                command.Parameters.AddWithValue("$variantName", Db(line.VariantNameSnapshot));
                command.Parameters.AddWithValue("$variantPrice", line.VariantUnitPriceCents ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$quoted", line.QuotedUnitPriceCents);
                command.Parameters.AddWithValue("$total", line.LineTotalCents);
                command.Parameters.AddWithValue("$display", line.DisplayOrder);
                command.Parameters.AddWithValue("$note", Db(line.LineNote));
                command.ExecuteNonQuery();
            }

            foreach (CafeBookingModifierSelectionSnapshot m in line.ModifierSelections)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_booking_revision_modifier_selections(
                        revision_id,booking_line_id,group_id,group_name_snapshot,
                        option_id,option_name_snapshot,price_delta_cents,display_order)
                    VALUES(
                        $revision,$line,$group,$groupName,
                        $option,$optionName,$price,$display);
                    """;
                command.Parameters.AddWithValue("$revision", r.RevisionId.ToString("D"));
                command.Parameters.AddWithValue("$line", line.BookingLineId.ToString("D"));
                command.Parameters.AddWithValue("$group", m.GroupId);
                command.Parameters.AddWithValue("$groupName", m.GroupNameSnapshot);
                command.Parameters.AddWithValue("$option", m.OptionId);
                command.Parameters.AddWithValue("$optionName", m.OptionNameSnapshot);
                command.Parameters.AddWithValue("$price", m.PriceDeltaCents);
                command.Parameters.AddWithValue("$display", m.DisplayOrder);
                command.ExecuteNonQuery();
            }
        }
    }

    private static void InsertOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid bookingId,
        Guid deviceId,
        Guid storeId,
        string operationType,
        string payloadHash,
        int expectedVersion,
        int resultingVersion,
        Guid? resultRevisionId,
        string resultStatus,
        string resultFulfillmentState,
        Guid actor,
        DateTimeOffset at)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_booking_operations(
                operation_id,booking_id,device_id,store_id,operation_type,payload_hash,
                expected_version,resulting_version,result_revision_id,result_status,
                result_fulfillment_state,actor_user_id,occurred_at_utc)
            VALUES(
                $operation,$booking,$device,$store,$type,$hash,
                $expected,$resulting,$revision,$status,
                $fulfillment,$actor,$at);
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$type", operationType);
        command.Parameters.AddWithValue("$hash", payloadHash);
        command.Parameters.AddWithValue("$expected", expectedVersion);
        command.Parameters.AddWithValue("$resulting", resultingVersion);
        command.Parameters.AddWithValue("$revision", resultRevisionId.HasValue ? resultRevisionId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$status", resultStatus);
        command.Parameters.AddWithValue("$fulfillment", resultFulfillmentState);
        command.Parameters.AddWithValue("$actor", actor.ToString("D"));
        command.Parameters.AddWithValue("$at", Utc(at));
        command.ExecuteNonQuery();
    }

    private static void InsertEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid bookingId,
        int expectedVersion,
        int resultingVersion,
        string action,
        string? fromStatus,
        string toStatus,
        string? fromFulfillment,
        string toFulfillment,
        Guid? revisionId,
        Guid actor,
        DateTimeOffset at,
        string? reason,
        string contentHash,
        Guid? orderId = null,
        Guid? saleId = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_booking_events(
                event_id,operation_id,booking_id,expected_version,resulting_version,
                action,from_status,to_status,from_fulfillment_state,to_fulfillment_state,
                booking_revision_id,order_id,sale_id,actor_user_id,occurred_at_utc,reason,content_hash)
            VALUES(
                $event,$operation,$booking,$expected,$resulting,
                $action,$from,$to,$fromFulfillment,$toFulfillment,
                $revision,$order,$sale,$actor,$at,$reason,$hash);
            """;
        command.Parameters.AddWithValue("$event", DeterministicGuid("salryn-c9-booking-event-v1", operationId.ToString("D")).ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        command.Parameters.AddWithValue("$expected", expectedVersion);
        command.Parameters.AddWithValue("$resulting", resultingVersion);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$from", Db(fromStatus));
        command.Parameters.AddWithValue("$to", toStatus);
        command.Parameters.AddWithValue("$fromFulfillment", Db(fromFulfillment));
        command.Parameters.AddWithValue("$toFulfillment", toFulfillment);
        command.Parameters.AddWithValue("$revision", revisionId.HasValue ? revisionId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$order", orderId.HasValue ? orderId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$sale", saleId.HasValue ? saleId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$actor", actor.ToString("D"));
        command.Parameters.AddWithValue("$at", Utc(at));
        command.Parameters.AddWithValue("$reason", Db(reason));
        command.Parameters.AddWithValue("$hash", contentHash);
        command.ExecuteNonQuery();
    }

    private static void InsertDepositFact(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid factId,
        Guid operationId,
        Guid bookingId,
        Guid storeId,
        Guid revisionId,
        string factType,
        Guid actor,
        long amount,
        DateTimeOffset at,
        string reference,
        Guid cashierSessionId,
        Guid cashMovementId,
        string payloadHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_booking_deposit_facts(
                deposit_fact_id,operation_id,booking_id,store_id,booking_revision_id,
                fact_type,actor_user_id,amount_cents,occurred_at_utc,reference,
                cashier_session_id,cash_movement_id,sale_id,payload_hash)
            VALUES(
                $fact,$operation,$booking,$store,$revision,
                $type,$actor,$amount,$at,$reference,
                $session,$movement,NULL,$hash);
            """;
        command.Parameters.AddWithValue("$fact", factId.ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$type", factType);
        command.Parameters.AddWithValue("$actor", actor.ToString("D"));
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$at", Utc(at));
        command.Parameters.AddWithValue("$reference", reference);
        command.Parameters.AddWithValue("$session", cashierSessionId.ToString("D"));
        command.Parameters.AddWithValue("$movement", cashMovementId.ToString("D"));
        command.Parameters.AddWithValue("$hash", payloadHash);
        command.ExecuteNonQuery();
    }

    private void Enqueue(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deviceId,
        Guid storeId,
        Guid actor,
        Guid operationId,
        Guid bookingId,
        long? baseVersion,
        string operationType,
        DateTimeOffset at,
        string json)
    {
        _outbox.Enqueue(
            connection, transaction,
            new SyncEnqueueRequest(
                deviceId, storeId, actor, operationType,
                CafeBookingOperationContract.SchemaVersion,
                bookingId, baseVersion, at, json, operationId));
    }

    private static BookingRow RequireBooking(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId,
        Guid storeId,
        int expectedVersion)
    {
        BookingRow? row = ReadBooking(connection, transaction, bookingId);
        if (row is null)
            throw new BookingValidationException("CafeBooking.BookingMissing", "Booking was not found.");
        if (row.StoreId != storeId || row.CurrentVersion != expectedVersion)
            throw new BookingValidationException(
                "CafeBooking.ExpectedVersionConflict",
                "Booking store/version authority changed; last-write-wins is forbidden.");
        return row;
    }

    private static BookingRow RequireBookingAnyVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId,
        Guid storeId)
    {
        BookingRow? row = ReadBooking(connection, transaction, bookingId);
        if (row is null)
            throw new BookingValidationException("CafeBooking.BookingMissing", "Booking was not found.");
        if (row.StoreId != storeId)
            throw new BookingValidationException("CafeBooking.StoreMismatch", "Booking belongs to another Store.");
        return row;
    }

    private static BookingRow? ReadBooking(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT booking_id,store_id,current_version,current_revision_id,status,
                   fulfillment_state,created_by_user_id,created_at_utc,
                   updated_by_user_id,updated_at_utc,cancelled_by_user_id,
                   cancelled_at_utc,cancellation_reason
            FROM cafe_bookings
            WHERE booking_id=$booking
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new BookingRow(
            Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
            reader.GetInt32(2), Guid.Parse(reader.GetString(3)),
            reader.GetString(4), reader.GetString(5), Guid.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            Guid.Parse(reader.GetString(8)),
            DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
            reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)),
            reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    private static CafeBookingRevisionSnapshot ReadRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision_id,booking_id,revision_number,booking_version,previous_revision_id,
                   customer_id,customer_name_snapshot,customer_phone_snapshot,customer_address_snapshot,
                   booking_mode,event_name,event_reference,event_start_utc,event_end_utc,location,
                   setup_at_utc,service_window_start_utc,service_window_end_utc,headcount,package_label,
                   menu_publication_revision_id,quoted_total_cents,deposit_requirement_cents,
                   special_instructions,notes,fulfillment_service_mode,actor_user_id,created_at_utc,content_hash
            FROM cafe_booking_revisions
            WHERE revision_id=$revision
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            throw new BookingValidationException(
                "CafeBooking.RevisionMissing",
                "Current immutable Booking revision is missing.");

        CafeBookingRevisionSnapshot header = new(
            Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
            reader.GetInt32(2), reader.GetInt32(3),
            reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
            Guid.Parse(reader.GetString(5)), reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9), reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture),
            reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture),
            reader.IsDBNull(16) ? null : DateTimeOffset.Parse(reader.GetString(16), CultureInfo.InvariantCulture),
            reader.IsDBNull(17) ? null : DateTimeOffset.Parse(reader.GetString(17), CultureInfo.InvariantCulture),
            reader.IsDBNull(18) ? null : reader.GetInt32(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.GetString(20), reader.GetInt64(21), reader.GetInt64(22),
            reader.IsDBNull(23) ? null : reader.GetString(23),
            reader.IsDBNull(24) ? null : reader.GetString(24),
            reader.GetString(25), Guid.Parse(reader.GetString(26)),
            DateTimeOffset.Parse(reader.GetString(27), CultureInfo.InvariantCulture),
            reader.GetString(28),
            Array.Empty<CafeBookingLineSnapshot>());
        reader.Close();

        return header with { Lines = ReadRevisionLines(connection, transaction, revisionId) };
    }

    private static IReadOnlyList<CafeBookingLineSnapshot> ReadRevisionLines(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId)
    {
        List<CafeBookingLineSnapshot> result = new();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT booking_line_id,product_id,product_name_snapshot,unit_name_snapshot,
                   quantity_x1000,base_unit_price_cents,variant_id,variant_name_snapshot,
                   variant_unit_price_cents,quoted_unit_price_cents,line_total_cents,
                   display_order,line_note
            FROM cafe_booking_revision_lines
            WHERE revision_id=$revision
            ORDER BY display_order,booking_line_id;
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        List<RawLine> raw = new();
        while (reader.Read())
            raw.Add(new RawLine(
                Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8), reader.GetInt64(9),
                reader.GetInt64(10), reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        reader.Close();

        foreach (RawLine line in raw)
            result.Add(new CafeBookingLineSnapshot(
                line.BookingLineId, line.ProductId, line.ProductNameSnapshot,
                line.UnitNameSnapshot, line.QuantityX1000, line.BaseUnitPriceCents,
                line.VariantId, line.VariantNameSnapshot, line.VariantUnitPriceCents,
                line.QuotedUnitPriceCents, line.LineTotalCents, line.DisplayOrder,
                line.LineNote, ReadModifiers(connection, transaction, revisionId, line.BookingLineId)));
        return result;
    }

    private static IReadOnlyList<CafeBookingModifierSelectionSnapshot> ReadModifiers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId,
        Guid lineId)
    {
        List<CafeBookingModifierSelectionSnapshot> result = new();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT group_id,group_name_snapshot,option_id,option_name_snapshot,
                   price_delta_cents,display_order
            FROM cafe_booking_revision_modifier_selections
            WHERE revision_id=$revision AND booking_line_id=$line
            ORDER BY display_order,group_id,option_id;
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$line", lineId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new CafeBookingModifierSelectionSnapshot(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt64(4), reader.GetInt32(5)));
        return result;
    }

    private static CafeBookingSummary BuildSummary(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookingRow b,
        CafeBookingRevisionSnapshot r)
    {
        DepositTotals totals = ReadDepositTotals(connection, transaction, b.BookingId);
        Guid? orderId = ScalarGuid(
            connection, transaction,
            "SELECT order_id FROM cafe_booking_order_links WHERE booking_id=$booking LIMIT 1;",
            b.BookingId);
        Guid? saleId = ScalarGuid(
            connection, transaction,
            "SELECT sale_id FROM cafe_booking_sale_links WHERE booking_id=$booking LIMIT 1;",
            b.BookingId);

        long outstanding = checked(r.QuotedTotalCents - totals.PreFulfillmentCredit);
        if (outstanding < 0)
            throw new BookingValidationException(
                "CafeBooking.DepositExceedsQuote",
                "Derived retained deposit exceeds current Booking quote.");

        return new CafeBookingSummary(
            b.BookingId, b.StoreId, b.CurrentVersion, b.CurrentRevisionId,
            b.Status, b.FulfillmentState, b.CreatedByUserId, b.CreatedAtUtc,
            b.UpdatedByUserId, b.UpdatedAtUtc, b.CancelledByUserId,
            b.CancelledAtUtc, b.CancellationReason,
            r.CustomerId, r.CustomerNameSnapshot, r.BookingMode,
            r.EventStartUtc, r.EventEndUtc, r.Location, r.PackageLabel,
            r.QuotedTotalCents, r.DepositRequirementCents,
            totals.Received, totals.Refunded, totals.Applied,
            totals.PreFulfillmentCredit, outstanding, orderId, saleId);
    }

    private static DepositTotals ReadDepositTotals(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN fact_type='Received' THEN amount_cents ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN fact_type='Refunded' THEN amount_cents ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN fact_type='Applied' THEN amount_cents ELSE 0 END),0)
            FROM cafe_booking_deposit_facts
            WHERE booking_id=$booking;
            """;
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        long received = reader.GetInt64(0);
        long refunded = reader.GetInt64(1);
        long applied = reader.GetInt64(2);
        long credit = checked(received - refunded - applied);
        if (credit < 0)
            throw new BookingValidationException(
                "CafeBooking.DepositLiabilityInvalid",
                "Booking deposit liability cannot be negative.");
        return new DepositTotals(received, refunded, applied, credit);
    }

    private static ReceivedDeposit? ReadReceivedDeposit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid factId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT booking_id,store_id,amount_cents
            FROM cafe_booking_deposit_facts
            WHERE deposit_fact_id=$fact AND fact_type='Received'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$fact", factId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new ReceivedDeposit(
                Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                reader.GetInt64(2))
            : null;
    }

    private static long ReadAllocatedTotal(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid receivedFactId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(amount_cents),0)
            FROM cafe_booking_deposit_allocations
            WHERE received_fact_id=$received;
            """;
        command.Parameters.AddWithValue("$received", receivedFactId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static OperationRow? ReadOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT booking_id,device_id,store_id,operation_type,payload_hash,
                   expected_version,resulting_version,result_revision_id,
                   result_status,result_fulfillment_state,actor_user_id,occurred_at_utc
            FROM cafe_booking_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new OperationRow(
            operationId, Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)), reader.GetString(3), reader.GetString(4),
            reader.GetInt32(5), reader.GetInt32(6),
            reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
            reader.GetString(8), reader.GetString(9),
            Guid.Parse(reader.GetString(10)),
            DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture));
    }

    private static int ReadNextRevisionNumber(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(MAX(revision_number),0)+1
            FROM cafe_booking_revisions
            WHERE booking_id=$booking;
            """;
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        return Convert.ToInt32(command.ExecuteScalar() ?? 1);
    }

    private static Dictionary<string,HashSet<string>> ReadGroupLinks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string ownerColumn,
        string revisionId)
    {
        Dictionary<string,HashSet<string>> result = new(StringComparer.Ordinal);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT " + ownerColumn + ",group_id FROM " + table +
            " WHERE revision_id=$revision ORDER BY display_order,group_id;";
        command.Parameters.AddWithValue("$revision", revisionId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string owner = reader.GetString(0);
            if (!result.TryGetValue(owner, out HashSet<string>? groups))
            {
                groups = new HashSet<string>(StringComparer.Ordinal);
                result[owner] = groups;
            }
            groups.Add(reader.GetString(1));
        }
        return result;
    }

    private static string? ValidateCreate(CafeBookingCreateMutation m)
    {
        if (m is null || !ValidIdentity(
                m.OperationId, m.DeviceId, m.BookingId, m.StoreId, m.ActorUserId) ||
            m.RevisionId == Guid.Empty || m.Configuration is null)
            return "Create requires Operation/Booking/Revision/Device/Store/Actor identities and configuration.";
        return null;
    }

    private static string? ValidateRevise(CafeBookingReviseMutation m)
    {
        if (m is null || !ValidIdentity(
                m.OperationId, m.DeviceId, m.BookingId, m.StoreId, m.ActorUserId) ||
            m.RevisionId == Guid.Empty || m.ExpectedVersion <= 0 || m.Configuration is null)
            return "Revision requires stable identities, positive ExpectedVersion and configuration.";
        return null;
    }

    private static bool ValidIdentity(
        Guid operationId,
        Guid deviceId,
        Guid bookingId,
        Guid storeId,
        Guid actorId) =>
        operationId != Guid.Empty && deviceId != Guid.Empty &&
        bookingId != Guid.Empty && storeId != Guid.Empty && actorId != Guid.Empty;

    private static bool ValidDepositIdentity(
        Guid operationId, Guid factId, Guid movementId, Guid deviceId,
        Guid storeId, Guid bookingId, Guid revisionId, Guid actorId, Guid sessionId) =>
        ValidIdentity(operationId, deviceId, bookingId, storeId, actorId) &&
        factId != Guid.Empty && movementId != Guid.Empty &&
        revisionId != Guid.Empty && sessionId != Guid.Empty;

    private static bool BookingExists(SqliteConnection c, SqliteTransaction t, Guid id) =>
        Exists(c, t, "cafe_bookings", "booking_id", id);
    private static bool RevisionExists(SqliteConnection c, SqliteTransaction t, Guid id) =>
        Exists(c, t, "cafe_booking_revisions", "revision_id", id);
    private static bool DepositFactExists(SqliteConnection c, SqliteTransaction t, Guid id) =>
        Exists(c, t, "cafe_booking_deposit_facts", "deposit_fact_id", id);
    private static bool CashMovementExists(SqliteConnection c, SqliteTransaction t, Guid id) =>
        Exists(c, t, "cash_movements", "id", id);

    private static bool Exists(
        SqliteConnection c, SqliteTransaction t, string table, string column, Guid id)
    {
        using SqliteCommand command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = "SELECT COUNT(*) FROM " + table + " WHERE " + column + "=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private bool OutboxOperationExists(
        SqliteConnection c, SqliteTransaction t, Guid operationId) =>
        _outbox.Find(c, t, operationId) is not null;

    private static Guid? ScalarGuid(
        SqliteConnection c,
        SqliteTransaction t,
        string sql,
        Guid bookingId)
    {
        using SqliteCommand command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : Guid.Parse(Convert.ToString(value)!);
    }

    private static object Db(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? Clean(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");

    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static Guid DeterministicGuid(string scope, string identity)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope + "|" + identity));
        byte[] bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static void EnsureCanonical<T>(CafeBookingCanonicalParseResult<T> result)
    {
        if (!result.IsValid)
            throw new BookingValidationException(
                result.ErrorCode ?? "CafeBooking.CanonicalInvalid",
                result.ErrorMessage ?? "C9 canonical payload validation failed.");
    }

    private static bool ExpectedException(Exception ex) =>
        ex is SqliteException or InvalidOperationException or
        FormatException or OverflowException or ArgumentException;

    private static Result<T> Fail<T>(Error error) => Result<T>.Fail(error);

    private static Result<T> Rollback<T>(
        SqliteTransaction transaction,
        Error error)
    {
        transaction.Rollback();
        return Result<T>.Fail(error);
    }

    private sealed record CustomerSnapshot(
        Guid CustomerId,
        string Name,
        string? Phone,
        string? Address);

    private sealed record MenuItem(
        Guid ProductId,
        string Name,
        string UnitName,
        long PriceCents,
        bool IsUnavailable);

    private sealed record MenuVariant(
        string Id,
        Guid ProductId,
        string Name,
        long PriceCents,
        bool IsUnavailable);

    private sealed record MenuGroup(
        string Id,
        string Name,
        bool IsRequired,
        int MinSelections,
        int MaxSelections,
        int DisplayOrder);

    private sealed record MenuOption(
        string Id,
        string GroupId,
        string Name,
        long PriceDeltaCents,
        int DisplayOrder);

    private sealed record MenuAuthority(
        Dictionary<Guid,MenuItem> Items,
        Dictionary<string,MenuVariant> Variants,
        Dictionary<string,MenuGroup> Groups,
        Dictionary<string,MenuOption> Options,
        Dictionary<string,HashSet<string>> ItemGroupLinks,
        Dictionary<string,HashSet<string>> VariantGroupLinks);

    private sealed record BookingRow(
        Guid BookingId,
        Guid StoreId,
        int CurrentVersion,
        Guid CurrentRevisionId,
        string Status,
        string FulfillmentState,
        Guid CreatedByUserId,
        DateTimeOffset CreatedAtUtc,
        Guid UpdatedByUserId,
        DateTimeOffset UpdatedAtUtc,
        Guid? CancelledByUserId,
        DateTimeOffset? CancelledAtUtc,
        string? CancellationReason);

    private sealed record RawLine(
        Guid BookingLineId,
        Guid ProductId,
        string ProductNameSnapshot,
        string UnitNameSnapshot,
        long QuantityX1000,
        long BaseUnitPriceCents,
        string? VariantId,
        string? VariantNameSnapshot,
        long? VariantUnitPriceCents,
        long QuotedUnitPriceCents,
        long LineTotalCents,
        int DisplayOrder,
        string? LineNote);

    private sealed record OperationRow(
        Guid OperationId,
        Guid BookingId,
        Guid DeviceId,
        Guid StoreId,
        string OperationType,
        string PayloadHash,
        int ExpectedVersion,
        int ResultingVersion,
        Guid? ResultRevisionId,
        string ResultStatus,
        string ResultFulfillmentState,
        Guid ActorUserId,
        DateTimeOffset OccurredAtUtc);

    private sealed record DepositTotals(
        long Received,
        long Refunded,
        long Applied,
        long PreFulfillmentCredit);

    private sealed record ReceivedDeposit(
        Guid BookingId,
        Guid StoreId,
        long AmountCents);

    private sealed class BookingValidationException : Exception
    {
        public BookingValidationException(string code, string message) : base(message)
        {
            Code = code;
        }
        public string Code { get; }
    }
}
