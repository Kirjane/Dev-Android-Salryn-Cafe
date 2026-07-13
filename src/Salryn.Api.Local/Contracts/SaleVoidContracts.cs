namespace Salryn.Api.Local.Contracts;

public sealed record SaleVoidRequest(string? Reason);

public sealed record SaleVoidResponse(
    string Code,
    string Message,
    SaleDetailResponse Sale,
    int ReversalMovementCount);
