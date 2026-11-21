using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;

namespace Salryn.Api.Local.Services;

/// <summary>
/// Unified C1-C11 durable mutation idempotency guard backed by <c>cafe_mutation_idempotency</c>.
/// Enforces fail-closed client-supplied <c>operationId</c> (body or <c>Idempotency-Key</c>/<c>X-Operation-Id</c> header),
/// per-key concurrency serialization, exact payload-hash replay, and <c>409 ERR_IDEMPOTENCY_CONFLICT</c> on payload divergence.
/// </summary>
public sealed class CafeMutationIdempotencyGuard
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal const string CanonicalCompletionMetadata = "Salryn.Cafe.CanonicalOrderCompletion";
    private static readonly object OutcomeScopeKey = new();
    private sealed record OutcomeScope(Guid OperationId, string EndpointKey, Guid ActorUserId, string RequestHash);

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly object _gateSync = new();
    private readonly Dictionary<Guid, GateEntry> _gates = new();

    public CafeMutationIdempotencyGuard(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public static bool TryResolveOperationId(
        HttpContext httpContext,
        Guid? bodyOperationId,
        out Guid operationId)
    {
        // Every supplied identifier must be valid and agree. Silently preferring the
        // body over a conflicting header can make retries ambiguous across clients.
        bool hasPrimary = httpContext.Request.Headers.ContainsKey("Idempotency-Key");
        bool hasSecondary = httpContext.Request.Headers.ContainsKey("X-Operation-Id");
        if ((hasPrimary && !TryParseHeaderGuid(httpContext, "Idempotency-Key", out _)) ||
            (hasSecondary && !TryParseHeaderGuid(httpContext, "X-Operation-Id", out _)))
        {
            operationId = Guid.Empty;
            return false;
        }

        Guid? resolved = bodyOperationId is { } bodyId && bodyId != Guid.Empty
            ? bodyId
            : null;
        if (hasPrimary)
        {
            TryParseHeaderGuid(httpContext, "Idempotency-Key", out Guid primary);
            if (resolved.HasValue && resolved.Value != primary)
            {
                operationId = Guid.Empty;
                return false;
            }

            resolved = primary;
        }

        if (hasSecondary)
        {
            TryParseHeaderGuid(httpContext, "X-Operation-Id", out Guid secondary);
            if (resolved.HasValue && resolved.Value != secondary)
            {
                operationId = Guid.Empty;
                return false;
            }

            resolved = secondary;
        }

        operationId = resolved ?? Guid.Empty;
        return operationId != Guid.Empty;
    }

    public static IResult OperationIdRequiredResult()
    {
        return Results.Json(
            new
            {
                code = "CafeMutation.OperationIdRequired",
                error = "A valid client-supplied operationId (or Idempotency-Key header) is required."
            },
            statusCode: StatusCodes.Status400BadRequest);
    }

    public static string ComputeRequestHash(string endpointKey, Guid actorUserId, object? payload)
    {
        string serialized = JsonSerializer.Serialize(payload, JsonOptions);
        string canonical = $"{endpointKey}|{actorUserId:D}|{serialized}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    public async Task<IResult> ExecuteAsync(
        HttpContext httpContext,
        string endpointKey,
        Guid actorUserId,
        Guid? bodyOperationId,
        object? canonicalPayload,
        Func<Guid, Task<IdempotentExecutionOutcome>> executeAsync,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveOperationId(httpContext, bodyOperationId, out Guid operationId))
        {
            return OperationIdRequiredResult();
        }

        string requestHash = ComputeRequestHash(endpointKey, actorUserId, canonicalPayload);
        GateEntry gate = RentGate(operationId);
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReturnGate(operationId, gate);
            throw;
        }

        try
        {
            StoredIdempotencyRecord? existing = FindRecord(operationId);
            if (existing is not null)
            {
                return BuildReplayOrConflictResult(httpContext, existing, endpointKey, requestHash);
            }

            // Durable ownership must precede any callback that can commit independently.
            // A process crash cannot authorize a second invocation. A fence without an
            // outcome is UNKNOWN, never success, and requires domain-specific recovery.
            if (!TryClaimExecution(operationId, endpointKey, actorUserId, requestHash))
            {
                existing = FindRecord(operationId);
                if (existing is not null)
                    return BuildReplayOrConflictResult(httpContext, existing, endpointKey, requestHash);
                // Only the canonical completion endpoint may reconstruct an outcome.
                // Its coordinator independently checks immutable operation evidence and
                // serializes posting in the same SQLite transaction. Other callbacks
                // remain fenced until they supply an equivalent reviewed recovery path.
                if (!CanRecoverCanonicalCompletion(httpContext, operationId, endpointKey, actorUserId, requestHash))
                    return BuildFencedResult(operationId, endpointKey, actorUserId, requestHash);
            }

            httpContext.Items[OutcomeScopeKey] = new OutcomeScope(operationId, endpointKey, actorUserId, requestHash);
            IdempotentExecutionOutcome outcome = await executeAsync(operationId).ConfigureAwait(false);
            string responseJson = JsonSerializer.Serialize(outcome.Body, JsonOptions);

            // Authorization can change between attempts. Never pin an authentication
            // or permission denial to a durable business-operation identifier.
            if (outcome.StatusCode is >= 200 and < 500
                && outcome.StatusCode is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden))
            {
                bool inserted = TryInsertRecord(
                    operationId,
                    endpointKey,
                    actorUserId,
                    requestHash,
                    outcome.StatusCode,
                    responseJson);

                if (!inserted)
                {
                    StoredIdempotencyRecord? raced = FindRecord(operationId);
                    if (raced is not null)
                    {
                        return BuildReplayOrConflictResult(httpContext, raced, endpointKey, requestHash);
                    }
                }
            }

            // Authorization denials are defined to have no business effect. Release only
            // that invocation's coordination fence so a subsequent authorized retry works.
            if (outcome.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
                ReleaseDeniedExecution(operationId, endpointKey, actorUserId, requestHash);

            return Results.Content(responseJson, "application/json", Encoding.UTF8, outcome.StatusCode);
        }
        finally
        {
            httpContext.Items.Remove(OutcomeScopeKey);
            gate.Semaphore.Release();
            ReturnGate(operationId, gate);
        }
    }

    public Task<IResult> ExecuteSyncAsync(
        HttpContext httpContext,
        string endpointKey,
        Guid actorUserId,
        Guid? bodyOperationId,
        object? canonicalPayload,
        Func<Guid, IdempotentExecutionOutcome> execute,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            httpContext,
            endpointKey,
            actorUserId,
            bodyOperationId,
            canonicalPayload,
            opId => Task.FromResult(execute(opId)),
            cancellationToken);
    }

    internal void PersistAtomicOutcome(HttpContext httpContext, Guid operationId,
        SqliteConnection connection, SqliteTransaction transaction, IdempotentExecutionOutcome outcome)
    {
        if (!httpContext.Items.TryGetValue(OutcomeScopeKey, out var value) || value is not OutcomeScope scope ||
            scope.OperationId != operationId || transaction.Connection != connection ||
            !string.Equals(Path.GetFullPath(connection.DataSource), Path.GetFullPath(_connectionFactory.DatabasePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            outcome.StatusCode is < 200 or >= 300)
            throw new InvalidOperationException("Atomic outcome requires the current operation's canonical transaction.");
        string json = JsonSerializer.Serialize(outcome.Body, JsonOptions);
        if (!TryInsertRecord(connection, transaction, operationId, scope.EndpointKey, scope.ActorUserId,
                scope.RequestHash, outcome.StatusCode, json))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT endpoint_key,actor_user_id,request_hash,status_code,response_json FROM cafe_mutation_idempotency WHERE operation_id=$op;";
            command.Parameters.AddWithValue("$op",operationId.ToString("D"));
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetString(0)!=scope.EndpointKey || reader.GetString(1)!=scope.ActorUserId.ToString("D") ||
                reader.GetString(2)!=scope.RequestHash || reader.GetInt32(3)!=outcome.StatusCode || reader.GetString(4)!=json)
                throw new InvalidOperationException("Immutable operation outcome conflicts with transaction result.");
        }
    }

    private bool CanRecoverCanonicalCompletion(HttpContext context, Guid operationId, string endpointKey, Guid actor, string hash)
    {
        if (endpointKey != "POST /api/cafe/orders/{orderId:guid}/complete" ||
            context.GetEndpoint()?.Metadata.GetOrderedMetadata<string>().Contains(CanonicalCompletionMetadata) != true)
            return false;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM cafe_mutation_execution_fences WHERE operation_id=$op AND endpoint_key=$endpoint AND actor_user_id=$actor AND request_hash=$hash;";
        command.Parameters.AddWithValue("$op",operationId.ToString("D"));
        command.Parameters.AddWithValue("$endpoint",endpointKey);
        command.Parameters.AddWithValue("$actor",actor.ToString("D"));
        command.Parameters.AddWithValue("$hash",hash);
        return command.ExecuteScalar() is not null;
    }

    private bool TryClaimExecution(Guid operationId, string endpointKey, Guid actorUserId, string requestHash)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO cafe_mutation_execution_fences
                (operation_id, endpoint_key, actor_user_id, request_hash, created_at_utc)
            VALUES ($op, $endpoint, $actor, $hash, $now);
            """;
        command.Parameters.AddWithValue("$op", operationId.ToString("D"));
        command.Parameters.AddWithValue("$endpoint", endpointKey);
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$hash", requestHash);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        return command.ExecuteNonQuery() == 1;
    }

    private IResult BuildFencedResult(Guid operationId, string endpointKey, Guid actorUserId, string requestHash)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT endpoint_key, actor_user_id, request_hash FROM cafe_mutation_execution_fences WHERE operation_id=$op;";
        command.Parameters.AddWithValue("$op", operationId.ToString("D"));
        using var reader = command.ExecuteReader();
        bool same = reader.Read() && reader.GetString(0) == endpointKey &&
            reader.GetString(1) == actorUserId.ToString("D") && reader.GetString(2) == requestHash;
        return Results.Json(new {
            code = same ? "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN" : "ERR_IDEMPOTENCY_CONFLICT",
            error = same ? "Operation is executing or its committed outcome needs reconciliation. Retain this operationId; do not submit a replacement operation."
                         : "OperationId was already claimed with a different actor, endpoint or payload."
        }, statusCode: StatusCodes.Status409Conflict);
    }

    private void ReleaseDeniedExecution(Guid operationId, string endpointKey, Guid actorUserId, string requestHash)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM cafe_mutation_execution_fences
            WHERE operation_id=$op AND endpoint_key=$endpoint AND actor_user_id=$actor AND request_hash=$hash;
            """;
        command.Parameters.AddWithValue("$op", operationId.ToString("D"));
        command.Parameters.AddWithValue("$endpoint", endpointKey);
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$hash", requestHash);
        command.ExecuteNonQuery();
    }

    private static IResult BuildReplayOrConflictResult(
        HttpContext httpContext,
        StoredIdempotencyRecord existing,
        string expectedEndpointKey,
        string expectedRequestHash)
    {
        if (!string.Equals(existing.EndpointKey, expectedEndpointKey, StringComparison.Ordinal) ||
            !string.Equals(existing.RequestHash, expectedRequestHash, StringComparison.Ordinal))
        {
            return Results.Json(
                new
                {
                    code = "ERR_IDEMPOTENCY_CONFLICT",
                    error = "OperationId was already used with a different endpoint or request payload."
                },
                statusCode: StatusCodes.Status409Conflict);
        }

        httpContext.Response.Headers["X-Idempotency-Replayed"] = "true";
        return Results.Content(existing.ResponseJson, "application/json", Encoding.UTF8, existing.StatusCode);
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();
        return connection;
    }

    private StoredIdempotencyRecord? FindRecord(Guid operationId)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT endpoint_key, request_hash, status_code, response_json
            FROM cafe_mutation_idempotency
            WHERE operation_id = $operationId
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$operationId", operationId.ToString("D"));

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new StoredIdempotencyRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3));
    }

    private bool TryInsertRecord(
        Guid operationId,
        string endpointKey,
        Guid actorUserId,
        string requestHash,
        int statusCode,
        string responseJson)
    {
        using SqliteConnection connection = OpenConnection();
        return TryInsertRecord(connection, null, operationId, endpointKey, actorUserId, requestHash, statusCode, responseJson);
    }

    private static bool TryInsertRecord(SqliteConnection connection, SqliteTransaction? transaction,
        Guid operationId, string endpointKey, Guid actorUserId, string requestHash, int statusCode, string responseJson)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT OR IGNORE INTO cafe_mutation_idempotency (
                operation_id,
                endpoint_key,
                actor_user_id,
                request_hash,
                status_code,
                response_json,
                created_at_utc
            )
            VALUES (
                $operationId,
                $endpointKey,
                $actorUserId,
                $requestHash,
                $statusCode,
                $responseJson,
                $createdAtUtc
            );
            """;
        cmd.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        cmd.Parameters.AddWithValue("$endpointKey", endpointKey);
        cmd.Parameters.AddWithValue("$actorUserId", actorUserId.ToString("D"));
        cmd.Parameters.AddWithValue("$requestHash", requestHash);
        cmd.Parameters.AddWithValue("$statusCode", statusCode);
        cmd.Parameters.AddWithValue("$responseJson", responseJson);
        cmd.Parameters.AddWithValue("$createdAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        return cmd.ExecuteNonQuery() > 0;
    }

    private static bool TryParseHeaderGuid(HttpContext httpContext, string headerName, out Guid value)
    {
        if (httpContext.Request.Headers.TryGetValue(headerName, out var raw) &&
            Guid.TryParse(raw.ToString().Trim(), out Guid parsed) &&
            parsed != Guid.Empty)
        {
            value = parsed;
            return true;
        }

        value = Guid.Empty;
        return false;
    }

    private GateEntry RentGate(Guid key)
    {
        lock (_gateSync)
        {
            if (!_gates.TryGetValue(key, out GateEntry? entry))
            {
                entry = new GateEntry();
                _gates[key] = entry;
            }

            entry.RefCount++;
            return entry;
        }
    }

    private void ReturnGate(Guid key, GateEntry entry)
    {
        lock (_gateSync)
        {
            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                _gates.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class GateEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount { get; set; }
    }

    private sealed record StoredIdempotencyRecord(
        string EndpointKey,
        string RequestHash,
        int StatusCode,
        string ResponseJson);
}

public sealed record IdempotentExecutionOutcome(int StatusCode, object? Body);
