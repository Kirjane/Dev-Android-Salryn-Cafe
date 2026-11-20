using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteCafePrintService :
    SqliteRepositoryBase,
    ICafePrintService
{
    private const string SaleReceiptSource = "SaleReceipt";
    private const string CafeOrderRevisionSource = "CafeOrderRevision";

    public SqliteCafePrintService(
        SqliteConnectionFactory connectionFactory)
        : base(
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory)))
    {
    }

    public Result<CafePrinterStationMutationResult> PublishStation(
        CafePrinterStationMutation mutation)
    {
        string? validation = ValidateStationMutation(mutation);
        if (validation is not null)
        {
            return Result<CafePrinterStationMutationResult>.Fail(
                Error.Validation(
                    "CafePrint.StationInvalid",
                    validation));
        }

        string name = mutation.Name.Trim();
        string? routeKey = NormalizeOptional(mutation.RouteKey);
        string contentHash = Hash(
            string.Join(
                "|",
                mutation.StoreId.ToString("D"),
                name,
                Convert.ToInt32(mutation.StationKind)
                    .ToString(CultureInfo.InvariantCulture),
                routeKey ?? "<null>",
                mutation.IsActive ? "1" : "0"));

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.PrintingStationManage);

            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafePrinterStationMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            CafePrinterStation? existing =
                ReadStation(
                    connection,
                    transaction,
                    mutation.StationId);

            if (existing is null)
            {
                if (mutation.ExpectedVersion != 0)
                {
                    transaction.Rollback();
                    return Result<CafePrinterStationMutationResult>.Fail(
                        Error.Conflict(
                            "CafePrint.StationVersionConflict",
                            "New printer station requires ExpectedVersion 0."));
                }

                CafePrinterStation created = new(
                    mutation.StationId,
                    mutation.StoreId,
                    name,
                    mutation.StationKind,
                    routeKey,
                    mutation.IsActive,
                    1,
                    contentHash,
                    mutation.ActorUserId,
                    mutation.OccurredAtUtc.ToUniversalTime(),
                    null,
                    null);

                InsertStation(
                    connection,
                    transaction,
                    created);

                transaction.Commit();

                return Result<CafePrinterStationMutationResult>.Success(
                    new(created, false));
            }

            if (existing.StoreId != mutation.StoreId)
            {
                transaction.Rollback();
                return Result<CafePrinterStationMutationResult>.Fail(
                    Error.Conflict(
                        "CafePrint.StationStoreConflict",
                        "Printer station belongs to a different StoreId."));
            }

            if (mutation.ExpectedVersion == 0 &&
                existing.Version == 1 &&
                string.Equals(
                    existing.ContentHash,
                    contentHash,
                    StringComparison.Ordinal))
            {
                transaction.Commit();
                return Result<CafePrinterStationMutationResult>.Success(
                    new(existing, true));
            }

            if (existing.Version == mutation.ExpectedVersion + 1 &&
                string.Equals(
                    existing.ContentHash,
                    contentHash,
                    StringComparison.Ordinal))
            {
                transaction.Commit();
                return Result<CafePrinterStationMutationResult>.Success(
                    new(existing, true));
            }

            if (existing.Version != mutation.ExpectedVersion)
            {
                transaction.Rollback();
                return Result<CafePrinterStationMutationResult>.Fail(
                    Error.Conflict(
                        "CafePrint.StationVersionConflict",
                        "Printer station ExpectedVersion does not match retained configuration."));
            }

            CafePrinterStation updated = existing with
            {
                Name = name,
                StationKind = mutation.StationKind,
                RouteKey = routeKey,
                IsActive = mutation.IsActive,
                Version = checked(existing.Version + 1),
                ContentHash = contentHash,
                UpdatedByUserId = mutation.ActorUserId,
                UpdatedAtUtc = mutation.OccurredAtUtc.ToUniversalTime()
            };

            UpdateStation(
                connection,
                transaction,
                updated);

            transaction.Commit();

            return Result<CafePrinterStationMutationResult>.Success(
                new(updated, false));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafePrinterStationMutationResult>.Fail(
                Error.Conflict(
                    "CafePrint.StationConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Unexpected<CafePrinterStationMutationResult>(
                "CafePrint.StationPublishFailed",
                ex);
        }
    }

    public Result<IReadOnlyList<CafePrinterStation>> ListStations(
        Guid storeId,
        Guid actorUserId)
    {
        if (storeId == Guid.Empty || actorUserId == Guid.Empty)
        {
            return Result<IReadOnlyList<CafePrinterStation>>.Fail(
                Error.Validation(
                    "CafePrint.StationListIdentityInvalid",
                    "StoreId and ActorUserId are required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                storeId,
                actorUserId,
                SalrynPermissions.PrintingView);

            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<IReadOnlyList<CafePrinterStation>>.Fail(
                    authority.ErrorDetail);
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT station_id,store_id,name,station_kind,route_key,
                       is_active,version,content_hash,
                       created_by_user_id,created_at_utc,
                       updated_by_user_id,updated_at_utc
                FROM cafe_printer_stations
                WHERE store_id=$store
                ORDER BY station_kind,name,station_id;
                """;
            command.Parameters.AddWithValue(
                "$store",
                storeId.ToString("D"));

            List<CafePrinterStation> result = new();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(MapStation(reader));

            transaction.Commit();

            return Result<IReadOnlyList<CafePrinterStation>>.Success(
                result);
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Unexpected<IReadOnlyList<CafePrinterStation>>(
                "CafePrint.StationListFailed",
                ex);
        }
    }

    public Task<Result<CafePrintJobMutationResult>> CreateReceiptJobAsync(
        CafeReceiptPrintJobMutation mutation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result<CafePrintJobMutationResult> result =
            CreateReceiptJob(mutation);

        return Task.FromResult(result);
    }

    public Result<CafePrintJobMutationResult> CreateKitchenTicketJob(
        CafeKitchenPrintJobMutation mutation)
    {
        string? validation = ValidateKitchenMutation(mutation);
        if (validation is not null)
        {
            return Result<CafePrintJobMutationResult>.Fail(
                Error.Validation(
                    "CafePrint.KitchenJobInvalid",
                    validation));
        }

        string permission =
            mutation.CopyKind == CafePrintCopyKind.Original
                ? SalrynPermissions.PrintingPrint
                : SalrynPermissions.PrintingReprint;

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                permission);

            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            Result<CafePrintJobMutationResult>? replay =
                TryReplayKitchen(
                    connection,
                    transaction,
                    mutation);

            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (JobIdExists(
                    connection,
                    transaction,
                    mutation.JobId))
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    Error.Conflict(
                        "CafePrint.JobIdConflict",
                        "Print JobId already exists under a different operation."));
            }

            CafePrinterStation? station =
                ReadStation(
                    connection,
                    transaction,
                    mutation.StationId);

            Result stationValidation =
                ValidateStationForDocument(
                    station,
                    mutation.StoreId,
                    CafePrintDocumentKind.KitchenTicket);

            if (stationValidation.IsFailure)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    stationValidation.ErrorDetail);
            }

            Result parentValidation =
                ValidateParentJob(
                    connection,
                    transaction,
                    mutation.ParentJobId,
                    CafePrintDocumentKind.KitchenTicket,
                    CafeOrderRevisionSource,
                    mutation.OrderRevisionId,
                    mutation.CopyKind);

            if (parentValidation.IsFailure)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    parentValidation.ErrorDetail);
            }

            string snapshotText;
            string snapshotHash;
            string sourceVersion;

            if (mutation.CopyKind != CafePrintCopyKind.Original &&
                mutation.ParentJobId.HasValue)
            {
                CafePrintJob? parentJob =
                    ReadJob(
                        connection,
                        transaction,
                        mutation.ParentJobId.Value);

                if (parentJob is null)
                {
                    transaction.Rollback();
                    return Result<CafePrintJobMutationResult>.Fail(
                        Error.Conflict(
                            "CafePrint.ParentJobConflict",
                            "Retained parent print job disappeared before reprint creation."));
                }

                snapshotText = parentJob.SnapshotText;
                snapshotHash = parentJob.SnapshotHash;
                sourceVersion =
                    parentJob.SourceVersion ??
                    mutation.OrderId.ToString("D") +
                    "/" +
                    mutation.OrderRevisionId.ToString("D");
            }
            else
            {
                Result<KitchenSnapshot> snapshot =
                    BuildKitchenSnapshot(
                        connection,
                        transaction,
                        mutation.StoreId,
                        mutation.OrderId,
                        mutation.OrderRevisionId);

                if (snapshot.IsFailure)
                {
                    transaction.Rollback();
                    return Result<CafePrintJobMutationResult>.Fail(
                        snapshot.ErrorDetail);
                }

                snapshotText = snapshot.Value.Text;
                snapshotHash = Hash(snapshotText);
                sourceVersion =
                    mutation.OrderId.ToString("D") +
                    "/" +
                    snapshot.Value.ContentHash;
            }

            string payloadHash = Hash(
                CanonicalJobRequest(
                    mutation.OperationId,
                    mutation.JobId,
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    mutation.StationId,
                    CafePrintDocumentKind.KitchenTicket,
                    CafeOrderRevisionSource,
                    mutation.OrderRevisionId,
                    sourceVersion,
                    snapshotHash,
                    mutation.CopyKind,
                    mutation.ParentJobId,
                    mutation.OccurredAtUtc));

            CafePrintJob job = new(
                mutation.JobId,
                mutation.OperationId,
                mutation.StoreId,
                mutation.DeviceId,
                mutation.ActorUserId,
                mutation.StationId,
                CafePrintDocumentKind.KitchenTicket,
                CafeOrderRevisionSource,
                mutation.OrderRevisionId,
                sourceVersion,
                snapshotText,
                snapshotHash,
                mutation.CopyKind,
                mutation.ParentJobId,
                payloadHash,
                mutation.OccurredAtUtc.ToUniversalTime());

            InsertJob(
                connection,
                transaction,
                job);

            transaction.Commit();

            return Result<CafePrintJobMutationResult>.Success(
                new(job, false));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafePrintJobMutationResult>.Fail(
                Error.Conflict(
                    "CafePrint.KitchenJobConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Unexpected<CafePrintJobMutationResult>(
                "CafePrint.KitchenJobFailed",
                ex);
        }
    }

    public async Task<Result<CafePrintAttemptResult>> ExecuteAttemptAsync(
        Guid jobId,
        Guid deviceId,
        Guid actorUserId,
        IReceiptPrinterAdapter printerAdapter,
        CancellationToken cancellationToken = default)
    {
        if (jobId == Guid.Empty ||
            deviceId == Guid.Empty ||
            actorUserId == Guid.Empty ||
            printerAdapter is null)
        {
            return Result<CafePrintAttemptResult>.Fail(
                Error.Validation(
                    "CafePrint.AttemptInvalid",
                    "JobId, DeviceId, ActorUserId and printer adapter are required."));
        }

        CafePrintJob job;

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            CafePrintJob? found =
                ReadJob(
                    connection,
                    transaction,
                    jobId);

            if (found is null)
            {
                transaction.Rollback();
                return Result<CafePrintAttemptResult>.Fail(
                    Error.NotFound(
                        "CafePrint.JobNotFound",
                        "Print job was not found."));
            }

            string permission =
                found.CopyKind == CafePrintCopyKind.Original
                    ? SalrynPermissions.PrintingPrint
                    : SalrynPermissions.PrintingReprint;

            Result authority = ValidateAuthority(
                connection,
                transaction,
                found.StoreId,
                actorUserId,
                permission);

            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafePrintAttemptResult>.Fail(
                    authority.ErrorDetail);
            }

            CafePrinterStation? station =
                ReadStation(
                    connection,
                    transaction,
                    found.StationId);

            if (station is null ||
                station.StoreId != found.StoreId ||
                !station.IsActive)
            {
                transaction.Rollback();
                return Result<CafePrintAttemptResult>.Fail(
                    Error.Conflict(
                        "CafePrint.StationUnavailable",
                        "Print job station is missing, inactive, or outside store authority."));
            }

            transaction.Commit();
            job = found;
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Unexpected<CafePrintAttemptResult>(
                "CafePrint.AttemptPrepareFailed",
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset requestedAt = DateTimeOffset.UtcNow;
        ReceiptPrinterAdapterResult adapterResult;

        try
        {
            adapterResult = await printerAdapter
                .PrintAsync(
                    BuildAdapterJobName(job),
                    job.SnapshotText,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            adapterResult = ReceiptPrinterAdapterResult.Failed(
                "Printer adapter threw while submitting the immutable C7 print job.",
                ex.GetType().Name + ": " + ex.Message);
        }

        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            CafePrintJob? retained =
                ReadJob(
                    connection,
                    transaction,
                    job.JobId);

            if (retained is null ||
                !string.Equals(
                    retained.PayloadHash,
                    job.PayloadHash,
                    StringComparison.Ordinal))
            {
                transaction.Rollback();
                return Result<CafePrintAttemptResult>.Fail(
                    Error.Conflict(
                        "CafePrint.JobChanged",
                        "Immutable print job could not be revalidated after adapter submission."));
            }

            int attemptNumber =
                ReadNextAttemptNumber(
                    connection,
                    transaction,
                    job.JobId);

            CafePrintAttemptStatus status =
                MapAttemptStatus(adapterResult);

            string mode =
                ResolveAdapterMode(printerAdapter);

            CafePrintAttempt attempt = new(
                Guid.NewGuid(),
                job.JobId,
                deviceId,
                attemptNumber,
                printerAdapter.AdapterName,
                mode,
                status,
                status == CafePrintAttemptStatus.Succeeded
                    ? "CafePrint.Submitted"
                    : status == CafePrintAttemptStatus.DisabledUnavailable
                        ? "CafePrint.DisabledUnavailable"
                        : "CafePrint.Failed",
                adapterResult.Message,
                adapterResult.ErrorMessage,
                requestedAt,
                completedAt);

            InsertAttempt(
                connection,
                transaction,
                attempt);

            transaction.Commit();

            return Result<CafePrintAttemptResult>.Success(
                new(attempt, job));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafePrintAttemptResult>.Fail(
                Error.Conflict(
                    "CafePrint.AttemptConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException)
        {
            return Unexpected<CafePrintAttemptResult>(
                "CafePrint.AttemptPersistFailed",
                ex);
        }
    }

    public Result<CafePrintJob?> FindJob(
        Guid jobId,
        Guid actorUserId)
    {
        if (jobId == Guid.Empty || actorUserId == Guid.Empty)
        {
            return Result<CafePrintJob?>.Fail(
                Error.Validation(
                    "CafePrint.JobReadIdentityInvalid",
                    "JobId and ActorUserId are required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            CafePrintJob? job =
                ReadJob(
                    connection,
                    transaction,
                    jobId);

            if (job is null)
            {
                transaction.Commit();
                return Result<CafePrintJob?>.Success(null);
            }

            Result authority = ValidateAuthority(
                connection,
                transaction,
                job.StoreId,
                actorUserId,
                SalrynPermissions.PrintingView);

            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafePrintJob?>.Fail(
                    authority.ErrorDetail);
            }

            transaction.Commit();

            return Result<CafePrintJob?>.Success(job);
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Unexpected<CafePrintJob?>(
                "CafePrint.JobReadFailed",
                ex);
        }
    }

    private Result<CafePrintJobMutationResult> CreateReceiptJob(
        CafeReceiptPrintJobMutation mutation)
    {
        string? validation = ValidateReceiptMutation(mutation);
        if (validation is not null)
        {
            return Result<CafePrintJobMutationResult>.Fail(
                Error.Validation(
                    "CafePrint.ReceiptJobInvalid",
                    validation));
        }

        string permission =
            mutation.CopyKind == CafePrintCopyKind.Original
                ? SalrynPermissions.PrintingPrint
                : SalrynPermissions.PrintingReprint;

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                permission);

            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            Result<CafePrintJobMutationResult>? replay =
                TryReplayReceipt(
                    connection,
                    transaction,
                    mutation);

            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (JobIdExists(
                    connection,
                    transaction,
                    mutation.JobId))
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    Error.Conflict(
                        "CafePrint.JobIdConflict",
                        "Print JobId already exists under a different operation."));
            }

            CafePrinterStation? station =
                ReadStation(
                    connection,
                    transaction,
                    mutation.StationId);

            Result stationValidation =
                ValidateStationForDocument(
                    station,
                    mutation.StoreId,
                    CafePrintDocumentKind.CustomerReceipt);

            if (stationValidation.IsFailure)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    stationValidation.ErrorDetail);
            }

            SaleSource? sale =
                ReadSaleSource(
                    connection,
                    transaction,
                    mutation.SaleId);

            if (sale is null)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    Error.NotFound(
                        "CafePrint.SaleNotFound",
                        "Sale was not found for receipt print job."));
            }

            if (sale.Status == SaleStatus.Draft)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    Error.Conflict(
                        "CafePrint.SaleNotPrintable",
                        "Draft Sale cannot produce a C7 customer receipt print job."));
            }

            if (string.IsNullOrWhiteSpace(sale.ReceiptSnapshot))
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    Error.Conflict(
                        "CafePrint.ReceiptSnapshotMissing",
                        "Sale has no persisted receipt snapshot."));
            }

            if (mutation.CopyKind == CafePrintCopyKind.Original &&
                sale.Status == SaleStatus.Voided)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    Error.Conflict(
                        "CafePrint.VoidedSaleRequiresVoidedCopy",
                        "Voided Sale cannot create an Original print job; use VoidedReprint."));
            }

            if (mutation.CopyKind == CafePrintCopyKind.VoidedReprint &&
                sale.Status != SaleStatus.Voided)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    Error.Conflict(
                        "CafePrint.SaleNotVoided",
                        "VoidedReprint requires a voided Sale."));
            }

            if (mutation.CopyKind == CafePrintCopyKind.Reprint &&
                sale.Status == SaleStatus.Voided)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    Error.Conflict(
                        "CafePrint.VoidedSaleRequiresVoidedCopy",
                        "Reprint of a voided Sale must be classified as VoidedReprint."));
            }

            Result parentValidation =
                ValidateParentJob(
                    connection,
                    transaction,
                    mutation.ParentJobId,
                    CafePrintDocumentKind.CustomerReceipt,
                    SaleReceiptSource,
                    mutation.SaleId,
                    mutation.CopyKind);

            if (parentValidation.IsFailure)
            {
                transaction.Rollback();
                return Result<CafePrintJobMutationResult>.Fail(
                    parentValidation.ErrorDetail);
            }

            string snapshotText =
                mutation.CopyKind == CafePrintCopyKind.VoidedReprint
                    ? BuildVoidedReceiptCopy(sale)
                    : sale.ReceiptSnapshot!;

            string snapshotHash =
                Hash(snapshotText);

            string payloadHash = Hash(
                CanonicalJobRequest(
                    mutation.OperationId,
                    mutation.JobId,
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    mutation.StationId,
                    CafePrintDocumentKind.CustomerReceipt,
                    SaleReceiptSource,
                    mutation.SaleId,
                    sale.ReceiptNo,
                    snapshotHash,
                    mutation.CopyKind,
                    mutation.ParentJobId,
                    mutation.OccurredAtUtc));

            CafePrintJob job = new(
                mutation.JobId,
                mutation.OperationId,
                mutation.StoreId,
                mutation.DeviceId,
                mutation.ActorUserId,
                mutation.StationId,
                CafePrintDocumentKind.CustomerReceipt,
                SaleReceiptSource,
                mutation.SaleId,
                sale.ReceiptNo,
                snapshotText,
                snapshotHash,
                mutation.CopyKind,
                mutation.ParentJobId,
                payloadHash,
                mutation.OccurredAtUtc.ToUniversalTime());

            InsertJob(
                connection,
                transaction,
                job);

            transaction.Commit();

            return Result<CafePrintJobMutationResult>.Success(
                new(job, false));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafePrintJobMutationResult>.Fail(
                Error.Conflict(
                    "CafePrint.ReceiptJobConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Unexpected<CafePrintJobMutationResult>(
                "CafePrint.ReceiptJobFailed",
                ex);
        }
    }

    private static Result<CafePrintJobMutationResult>? TryReplayReceipt(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeReceiptPrintJobMutation mutation)
    {
        CafePrintJob? existing =
            ReadJobByOperation(
                connection,
                transaction,
                mutation.OperationId);

        if (existing is null)
            return null;

        string candidatePayloadHash =
            Hash(
                CanonicalJobRequest(
                    mutation.OperationId,
                    mutation.JobId,
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    mutation.StationId,
                    CafePrintDocumentKind.CustomerReceipt,
                    SaleReceiptSource,
                    mutation.SaleId,
                    existing.SourceVersion,
                    existing.SnapshotHash,
                    mutation.CopyKind,
                    mutation.ParentJobId,
                    mutation.OccurredAtUtc));

        bool exact =
            existing.JobId == mutation.JobId &&
            existing.DeviceId == mutation.DeviceId &&
            existing.StoreId == mutation.StoreId &&
            existing.ActorUserId == mutation.ActorUserId &&
            existing.StationId == mutation.StationId &&
            existing.DocumentKind == CafePrintDocumentKind.CustomerReceipt &&
            string.Equals(
                existing.SourceType,
                SaleReceiptSource,
                StringComparison.Ordinal) &&
            existing.SourceId == mutation.SaleId &&
            existing.CopyKind == mutation.CopyKind &&
            existing.ParentJobId == mutation.ParentJobId &&
            string.Equals(
                existing.PayloadHash,
                candidatePayloadHash,
                StringComparison.Ordinal);

        return exact
            ? Result<CafePrintJobMutationResult>.Success(
                new(existing, true))
            : Result<CafePrintJobMutationResult>.Fail(
                Error.Conflict(
                    "CafePrint.OperationPayloadConflict",
                    "OperationId already exists with different receipt print evidence."));
    }

    private static Result<CafePrintJobMutationResult>? TryReplayKitchen(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeKitchenPrintJobMutation mutation)
    {
        CafePrintJob? existing =
            ReadJobByOperation(
                connection,
                transaction,
                mutation.OperationId);

        if (existing is null)
            return null;

        string candidatePayloadHash =
            Hash(
                CanonicalJobRequest(
                    mutation.OperationId,
                    mutation.JobId,
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    mutation.StationId,
                    CafePrintDocumentKind.KitchenTicket,
                    CafeOrderRevisionSource,
                    mutation.OrderRevisionId,
                    existing.SourceVersion,
                    existing.SnapshotHash,
                    mutation.CopyKind,
                    mutation.ParentJobId,
                    mutation.OccurredAtUtc));

        bool exact =
            existing.JobId == mutation.JobId &&
            existing.DeviceId == mutation.DeviceId &&
            existing.StoreId == mutation.StoreId &&
            existing.ActorUserId == mutation.ActorUserId &&
            existing.StationId == mutation.StationId &&
            existing.DocumentKind == CafePrintDocumentKind.KitchenTicket &&
            string.Equals(
                existing.SourceType,
                CafeOrderRevisionSource,
                StringComparison.Ordinal) &&
            existing.SourceId == mutation.OrderRevisionId &&
            existing.CopyKind == mutation.CopyKind &&
            existing.ParentJobId == mutation.ParentJobId &&
            string.Equals(
                existing.PayloadHash,
                candidatePayloadHash,
                StringComparison.Ordinal);

        return exact
            ? Result<CafePrintJobMutationResult>.Success(
                new(existing, true))
            : Result<CafePrintJobMutationResult>.Fail(
                Error.Conflict(
                    "CafePrint.OperationPayloadConflict",
                    "OperationId already exists with different kitchen print evidence."));
    }

    private static Result ValidateParentJob(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid? parentJobId,
        CafePrintDocumentKind documentKind,
        string sourceType,
        Guid sourceId,
        CafePrintCopyKind copyKind)
    {
        if (copyKind == CafePrintCopyKind.Original)
        {
            return parentJobId.HasValue
                ? Result.Fail(
                    Error.Validation(
                        "CafePrint.OriginalParentInvalid",
                        "Original print job cannot reference ParentJobId."))
                : Result.Success();
        }

        if (!parentJobId.HasValue)
            return Result.Success();

        CafePrintJob? parent =
            ReadJob(
                connection,
                transaction,
                parentJobId.Value);

        if (parent is null ||
            parent.DocumentKind != documentKind ||
            !string.Equals(
                parent.SourceType,
                sourceType,
                StringComparison.Ordinal) ||
            parent.SourceId != sourceId)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafePrint.ParentJobConflict",
                    "Parent print job does not match the requested source/document."));
        }

        return Result.Success();
    }

    private static Result<KitchenSnapshot> BuildKitchenSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid orderId,
        Guid revisionId)
    {
        using SqliteCommand header = connection.CreateCommand();
        header.Transaction = transaction;
        header.CommandText = """
            SELECT o.store_id,o.state,o.submitted_revision_id,
                   r.revision_number,r.order_version,
                   r.service_mode,r.service_reference,
                   r.content_hash,r.created_at_utc,
                   (
                       SELECT occurred_at_utc
                       FROM cafe_order_lifecycle_events e
                       WHERE e.order_id=o.order_id
                         AND e.action='PreparationSubmitted'
                         AND e.order_revision_id=r.revision_id
                       ORDER BY e.resulting_version,e.occurred_at_utc
                       LIMIT 1
                   ) AS submitted_at_utc,
                   (
                       SELECT to_state
                       FROM cafe_order_lifecycle_events e
                       WHERE e.order_id=o.order_id
                         AND e.action='PreparationSubmitted'
                         AND e.order_revision_id=r.revision_id
                       ORDER BY e.resulting_version,e.occurred_at_utc
                       LIMIT 1
                   ) AS submitted_state
            FROM cafe_orders o
            INNER JOIN cafe_order_revisions r
              ON r.order_id=o.order_id
             AND r.revision_id=$revision
            WHERE o.order_id=$order
            LIMIT 1;
            """;
        header.Parameters.AddWithValue(
            "$revision",
            revisionId.ToString("D"));
        header.Parameters.AddWithValue(
            "$order",
            orderId.ToString("D"));

        using SqliteDataReader reader = header.ExecuteReader();

        if (!reader.Read())
        {
            return Result<KitchenSnapshot>.Fail(
                Error.NotFound(
                    "CafePrint.OrderRevisionNotFound",
                    "Cafe order revision was not found."));
        }

        Guid retainedStore = Guid.Parse(reader.GetString(0));
        Guid? submittedRevision =
            reader.IsDBNull(2)
                ? null
                : Guid.Parse(reader.GetString(2));

        if (retainedStore != storeId)
        {
            return Result<KitchenSnapshot>.Fail(
                Error.Conflict(
                    "CafePrint.OrderStoreConflict",
                    "Cafe order belongs to a different StoreId."));
        }

        if (!submittedRevision.HasValue ||
            submittedRevision.Value != revisionId ||
            reader.IsDBNull(9) ||
            reader.IsDBNull(10))
        {
            return Result<KitchenSnapshot>.Fail(
                Error.Conflict(
                    "CafePrint.OrderRevisionNotSubmitted",
                    "Kitchen ticket requires the exact accepted preparation-submitted order revision."));
        }

        int revisionNumber = reader.GetInt32(3);
        int orderVersion = reader.GetInt32(4);
        string serviceMode = reader.GetString(5);
        string? serviceReference =
            reader.IsDBNull(6)
                ? null
                : reader.GetString(6);
        string contentHash = reader.GetString(7);
        DateTimeOffset submittedAt =
            DateTimeOffset.Parse(
                reader.GetString(9),
                CultureInfo.InvariantCulture);
        string state = reader.GetString(10);

        reader.Close();

        StringBuilder text = new();
        text.AppendLine("SALRYN CAFE KITCHEN TICKET");
        text.AppendLine("Order: " + orderId.ToString("D"));
        text.AppendLine("Revision: " + revisionId.ToString("D"));
        text.AppendLine("Revision No: " + revisionNumber.ToString(CultureInfo.InvariantCulture));
        text.AppendLine("Order Version: " + orderVersion.ToString(CultureInfo.InvariantCulture));
        text.AppendLine("State: " + state);
        text.AppendLine("Service: " + serviceMode);
        text.AppendLine("Reference: " + (serviceReference ?? "-"));
        text.AppendLine("Submitted UTC: " + submittedAt.ToUniversalTime().ToString("O"));
        text.AppendLine("--------------------------------");

        using SqliteCommand lines = connection.CreateCommand();
        lines.Transaction = transaction;
        lines.CommandText = """
            SELECT order_line_id,product_name_snapshot,
                   quantity_x1000,variant_name_snapshot,
                   note,display_order
            FROM cafe_order_revision_lines
            WHERE revision_id=$revision
            ORDER BY display_order,order_line_id;
            """;
        lines.Parameters.AddWithValue(
            "$revision",
            revisionId.ToString("D"));

        using SqliteDataReader lineReader = lines.ExecuteReader();
        List<KitchenLine> snapshots = new();

        while (lineReader.Read())
        {
            snapshots.Add(
                new KitchenLine(
                    Guid.Parse(lineReader.GetString(0)),
                    lineReader.GetString(1),
                    lineReader.GetInt64(2),
                    lineReader.IsDBNull(3)
                        ? null
                        : lineReader.GetString(3),
                    lineReader.IsDBNull(4)
                        ? null
                        : lineReader.GetString(4),
                    lineReader.GetInt32(5)));
        }

        lineReader.Close();

        foreach (KitchenLine line in snapshots)
        {
            decimal quantity =
                line.QuantityX1000 / 1000m;

            text.Append(
                    quantity.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" x ")
                .Append(line.ProductName);

            if (!string.IsNullOrWhiteSpace(line.VariantName))
                text.Append(" [").Append(line.VariantName).Append(']');

            text.AppendLine();

            using SqliteCommand modifiers = connection.CreateCommand();
            modifiers.Transaction = transaction;
            modifiers.CommandText = """
                SELECT group_name_snapshot,option_name_snapshot
                FROM cafe_order_revision_modifiers
                WHERE revision_id=$revision
                  AND order_line_id=$line
                ORDER BY display_order,group_id,option_id;
                """;
            modifiers.Parameters.AddWithValue(
                "$revision",
                revisionId.ToString("D"));
            modifiers.Parameters.AddWithValue(
                "$line",
                line.OrderLineId.ToString("D"));

            using SqliteDataReader modifierReader =
                modifiers.ExecuteReader();

            while (modifierReader.Read())
            {
                text.Append("  + ")
                    .Append(modifierReader.GetString(0))
                    .Append(": ")
                    .AppendLine(modifierReader.GetString(1));
            }

            if (!string.IsNullOrWhiteSpace(line.Note))
                text.Append("  NOTE: ").AppendLine(line.Note);
        }

        return Result<KitchenSnapshot>.Success(
            new(
                text.ToString(),
                contentHash));
    }

    private static string BuildVoidedReceiptCopy(
        SaleSource sale)
    {
        StringBuilder text = new();
        text.Append(sale.ReceiptSnapshot);
        if (!sale.ReceiptSnapshot!.EndsWith(
                Environment.NewLine,
                StringComparison.Ordinal))
        {
            text.AppendLine();
        }

        text.AppendLine();
        text.AppendLine("*** VOIDED RECEIPT COPY ***");
        text.AppendLine("Receipt: " + sale.ReceiptNo);
        text.AppendLine(
            "Voided UTC: " +
            (sale.VoidedAtUtc?.ToUniversalTime().ToString("O") ?? "UNKNOWN"));
        text.AppendLine(
            "Void reason: " +
            (string.IsNullOrWhiteSpace(sale.VoidReason)
                ? "Not recorded"
                : sale.VoidReason));
        text.AppendLine("*** ORIGINAL RECEIPT BODY ABOVE IS UNCHANGED ***");

        return text.ToString();
    }

    private static Result ValidateStationForDocument(
        CafePrinterStation? station,
        Guid storeId,
        CafePrintDocumentKind documentKind)
    {
        if (station is null ||
            station.StoreId != storeId ||
            !station.IsActive)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafePrint.StationUnavailable",
                    "Active logical printer station for this store is required."));
        }

        bool kindMatches =
            documentKind == CafePrintDocumentKind.CustomerReceipt
                ? station.StationKind ==
                    CafePrinterStationKind.CustomerReceipt
                : station.StationKind is
                    CafePrinterStationKind.Kitchen or
                    CafePrinterStationKind.BarDrinks or
                    CafePrinterStationKind.OtherPreparation;

        return kindMatches
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafePrint.StationKindMismatch",
                    "Logical printer station kind does not accept this document kind."));
    }

    private static Result ValidateAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid actorUserId,
        string permission)
    {
        using (SqliteCommand store = connection.CreateCommand())
        {
            store.Transaction = transaction;
            store.CommandText = """
                SELECT COUNT(*)
                FROM deployment_identity
                WHERE store_id=$store;
                """;
            store.Parameters.AddWithValue(
                "$store",
                storeId.ToString("D"));

            if (Convert.ToInt64(
                    store.ExecuteScalar() ?? 0L) != 1)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafePrint.StoreAuthorityMismatch",
                        "Print StoreId does not match local deployment authority."));
            }
        }

        using SqliteCommand actor = connection.CreateCommand();
        actor.Transaction = transaction;
        actor.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN role_permissions rp
              ON rp.role_id=u.role_id
            WHERE u.id=$user
              AND u.status=1
              AND rp.permission=$permission;
            """;
        actor.Parameters.AddWithValue(
            "$user",
            actorUserId.ToString("D"));
        actor.Parameters.AddWithValue(
            "$permission",
            permission);

        return Convert.ToInt64(
                   actor.ExecuteScalar() ?? 0L) == 1
            ? Result.Success()
            : Result.Fail(
                Error.Forbidden(
                    "CafePrint.PermissionRequired",
                    "Print operation requires permission: " +
                    permission));
    }

    private static CafePrinterStation? ReadStation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid stationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT station_id,store_id,name,station_kind,route_key,
                   is_active,version,content_hash,
                   created_by_user_id,created_at_utc,
                   updated_by_user_id,updated_at_utc
            FROM cafe_printer_stations
            WHERE station_id=$station
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$station",
            stationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? MapStation(reader)
            : null;
    }

    private static CafePrinterStation MapStation(
        SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            (CafePrinterStationKind)reader.GetInt32(3),
            reader.IsDBNull(4)
                ? null
                : reader.GetString(4),
            reader.GetInt32(5) == 1,
            reader.GetInt64(6),
            reader.GetString(7),
            Guid.Parse(reader.GetString(8)),
            DateTimeOffset.Parse(
                reader.GetString(9),
                CultureInfo.InvariantCulture),
            reader.IsDBNull(10)
                ? null
                : Guid.Parse(reader.GetString(10)),
            reader.IsDBNull(11)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(11),
                    CultureInfo.InvariantCulture));

    private static void InsertStation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafePrinterStation station)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_printer_stations(
                station_id,store_id,name,station_kind,route_key,
                is_active,version,content_hash,
                created_by_user_id,created_at_utc,
                updated_by_user_id,updated_at_utc)
            VALUES(
                $station,$store,$name,$kind,$route,
                $active,$version,$hash,
                $createdBy,$createdAt,NULL,NULL);
            """;
        AddStationParameters(command, station);
        command.ExecuteNonQuery();
    }

    private static void UpdateStation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafePrinterStation station)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE cafe_printer_stations
            SET name=$name,
                station_kind=$kind,
                route_key=$route,
                is_active=$active,
                version=$version,
                content_hash=$hash,
                updated_by_user_id=$updatedBy,
                updated_at_utc=$updatedAt
            WHERE station_id=$station
              AND store_id=$store;
            """;
        AddStationParameters(command, station);
        command.Parameters.AddWithValue(
            "$updatedBy",
            station.UpdatedByUserId?.ToString("D") ??
                (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$updatedAt",
            station.UpdatedAtUtc?.ToUniversalTime().ToString("O") ??
                (object)DBNull.Value);

        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException(
                "Printer station update lost ownership.");
    }

    private static void AddStationParameters(
        SqliteCommand command,
        CafePrinterStation station)
    {
        command.Parameters.AddWithValue(
            "$station",
            station.StationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$store",
            station.StoreId.ToString("D"));
        command.Parameters.AddWithValue(
            "$name",
            station.Name);
        command.Parameters.AddWithValue(
            "$kind",
            Convert.ToInt32(station.StationKind));
        command.Parameters.AddWithValue(
            "$route",
            station.RouteKey ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$active",
            station.IsActive ? 1 : 0);
        command.Parameters.AddWithValue(
            "$version",
            station.Version);
        command.Parameters.AddWithValue(
            "$hash",
            station.ContentHash);
        command.Parameters.AddWithValue(
            "$createdBy",
            station.CreatedByUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$createdAt",
            station.CreatedAtUtc.ToUniversalTime().ToString("O"));
    }

    private static SaleSource? ReadSaleSource(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,receipt_no,status,void_reason,voided_at,receipt_snapshot
            FROM sales
            WHERE id=$sale
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$sale",
            saleId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new SaleSource(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            (SaleStatus)reader.GetInt32(2),
            reader.IsDBNull(3)
                ? null
                : reader.GetString(3),
            reader.IsDBNull(4)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(4),
                    CultureInfo.InvariantCulture),
            reader.IsDBNull(5)
                ? null
                : reader.GetString(5));
    }

    private static CafePrintJob? ReadJobByOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            JobSelectSql +
            " WHERE operation_id=$operation LIMIT 1;";
        command.Parameters.AddWithValue(
            "$operation",
            operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? MapJob(reader)
            : null;
    }

    private static CafePrintJob? ReadJob(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid jobId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            JobSelectSql +
            " WHERE job_id=$job LIMIT 1;";
        command.Parameters.AddWithValue(
            "$job",
            jobId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? MapJob(reader)
            : null;
    }

    private static bool JobIdExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid jobId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM cafe_print_jobs WHERE job_id=$job;";
        command.Parameters.AddWithValue(
            "$job",
            jobId.ToString("D"));

        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L) != 0;
    }

    private static void InsertJob(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafePrintJob job)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_print_jobs(
                job_id,operation_id,store_id,device_id,actor_user_id,
                station_id,document_kind,source_type,source_id,source_version,
                snapshot_text,snapshot_hash,copy_kind,parent_job_id,
                payload_hash,created_at_utc)
            VALUES(
                $job,$operation,$store,$device,$actor,
                $station,$document,$sourceType,$sourceId,$sourceVersion,
                $snapshot,$snapshotHash,$copy,$parent,
                $payloadHash,$createdAt);
            """;
        command.Parameters.AddWithValue(
            "$job",
            job.JobId.ToString("D"));
        command.Parameters.AddWithValue(
            "$operation",
            job.OperationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$store",
            job.StoreId.ToString("D"));
        command.Parameters.AddWithValue(
            "$device",
            job.DeviceId.ToString("D"));
        command.Parameters.AddWithValue(
            "$actor",
            job.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$station",
            job.StationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$document",
            Convert.ToInt32(job.DocumentKind));
        command.Parameters.AddWithValue(
            "$sourceType",
            job.SourceType);
        command.Parameters.AddWithValue(
            "$sourceId",
            job.SourceId.ToString("D"));
        command.Parameters.AddWithValue(
            "$sourceVersion",
            job.SourceVersion ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$snapshot",
            job.SnapshotText);
        command.Parameters.AddWithValue(
            "$snapshotHash",
            job.SnapshotHash);
        command.Parameters.AddWithValue(
            "$copy",
            Convert.ToInt32(job.CopyKind));
        command.Parameters.AddWithValue(
            "$parent",
            job.ParentJobId?.ToString("D") ??
                (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$payloadHash",
            job.PayloadHash);
        command.Parameters.AddWithValue(
            "$createdAt",
            job.CreatedAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static CafePrintJob MapJob(
        SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            Guid.Parse(reader.GetString(4)),
            Guid.Parse(reader.GetString(5)),
            (CafePrintDocumentKind)reader.GetInt32(6),
            reader.GetString(7),
            Guid.Parse(reader.GetString(8)),
            reader.IsDBNull(9)
                ? null
                : reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            (CafePrintCopyKind)reader.GetInt32(12),
            reader.IsDBNull(13)
                ? null
                : Guid.Parse(reader.GetString(13)),
            reader.GetString(14),
            DateTimeOffset.Parse(
                reader.GetString(15),
                CultureInfo.InvariantCulture));

    private static int ReadNextAttemptNumber(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid jobId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(MAX(attempt_number),0) + 1
            FROM cafe_print_attempts
            WHERE job_id=$job;
            """;
        command.Parameters.AddWithValue(
            "$job",
            jobId.ToString("D"));

        return Convert.ToInt32(
            command.ExecuteScalar() ?? 1,
            CultureInfo.InvariantCulture);
    }

    private static void InsertAttempt(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafePrintAttempt attempt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_print_attempts(
                attempt_id,job_id,device_id,attempt_number,
                adapter_name,adapter_mode,status,result_code,
                result_message,error_message,
                requested_at_utc,completed_at_utc)
            VALUES(
                $attempt,$job,$device,$number,
                $adapter,$mode,$status,$code,
                $message,$error,$requested,$completed);
            """;
        command.Parameters.AddWithValue(
            "$attempt",
            attempt.AttemptId.ToString("D"));
        command.Parameters.AddWithValue(
            "$job",
            attempt.JobId.ToString("D"));
        command.Parameters.AddWithValue(
            "$device",
            attempt.DeviceId.ToString("D"));
        command.Parameters.AddWithValue(
            "$number",
            attempt.AttemptNumber);
        command.Parameters.AddWithValue(
            "$adapter",
            attempt.AdapterName);
        command.Parameters.AddWithValue(
            "$mode",
            attempt.AdapterMode);
        command.Parameters.AddWithValue(
            "$status",
            Convert.ToInt32(attempt.Status));
        command.Parameters.AddWithValue(
            "$code",
            attempt.ResultCode);
        command.Parameters.AddWithValue(
            "$message",
            attempt.ResultMessage);
        command.Parameters.AddWithValue(
            "$error",
            attempt.ErrorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$requested",
            attempt.RequestedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue(
            "$completed",
            attempt.CompletedAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static CafePrintAttemptStatus MapAttemptStatus(
        ReceiptPrinterAdapterResult result)
    {
        if (result.Succeeded)
            return CafePrintAttemptStatus.Succeeded;

        return string.Equals(
                result.Status,
                PosReceiptPrintStatuses.DisabledUnavailable,
                StringComparison.Ordinal)
            ? CafePrintAttemptStatus.DisabledUnavailable
            : CafePrintAttemptStatus.Failed;
    }

    private static string ResolveAdapterMode(
        IReceiptPrinterAdapter adapter)
    {
        if (adapter is WindowsQueueReceiptPrinterAdapter)
            return ReceiptPrinterModes.WindowsPrinter;

        if (!adapter.IsEnabled)
            return ReceiptPrinterModes.Disabled;

        return adapter.GetType().Name;
    }

    private static string BuildAdapterJobName(
        CafePrintJob job) =>
        job.DocumentKind == CafePrintDocumentKind.CustomerReceipt
            ? "Cafe receipt " + job.SourceVersion
            : "Cafe kitchen ticket " + job.SourceId.ToString("D");

    private static string CanonicalJobRequest(
        Guid operationId,
        Guid jobId,
        Guid deviceId,
        Guid storeId,
        Guid actorUserId,
        Guid stationId,
        CafePrintDocumentKind documentKind,
        string sourceType,
        Guid sourceId,
        string? sourceVersion,
        string snapshotHash,
        CafePrintCopyKind copyKind,
        Guid? parentJobId,
        DateTimeOffset occurredAtUtc) =>
        string.Join(
            "|",
            operationId.ToString("D"),
            jobId.ToString("D"),
            deviceId.ToString("D"),
            storeId.ToString("D"),
            actorUserId.ToString("D"),
            stationId.ToString("D"),
            Convert.ToInt32(documentKind)
                .ToString(CultureInfo.InvariantCulture),
            sourceType,
            sourceId.ToString("D"),
            sourceVersion ?? "<null>",
            snapshotHash,
            Convert.ToInt32(copyKind)
                .ToString(CultureInfo.InvariantCulture),
            parentJobId?.ToString("D") ?? "<null>",
            occurredAtUtc.ToUniversalTime().ToString("O"));

    private static string Hash(string text) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? ValidateStationMutation(
        CafePrinterStationMutation mutation)
    {
        if (mutation is null)
            return "Station mutation is required.";

        if (mutation.OperationId == Guid.Empty ||
            mutation.StationId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty)
        {
            return "Station identities must be non-empty.";
        }

        if (string.IsNullOrWhiteSpace(mutation.Name))
            return "Station name is required.";

        if (mutation.ExpectedVersion < 0)
            return "ExpectedVersion cannot be negative.";

        if (!Enum.IsDefined(mutation.StationKind))
            return "Station kind is not supported.";

        return null;
    }

    private static string? ValidateReceiptMutation(
        CafeReceiptPrintJobMutation mutation)
    {
        if (mutation is null)
            return "Receipt job mutation is required.";

        if (mutation.OperationId == Guid.Empty ||
            mutation.JobId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.StationId == Guid.Empty ||
            mutation.SaleId == Guid.Empty)
        {
            return "Receipt print identities must be non-empty.";
        }

        if (!Enum.IsDefined(mutation.CopyKind))
            return "Receipt copy kind is invalid.";

        if (mutation.CopyKind == CafePrintCopyKind.Original &&
            mutation.ParentJobId.HasValue)
        {
            return "Original receipt job cannot reference ParentJobId.";
        }

        return null;
    }

    private static string? ValidateKitchenMutation(
        CafeKitchenPrintJobMutation mutation)
    {
        if (mutation is null)
            return "Kitchen job mutation is required.";

        if (mutation.OperationId == Guid.Empty ||
            mutation.JobId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.StationId == Guid.Empty ||
            mutation.OrderId == Guid.Empty ||
            mutation.OrderRevisionId == Guid.Empty)
        {
            return "Kitchen print identities must be non-empty.";
        }

        if (mutation.CopyKind == CafePrintCopyKind.VoidedReprint)
            return "VoidedReprint is not valid for kitchen tickets.";

        if (mutation.CopyKind == CafePrintCopyKind.Original &&
            mutation.ParentJobId.HasValue)
        {
            return "Original kitchen job cannot reference ParentJobId.";
        }

        return null;
    }

    private const string JobSelectSql = """
        SELECT job_id,operation_id,store_id,device_id,actor_user_id,
               station_id,document_kind,source_type,source_id,source_version,
               snapshot_text,snapshot_hash,copy_kind,parent_job_id,
               payload_hash,created_at_utc
        FROM cafe_print_jobs
        """;

    private sealed record SaleSource(
        Guid SaleId,
        string ReceiptNo,
        SaleStatus Status,
        string? VoidReason,
        DateTimeOffset? VoidedAtUtc,
        string? ReceiptSnapshot);

    private sealed record KitchenSnapshot(
        string Text,
        string ContentHash);

    private sealed record KitchenLine(
        Guid OrderLineId,
        string ProductName,
        long QuantityX1000,
        string? VariantName,
        string? Note,
        int DisplayOrder);
}
