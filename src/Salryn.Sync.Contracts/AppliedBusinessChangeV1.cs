namespace Salryn.Sync.Contracts;

public sealed record AppliedBusinessChangeV1(
    Guid CentralInstanceId,
    long ServerSequence,
    Guid ChangeId,
    Guid SourceOperationId,
    Guid OriginDeviceId,
    long OriginLocalSequence,
    Guid StoreId,
    string ChangeType,
    int SchemaVersion,
    Guid AggregateId,
    string PayloadJson,
    string PayloadHash,
    string ChangeHash,
    DateTimeOffset CreatedAtUtc);
