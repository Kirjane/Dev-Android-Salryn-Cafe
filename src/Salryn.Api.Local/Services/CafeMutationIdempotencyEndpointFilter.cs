using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;

namespace Salryn.Api.Local.Services;

/// <summary>
/// Endpoint filter applied to all C1-C11 write endpoints (<c>/api/menu/*</c>, <c>/api/cafe/*</c>).
/// Enforces fail-closed client <c>operationId</c>, per-key serialization, exact payload-hash replay,
/// and <c>409 ERR_IDEMPOTENCY_CONFLICT</c> backed by <see cref="CafeMutationIdempotencyGuard"/>.
/// </summary>
internal sealed class CafeMutationIdempotencyEndpointFilter : IEndpointFilter
{
    public const string ResolvedOperationIdItemKey = "Salryn.Cafe.ResolvedOperationId";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        HttpContext httpContext = context.HttpContext;
        string method = httpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return await next(context).ConfigureAwait(false);
        }

        // A cached replay must not bypass the local-operator transport boundary.
        // Individual endpoint permission guards still govern each protected action.
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult deniedTransport)
        {
            return deniedTransport;
        }

        // Let unauthenticated requests fall through to endpoint auth guards (401/403) without recording idempotency.
        LocalApiSessionService? sessions = httpContext.RequestServices.GetService<LocalApiSessionService>();
        LocalApiSession? session = null;
        if (sessions is not null)
        {
            string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
            session = sessions.GetRequiredSession(sessionId);
            if (session is null)
            {
                return await next(context).ConfigureAwait(false);
            }
        }

        Guid actorUserId = session?.UserId ?? Guid.Empty;
        Guid? bodyOperationId = ExtractOperationIdFromArguments(context.Arguments);

        if (!CafeMutationIdempotencyGuard.TryResolveOperationId(httpContext, bodyOperationId, out Guid operationId))
        {
            return CafeMutationIdempotencyGuard.OperationIdRequiredResult();
        }

        httpContext.Items[ResolvedOperationIdItemKey] = operationId;

        // Missing registration is a composition failure, never permission to execute
        // an unguarded mutation or create a separate per-request concurrency gate.
        CafeMutationIdempotencyGuard guard = httpContext.RequestServices.GetRequiredService<CafeMutationIdempotencyGuard>();

        string routePattern = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
            ?? httpContext.Request.Path.Value
            ?? string.Empty;
        string endpointKey = $"{method.ToUpperInvariant()} {routePattern}";

        object canonicalPayload = BuildCanonicalPayload(httpContext, context.Arguments);

        return await guard.ExecuteAsync(
            httpContext,
            endpointKey,
            actorUserId,
            operationId,
            canonicalPayload,
            async _ =>
            {
                object? rawResult = await next(context).ConfigureAwait(false);
                return ExtractOutcome(rawResult);
            },
            httpContext.RequestAborted).ConfigureAwait(false);
    }

    private static Guid? ExtractOperationIdFromArguments(IList<object?> arguments)
    {
        foreach (object? arg in arguments)
        {
            if (arg is null || IsFrameworkArgument(arg))
            {
                continue;
            }

            Type type = arg.GetType();
            PropertyInfo? prop = type.GetProperty("OperationId", BindingFlags.Public | BindingFlags.Instance)
                ?? type.GetProperty("CompletionOperationId", BindingFlags.Public | BindingFlags.Instance);

            if (prop is null)
            {
                continue;
            }

            object? val = prop.GetValue(arg);
            if (val is Guid g && g != Guid.Empty)
            {
                return g;
            }
        }

        return null;
    }

    private static object BuildCanonicalPayload(HttpContext httpContext, IList<object?> arguments)
    {
        Dictionary<string, string?> routeValues = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> kv in httpContext.Request.RouteValues)
        {
            routeValues[kv.Key] = kv.Value?.ToString();
        }

        List<object?> payloadArgs = new();
        foreach (object? arg in arguments)
        {
            if (arg is null || IsFrameworkArgument(arg))
            {
                continue;
            }

            payloadArgs.Add(arg);
        }

        return new
        {
            route = routeValues,
            query = httpContext.Request.QueryString.Value ?? string.Empty,
            args = payloadArgs
        };
    }

    private static bool IsFrameworkArgument(object arg)
    {
        return arg is HttpContext
            or CancellationToken
            or LocalApiSessionService
            or SalrynLocalComposition
            or CafeMutationIdempotencyGuard
            or HostOperatorApiServices;
    }

    private static IdempotentExecutionOutcome ExtractOutcome(object? rawResult)
    {
        int statusCode = StatusCodes.Status200OK;
        object? body = rawResult;

        if (rawResult is IStatusCodeHttpResult statusResult && statusResult.StatusCode.HasValue)
        {
            statusCode = statusResult.StatusCode.Value;
        }

        if (rawResult is IValueHttpResult valueResult)
        {
            body = valueResult.Value;
        }
        else if (rawResult is IResult)
        {
            body = new { statusCode };
        }

        return new IdempotentExecutionOutcome(statusCode, body);
    }
}
