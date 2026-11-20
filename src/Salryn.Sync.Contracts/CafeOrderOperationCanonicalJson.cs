using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public sealed record CafeOrderCanonicalParseResult(
    bool IsValid,
    CafeOrderOperationV1? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CafeOrderCanonicalParseResult Success(CafeOrderOperationV1 payload) =>
        new(true, payload, null, null);

    public static CafeOrderCanonicalParseResult Fail(string code, string message) =>
        new(false, null, code, message);
}

public static class CafeOrderOperationCanonicalJson
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(CafeOrderOperationV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Json);

    public static string ComputeHash(string canonicalJson) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
        .ToLowerInvariant();

    public static CafeOrderCanonicalParseResult Parse(
        string operationType,
        string canonicalJson)
    {
        if (!CafeOrderOperationContract.IsSupported(operationType))
        {
            return CafeOrderCanonicalParseResult.Fail(
                "CafeOrder.ContractUnsupported",
                "Operation type is not a supported C4 order semantic contract.");
        }

        try
        {
            CafeOrderOperationV1? payload =
                JsonSerializer.Deserialize<CafeOrderOperationV1>(
                    canonicalJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (payload is null)
            {
                return CafeOrderCanonicalParseResult.Fail(
                    "CafeOrder.PayloadMissing",
                    "C4 order payload is missing.");
            }

            if (!string.Equals(
                    payload.OperationType,
                    operationType,
                    StringComparison.Ordinal))
            {
                return CafeOrderCanonicalParseResult.Fail(
                    "CafeOrder.OperationTypeMismatch",
                    "Envelope operation type does not match the canonical payload.");
            }

            string? validation = Validate(payload);
            if (validation is not null)
            {
                return CafeOrderCanonicalParseResult.Fail(
                    "CafeOrder.PayloadInvalid",
                    validation);
            }

            string normalized = Serialize(payload);
            if (!string.Equals(
                    normalized,
                    canonicalJson,
                    StringComparison.Ordinal))
            {
                return CafeOrderCanonicalParseResult.Fail(
                    "CafeOrder.PayloadNotCanonical",
                    "C4 order payload is not canonical JSON.");
            }

            return CafeOrderCanonicalParseResult.Success(payload);
        }
        catch (JsonException ex)
        {
            return CafeOrderCanonicalParseResult.Fail(
                "CafeOrder.PayloadInvalidJson",
                ex.Message);
        }
        catch (OverflowException ex)
        {
            return CafeOrderCanonicalParseResult.Fail(
                "CafeOrder.PayloadOverflow",
                ex.Message);
        }
    }

    public static string? Validate(CafeOrderOperationV1 payload)
    {
        if (!CafeOrderOperationContract.IsSupported(payload.OperationType))
        {
            return "Unsupported order operation type.";
        }

        if (payload.OrderId == Guid.Empty ||
            payload.StoreId == Guid.Empty ||
            payload.ActorUserId == Guid.Empty ||
            payload.CurrentRevisionId == Guid.Empty)
        {
            return "Order, store, actor and current revision identities are required.";
        }

        if (payload.ExpectedVersion < 0 ||
            payload.ResultingVersion != checked(payload.ExpectedVersion + 1))
        {
            return "Order version must advance exactly once from ExpectedVersion.";
        }

        if (!IsState(payload.ToState) ||
            !IsServiceMode(payload.ServiceMode) ||
            string.IsNullOrWhiteSpace(payload.MenuPublicationRevisionId))
        {
            return "Order state, service mode and bound MenuPublication are required.";
        }

        if (payload.FromState is not null && !IsState(payload.FromState))
        {
            return "FromState is invalid.";
        }

        string? operationError = ValidateOperationShape(payload);
        if (operationError is not null)
        {
            return operationError;
        }

        if (payload.Revision is not null)
        {
            string? revisionError = ValidateRevision(payload);
            if (revisionError is not null)
            {
                return revisionError;
            }
        }

        return null;
    }

    private static string? ValidateOperationShape(CafeOrderOperationV1 payload)
    {
        switch (payload.OperationType)
        {
            case CafeOrderOperationContract.OrderRevisionCommittedV1:
                if (payload.Revision is null)
                    return "Revision operation requires immutable revision content.";

                if (payload.ExpectedVersion == 0)
                {
                    if (!string.Equals(payload.Action, "Created", StringComparison.Ordinal) ||
                        payload.FromState is not null ||
                        !string.Equals(payload.ToState, "Open", StringComparison.Ordinal))
                    {
                        return "Initial revision must create an Open order from version zero.";
                    }
                }
                else
                {
                    if (!string.Equals(payload.Action, "Revised", StringComparison.Ordinal) ||
                        payload.FromState is null ||
                        !string.Equals(payload.FromState, payload.ToState, StringComparison.Ordinal) ||
                        (payload.ToState is not "Open" and not "Parked"))
                    {
                        return "Later revisions may only revise an Open or Parked order without a state transition.";
                    }
                }
                break;

            case CafeOrderOperationContract.OrderParkedV1:
                if (!Transition(payload, "Parked", "Open", "Parked"))
                    return "Park requires Open -> Parked.";
                break;

            case CafeOrderOperationContract.OrderResumedV1:
                if (!Transition(payload, "Resumed", "Parked", "Open"))
                    return "Resume requires Parked -> Open.";
                break;

            case CafeOrderOperationContract.OrderPreparationSubmittedV1:
                if (!Transition(payload, "PreparationSubmitted", "Open", "Submitted") ||
                    payload.SubmittedRevisionId != payload.CurrentRevisionId)
                {
                    return "Preparation submit requires Open -> Submitted and freezes the current revision.";
                }
                break;

            case CafeOrderOperationContract.OrderPreparationStatusChangedV1:
                bool preparing =
                    Transition(payload, "PreparationPreparing", "Submitted", "Preparing");
                bool readyFromSubmitted =
                    Transition(payload, "PreparationReady", "Submitted", "Ready");
                bool readyFromPreparing =
                    Transition(payload, "PreparationReady", "Preparing", "Ready");
                if (!preparing && !readyFromSubmitted && !readyFromPreparing)
                    return "Preparation status transition is not allowed.";
                break;

            case CafeOrderOperationContract.OrderCancelledV1:
                if (!string.Equals(payload.Action, "Cancelled", StringComparison.Ordinal) ||
                    payload.FromState is null ||
                    payload.FromState is "Completed" or "Cancelled" ||
                    !string.Equals(payload.ToState, "Cancelled", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(payload.Reason))
                {
                    return "Cancellation requires a non-terminal source state and a reason.";
                }
                break;

            case CafeOrderOperationContract.OrderCompletedV1:
                if (!Transition(payload, "Completed", "Ready", "Completed") ||
                    !payload.SaleId.HasValue ||
                    payload.SaleId.Value == Guid.Empty ||
                    !payload.SaleCommittedOperationId.HasValue ||
                    payload.SaleCommittedOperationId.Value == Guid.Empty ||
                    string.IsNullOrWhiteSpace(payload.ReceiptNumber) ||
                    payload.SaleItemLinks is null ||
                    payload.SaleItemLinks.Count == 0)
                {
                    return "Completion requires Ready -> Completed, Sale/SaleCommitted identity and line correlation.";
                }

                if (payload.SaleItemLinks.Any(x =>
                        x.SaleItemId == Guid.Empty ||
                        x.OrderLineId == Guid.Empty) ||
                    payload.SaleItemLinks.Select(x => x.SaleItemId).Distinct().Count() !=
                        payload.SaleItemLinks.Count ||
                    payload.SaleItemLinks.Select(x => x.OrderLineId).Distinct().Count() !=
                        payload.SaleItemLinks.Count)
                {
                    return "Completion SaleItem/OrderLine correlations must be non-empty and unique.";
                }
                break;
        }

        return null;
    }

    private static string? ValidateRevision(CafeOrderOperationV1 operation)
    {
        CafeOrderRevisionSnapshotV1 revision = operation.Revision!;

        if (revision.RevisionId == Guid.Empty ||
            revision.RevisionNumber <= 0 ||
            revision.OrderVersion != operation.ResultingVersion ||
            revision.ActorUserId != operation.ActorUserId ||
            revision.Lines is null ||
            revision.Lines.Count == 0 ||
            string.IsNullOrWhiteSpace(revision.ContentHash))
        {
            return "Revision identity, version, actor, lines and content hash are required.";
        }

        if (revision.RevisionId != operation.CurrentRevisionId ||
            !string.Equals(
                revision.MenuPublicationRevisionId,
                operation.MenuPublicationRevisionId,
                StringComparison.Ordinal) ||
            !string.Equals(
                revision.ServiceMode,
                operation.ServiceMode,
                StringComparison.Ordinal) ||
            !string.Equals(
                NormalizeOptional(revision.ServiceReference),
                NormalizeOptional(operation.ServiceReference),
                StringComparison.Ordinal))
        {
            return "Revision authority does not match the operation current projection.";
        }

        if (operation.ExpectedVersion == 0)
        {
            if (revision.RevisionNumber != 1 ||
                revision.PreviousRevisionId.HasValue)
            {
                return "Initial revision must be revision 1 without a previous revision.";
            }
        }
        else if (!revision.PreviousRevisionId.HasValue ||
                 revision.PreviousRevisionId.Value == Guid.Empty)
        {
            return "Later revision must preserve previous revision identity.";
        }

        HashSet<Guid> lineIds = new();
        foreach (CafeOrderLineSnapshotV1 line in revision.Lines)
        {
            if (line.OrderLineId == Guid.Empty ||
                line.ProductId == Guid.Empty ||
                !lineIds.Add(line.OrderLineId) ||
                string.IsNullOrWhiteSpace(line.ProductNameSnapshot) ||
                string.IsNullOrWhiteSpace(line.UnitNameSnapshot) ||
                line.QuantityX1000 <= 0 ||
                line.BaseUnitPriceCents < 0 ||
                line.CalculatedUnitPriceCents < 0 ||
                line.LineTotalCents < 0)
            {
                return "Order revision line identities, snapshots, quantities and prices are invalid.";
            }

            bool noVariant =
                line.VariantId is null &&
                line.VariantNameSnapshot is null &&
                !line.VariantUnitPriceCents.HasValue;
            bool withVariant =
                !string.IsNullOrWhiteSpace(line.VariantId) &&
                !string.IsNullOrWhiteSpace(line.VariantNameSnapshot) &&
                line.VariantUnitPriceCents.HasValue &&
                line.VariantUnitPriceCents.Value >= 0;
            if (!noVariant && !withVariant)
            {
                return "Variant snapshot must be fully absent or fully populated.";
            }

            IReadOnlyList<CafeOrderModifierSnapshotV1> modifiers =
                line.ModifierSelections ?? Array.Empty<CafeOrderModifierSnapshotV1>();
            HashSet<string> options = new(StringComparer.Ordinal);
            foreach (CafeOrderModifierSnapshotV1 modifier in modifiers)
            {
                if (string.IsNullOrWhiteSpace(modifier.GroupId) ||
                    string.IsNullOrWhiteSpace(modifier.GroupNameSnapshot) ||
                    string.IsNullOrWhiteSpace(modifier.OptionId) ||
                    string.IsNullOrWhiteSpace(modifier.OptionNameSnapshot) ||
                    !options.Add(modifier.OptionId))
                {
                    return "Modifier snapshots require stable unique group/option identities.";
                }
            }

            long expectedUnit = checked(
                (line.VariantUnitPriceCents ?? line.BaseUnitPriceCents) +
                modifiers.Sum(x => x.PriceDeltaCents));
            if (expectedUnit < 0 ||
                expectedUnit != line.CalculatedUnitPriceCents)
            {
                return "Calculated order unit price does not match bound variant/item plus modifier snapshots.";
            }

            long expectedTotal = checked((long)Math.Round(
                expectedUnit * (line.QuantityX1000 / 1000m),
                0,
                MidpointRounding.AwayFromZero));
            if (expectedTotal != line.LineTotalCents)
            {
                return "Order line total does not match canonical quantity and unit price.";
            }
        }

        return null;
    }

    private static bool Transition(
        CafeOrderOperationV1 payload,
        string action,
        string from,
        string to) =>
        string.Equals(payload.Action, action, StringComparison.Ordinal) &&
        string.Equals(payload.FromState, from, StringComparison.Ordinal) &&
        string.Equals(payload.ToState, to, StringComparison.Ordinal);

    private static bool IsState(string? value) =>
        value is "Open" or "Parked" or "Submitted" or "Preparing" or "Ready" or "Completed" or "Cancelled";

    private static bool IsServiceMode(string? value) =>
        value is "DineIn" or "Takeout";

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CafeOrderOperationV1 Normalize(CafeOrderOperationV1 payload)
    {
        CafeOrderRevisionSnapshotV1? revision = payload.Revision is null
            ? null
            : payload.Revision with
            {
                CreatedAtUtc = payload.Revision.CreatedAtUtc.ToUniversalTime(),
                ServiceReference = NormalizeOptional(payload.Revision.ServiceReference),
                Lines = payload.Revision.Lines
                    .Select(line => line with
                    {
                        Note = NormalizeOptional(line.Note),
                        ModifierSelections = (line.ModifierSelections ?? Array.Empty<CafeOrderModifierSnapshotV1>())
                            .OrderBy(x => x.DisplayOrder)
                            .ThenBy(x => x.GroupId, StringComparer.Ordinal)
                            .ThenBy(x => x.OptionId, StringComparer.Ordinal)
                            .ToArray()
                    })
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.OrderLineId)
                    .ToArray()
            };

        return payload with
        {
            OccurredAtUtc = payload.OccurredAtUtc.ToUniversalTime(),
            ServiceReference = NormalizeOptional(payload.ServiceReference),
            Reason = NormalizeOptional(payload.Reason),
            ReceiptNumber = NormalizeOptional(payload.ReceiptNumber),
            Revision = revision,
            SaleItemLinks = (payload.SaleItemLinks ?? Array.Empty<CafeOrderSaleItemLinkV1>())
                .OrderBy(x => x.OrderLineId)
                .ThenBy(x => x.SaleItemId)
                .ToArray()
        };
    }
}
