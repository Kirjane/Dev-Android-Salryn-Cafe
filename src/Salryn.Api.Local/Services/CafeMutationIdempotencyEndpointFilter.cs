using System.Collections;
using System.Reflection;
using Salryn.Core.Services.Licensing;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Repositories.System;

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

        // Revalidate the database authority before either executing or replaying.
        // Local sessions snapshot permissions at login; without this check, a
        // revoked role or inactive operator can retrieve a cached financial result.
        if (session is not null)
        {
            SalrynLocalComposition composition = httpContext.RequestServices
                .GetRequiredService<SalrynLocalComposition>();
            SqliteUserPermissionReadRepository permissions =
                new(composition.ConnectionFactory);
            var current = await permissions.FindByUserIdAsync(
                    session.UserId, httpContext.RequestAborted)
                .ConfigureAwait(false);

            if (current.IsFailure || current.Value is null ||
                current.Value.Status != UserStatus.Active ||
                session.Status != UserStatus.Active ||
                current.Value.RoleId != session.RoleId ||
                !string.Equals(current.Value.Username, session.Username, StringComparison.OrdinalIgnoreCase))
            {
                sessions!.Logout(session.SessionId);
                return Results.Json(
                    new { code = "Auth.OperatorSessionRevoked", error = "Operator session is no longer authorized." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!new HashSet<string>(session.Permissions, StringComparer.OrdinalIgnoreCase)
                .SetEquals(current.Value.Permissions))
            {
                sessions!.Logout(session.SessionId);
                return Results.Json(
                    new { code = "Auth.OperatorPermissionsChanged", error = "Operator permissions changed; sign in again." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        // Canonical order completion must retain checkout's license boundary before
        // either a new financial write or a cached financial response is returned.
        if (session is not null && httpContext.GetEndpoint()?.Metadata.GetOrderedMetadata<string>()
                .Contains(CafeMutationIdempotencyGuard.CanonicalCompletionMetadata) == true)
        {
            var licensing = httpContext.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
            if (await ApiLicenseGuard.RequireLicensedFeatureAsync(licensing, LicenseFeatures.Pos, httpContext.RequestAborted)
                    .ConfigureAwait(false) is IResult licenseDenied)
                return licenseDenied;
            bool hasAr = context.Arguments.Where(arg => arg is not null && !IsFrameworkArgument(arg))
                .Select(arg => arg!.GetType().GetProperty("Payments")?.GetValue(arg)).OfType<IEnumerable>()
                .SelectMany(payments => payments.Cast<object>())
                .Any(payment => Enum.TryParse<PaymentMethod>(payment.GetType().GetProperty("Method")?.GetValue(payment)?.ToString(),
                    true, out var method) && method == PaymentMethod.AccountsReceivable);
            if (hasAr && LocalApiPermissionGuards.RequirePermission(httpContext, sessions!, LocalApiPermissions.ReceivablesCreateCreditSale)
                    is IResult creditDenied)
                return creditDenied;
            if (hasAr && await ApiLicenseGuard.RequireLicensedFeatureAsync(licensing, LicenseFeatures.ReceivablesUtangLedger,
                    httpContext.RequestAborted).ConfigureAwait(false) is IResult arLicenseDenied)
                return arLicenseDenied;
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
