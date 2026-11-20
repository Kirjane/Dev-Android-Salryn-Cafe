namespace Salryn.Sync.Contracts;

public static class SaleCommittedV2Contract
{
    public const string OperationType = "SaleCommittedV2";
    public const int SchemaVersion = 2;

    public const string Applied = SaleCommittedV1Contract.Applied;
    public const string Rejected = SaleCommittedV1Contract.Rejected;
    public const string NeedsReview = SaleCommittedV1Contract.NeedsReview;
}
