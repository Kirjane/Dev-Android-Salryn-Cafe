namespace Salryn.Api.Local.Contracts;

public sealed record HostOperatorShiftOpenRequest(long OpeningCashCents);

public sealed record HostOperatorShiftResponse(
    string ShiftState,
    string? ShiftId,
    string? OpenedAtUtc,
    long? OpeningCashCents,
    string Message);