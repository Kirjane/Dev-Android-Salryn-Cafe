using Salryn.Core.Common;
using Salryn.Core.Deployment;

namespace Salryn.Core.Host;

public sealed record HostCommandTraceMetadata(
    Guid StoreId,
    Guid HostId,
    Guid TerminalId,
    Guid UserId,
    DateTimeOffset TimestampUtc,
    string CommandName);

public sealed record SalrynHostCommandEnvelope<TPayload>(
    string CommandName,
    Guid RequestId,
    Guid StoreId,
    Guid HostId,
    Guid TerminalId,
    Guid UserId,
    Guid? SessionId,
    DateTimeOffset TimestampUtc,
    InstallationRole SourceMachineRole,
    InstallationRole InstallationRole,
    TPayload Payload);

public sealed record SalrynHostCommandResult<TResponse>(
    bool Succeeded,
    TResponse? Response,
    string? ErrorCode,
    string? ErrorMessage,
    Guid RequestId,
    HostCommandTraceMetadata? Trace)
{
    public static SalrynHostCommandResult<TResponse> Success(
        TResponse response,
        SalrynHostCommandEnvelope<object?> envelope) =>
        new(
            Succeeded: true,
            Response: response,
            ErrorCode: null,
            ErrorMessage: null,
            RequestId: envelope.RequestId,
            Trace: CreateTrace(envelope));

    public static SalrynHostCommandResult<TResponse> Failure(
        SalrynHostCommandEnvelope<object?> envelope,
        Error error) =>
        new(
            Succeeded: false,
            Response: default,
            ErrorCode: error.Code,
            ErrorMessage: error.Message,
            RequestId: envelope.RequestId,
            Trace: CreateTrace(envelope));

    private static HostCommandTraceMetadata CreateTrace(SalrynHostCommandEnvelope<object?> envelope) =>
        new(
            envelope.StoreId,
            envelope.HostId,
            envelope.TerminalId,
            envelope.UserId,
            envelope.TimestampUtc,
            envelope.CommandName);
}

[Flags]
public enum HostCommandAuthorityRequirements
{
    None = 0,
    Database = 1,
    OfficialSequence = 2,
    Journal = 4,
    Backup = 8
}

public static class SalrynHostCommandEnvelopeValidator
{
    public static Result Validate<TPayload>(SalrynHostCommandEnvelope<TPayload>? envelope, bool payloadRequired = true)
    {
        if (envelope is null)
        {
            return Result.Fail(Error.Validation(
                "HostCommand.EnvelopeRequired",
                "Host command envelope is required."));
        }

        if (string.IsNullOrWhiteSpace(envelope.CommandName))
        {
            return Result.Fail(Error.Validation(
                "HostCommand.CommandNameRequired",
                "Host command name is required."));
        }

        if (envelope.RequestId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "HostCommand.RequestIdRequired",
                "Host command request id is required."));
        }

        if (envelope.StoreId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "HostCommand.StoreIdRequired",
                "Host command store id is required."));
        }

        if (envelope.TerminalId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "HostCommand.TerminalIdRequired",
                "Host command terminal id is required."));
        }

        if (envelope.UserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "HostCommand.UserIdRequired",
                "Host command user id is required."));
        }

        if (envelope.TimestampUtc == default)
        {
            return Result.Fail(Error.Validation(
                "HostCommand.TimestampRequired",
                "Host command timestamp is required."));
        }

        if (payloadRequired && envelope.Payload is null)
        {
            return Result.Fail(Error.Validation(
                "HostCommand.PayloadRequired",
                "Host command payload is required."));
        }

        return Result.Success();
    }
}
