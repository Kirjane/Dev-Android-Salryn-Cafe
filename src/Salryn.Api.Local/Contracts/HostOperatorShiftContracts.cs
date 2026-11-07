namespace Salryn.Api.Local.Contracts;

public sealed record HostOperatorShiftOpenRequest(long OpeningCashCents);
public sealed record HostOperatorShiftCloseRequest(long CountedCashCents,bool PhysicalCountConfirmed,string ClientRequestId,string? Note);

public sealed record HostOperatorShiftResponse(
    string ShiftState,
    string? ShiftId,
    string? OpenedAtUtc,
    long? OpeningCashCents,
    string Message,
    long? ExpectedCashCents=null,
    long? CountedCashCents=null,
    long? VarianceCents=null,
    string? CoverageStatus=null,
    string? ReviewStatus=null);
