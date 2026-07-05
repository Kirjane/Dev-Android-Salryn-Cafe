using Salryn.Api.Local.Bridge;

namespace Salryn.Api.Local.RemotePos;

/// <summary>
/// LAN-M16 host-owned remote POS command surface at <c>/api/remote-pos</c>. GET is used for
/// status/capabilities/session/cart/shift reads; POST is used for explicit remote POS commands.
/// This is a SEPARATE surface from the read-only bridge (<c>/api/remote-readonly</c>), which
/// remains GET-only and read-only. Every command validates: header-bound remote bridge, terminal
/// id, handshake proof, host-issued operator session, role/permission matrix, and (for writes)
/// an idempotency key. All durable writes execute on the host.
/// </summary>
public static class RemotePosApiEndpoints
{
    public static WebApplication MapRemotePosEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/remote-pos");

        // ---- Pairing gate -------------------------------------------------------------------

        group.MapGet("/pairing/status", (
            string? requestId,
            RemotePosApiServices services) => ToPairingResult(services.PairingGate.GetRemoteStatus(requestId)));

        group.MapPost("/pairing/request", (
            RemotePosJoinRequest request,
            HttpContext httpContext,
            RemotePosApiServices services) =>
        {
            RemoteReadOnlyBridgeRequestContext bridge = RemoteReadOnlyBridgeRequestContext.FromHttpContext(httpContext);
            return ToPairingResult(services.PairingGate.RequestJoin(request, bridge));
        });

        group.MapPost("/pairing/activate", async (
            RemotePosPairingActivationRequest request,
            HttpContext httpContext,
            RemotePosApiServices services,
            CancellationToken cancellationToken) =>
        {
            RemoteReadOnlyBridgeRequestContext bridge = RemoteReadOnlyBridgeRequestContext.FromHttpContext(httpContext);
            Result<RemotePosPairingActivationResponse> result = await services.PairingGate
                .ActivateAsync(request, bridge, cancellationToken)
                .ConfigureAwait(false);

            return ToPairingResult(result);
        });

        // ---- Session -----------------------------------------------------------------------

        group.MapPost("/session/login", async (
            RemotePosLoginRequest request,
            HttpContext httpContext,
            RemotePosApiServices services,
            CancellationToken cancellationToken) =>
        {
            if (RemotePosApiAuthorization.RequireRemoteBridge(httpContext) is IResult rejected)
            {
                return rejected;
            }

            RemotePosTrustContext trust = RemotePosApiAuthorization.BuildTrustContext(httpContext);
            RemotePosLoginOutcome outcome = await services.SessionService
                .LoginAsync(trust, request, cancellationToken)
                .ConfigureAwait(false);

            if (!outcome.Ok || outcome.Session is null || outcome.Token is null)
            {
                return RemotePosApiAuthorization.Rejected(outcome.Code, outcome.Message, StatusForLogin(outcome.State), outcome.State);
            }

            return Results.Ok(ToSessionResponse(services, outcome.Session, outcome.Token));
        });

        group.MapPost("/session/logout", async (
            HttpContext httpContext,
            RemotePosApiServices services,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemotePosOperatorSession? session) = await RemotePosApiAuthorization
                .AuthorizeCommandAsync(httpContext, services, RemotePosCommands.Logout, cancellationToken)
                .ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            services.SessionService.Revoke(session!.SessionId);
            return Results.Ok(new { status = "logged_out" });
        });

        group.MapGet("/session", async (
            HttpContext httpContext,
            RemotePosApiServices services,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemotePosOperatorSession? session) = await RemotePosApiAuthorization
                .AuthorizeCommandAsync(httpContext, services, RemotePosCommands.SessionRead, cancellationToken)
                .ConfigureAwait(false);

            return rejected ?? Results.Ok(ToSessionResponse(services, session!, token: null));
        });

        group.MapGet("/capabilities", async (
            HttpContext httpContext,
            RemotePosApiServices services,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemotePosOperatorSession? session) = await RemotePosApiAuthorization
                .AuthorizeCommandAsync(httpContext, services, RemotePosCommands.Capabilities, cancellationToken)
                .ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            RemotePosCapabilitiesResponse response = new(
                session!.RoleName,
                RemotePosAuthorizationMatrix.BuildCapabilities(session.Permissions),
                RemotePosAuthorizationMatrix.HostLocalOnlyActions(),
                new[]
                {
                    "Terminal trust is necessary but not sufficient; role/permission is also enforced.",
                    "Host-local LAN control, terminal trust management, and settings are never available over LAN.",
                });

            return Results.Ok(response);
        });

        // ---- Cart --------------------------------------------------------------------------

        group.MapPost("/cart/start", (HttpContext httpContext, RemotePosApiServices services, CancellationToken cancellationToken) =>
            ExecuteCartAsync(httpContext, services, RemotePosCommands.CartStart, cancellationToken,
                (svc, session) => Task.FromResult(new RemotePosCartOperationResult(true, "RemotePos.Cart.Ok", "New cart.", svc.CheckoutService.StartCart(session)))));

        group.MapGet("/cart", (HttpContext httpContext, RemotePosApiServices services, CancellationToken cancellationToken) =>
            ExecuteCartAsync(httpContext, services, RemotePosCommands.CartView, cancellationToken,
                (svc, session) => Task.FromResult(new RemotePosCartOperationResult(true, "RemotePos.Cart.Ok", "Current cart.", svc.CheckoutService.ViewCart(session)))));

        group.MapPost("/cart/add-line", (RemotePosCartAddLineRequest request, HttpContext httpContext, RemotePosApiServices services, CancellationToken cancellationToken) =>
            ExecuteCartAsync(httpContext, services, RemotePosCommands.CartAddLine, cancellationToken,
                (svc, session) => svc.CheckoutService.AddLineAsync(session, request.ProductId, request.QuantityX1000, cancellationToken)));

        group.MapPost("/cart/update-line", (RemotePosCartUpdateLineRequest request, HttpContext httpContext, RemotePosApiServices services, CancellationToken cancellationToken) =>
            ExecuteCartAsync(httpContext, services, RemotePosCommands.CartUpdateLine, cancellationToken,
                (svc, session) => svc.CheckoutService.UpdateLineAsync(session, request.ProductId, request.QuantityX1000, cancellationToken)));

        group.MapPost("/cart/remove-line", (RemotePosCartRemoveLineRequest request, HttpContext httpContext, RemotePosApiServices services, CancellationToken cancellationToken) =>
            ExecuteCartAsync(httpContext, services, RemotePosCommands.CartRemoveLine, cancellationToken,
                (svc, session) => Task.FromResult(svc.CheckoutService.RemoveLine(session, request.ProductId))));

        group.MapPost("/cart/clear", (HttpContext httpContext, RemotePosApiServices services, CancellationToken cancellationToken) =>
            ExecuteCartAsync(httpContext, services, RemotePosCommands.CartClear, cancellationToken,
                (svc, session) => Task.FromResult(svc.CheckoutService.ClearCart(session))));

        // ---- Shift -------------------------------------------------------------------------

        group.MapPost("/shift/open", (RemotePosShiftOpenRequest request, HttpContext httpContext, RemotePosApiServices services, CancellationToken cancellationToken) =>
            ExecuteShiftAsync(httpContext, services, RemotePosCommands.ShiftOpen, cancellationToken,
                (svc, session) => svc.CheckoutService.OpenShiftAsync(session, request.OpeningCashCents, cancellationToken)));

        group.MapPost("/shift/close", (HttpContext httpContext, RemotePosApiServices services, CancellationToken cancellationToken) =>
            ExecuteShiftAsync(httpContext, services, RemotePosCommands.ShiftClose, cancellationToken,
                (svc, session) => svc.CheckoutService.CloseShiftAsync(session, cancellationToken)));

        group.MapGet("/shift", (HttpContext httpContext, RemotePosApiServices services, CancellationToken cancellationToken) =>
            ExecuteShiftAsync(httpContext, services, RemotePosCommands.ShiftRead, cancellationToken,
                (svc, session) => svc.CheckoutService.ReadShiftAsync(session, cancellationToken)));

        // ---- Checkout ----------------------------------------------------------------------

        group.MapPost("/checkout/submit", async (
            RemotePosCheckoutSubmitRequest request,
            HttpContext httpContext,
            RemotePosApiServices services,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemotePosOperatorSession? session) = await RemotePosApiAuthorization
                .AuthorizeCommandAsync(httpContext, services, RemotePosCommands.CheckoutSubmit, cancellationToken)
                .ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            RemotePosCheckoutResultResponse result = await services.CheckoutService
                .SubmitAsync(session!, request, cancellationToken)
                .ConfigureAwait(false);

            return Results.Json(result, statusCode: StatusForCheckout(result.Status));
        });

        group.MapGet("/checkout/status/{clientRequestId}", async (
            string clientRequestId,
            HttpContext httpContext,
            RemotePosApiServices services,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemotePosOperatorSession? session) = await RemotePosApiAuthorization
                .AuthorizeCommandAsync(httpContext, services, RemotePosCommands.CheckoutStatus, cancellationToken)
                .ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            RemotePosCheckoutResultResponse? result = services.CheckoutService.FindCheckoutResult(session!, clientRequestId);

            if (result is null)
            {
                return Results.Json(
                    new RemotePosCheckoutResultResponse(
                        RemotePosCheckoutStatus.NotFound, clientRequestId, null, null, null, null, false,
                        "No checkout result found for this client request id."),
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(result);
        });

        // ---- Manager / Admin ---------------------------------------------------------------

        // Manager overrides have no safe remote domain path; the matrix returns NotSupported so
        // AuthorizeCommandAsync rejects with an explicit, sanitized code before any handler runs.
        group.MapPost("/manager/override", async (
            RemotePosManagerOverrideRequest request,
            HttpContext httpContext,
            RemotePosApiServices services,
            CancellationToken cancellationToken) =>
        {
            _ = request;
            (IResult? rejected, RemotePosOperatorSession? session) = await RemotePosApiAuthorization
                .AuthorizeCommandAsync(httpContext, services, RemotePosCommands.ManagerOverride, cancellationToken)
                .ConfigureAwait(false);

            return rejected ?? Results.Ok(new { status = "not_supported", session = session!.SessionId });
        });

        group.MapGet("/admin/status", async (
            HttpContext httpContext,
            RemotePosApiServices services,
            CancellationToken cancellationToken) =>
        {
            (IResult? rejected, RemotePosOperatorSession? session) = await RemotePosApiAuthorization
                .AuthorizeCommandAsync(httpContext, services, RemotePosCommands.AdminStatus, cancellationToken)
                .ConfigureAwait(false);

            if (rejected is not null)
            {
                return rejected;
            }

            return Results.Ok(new
            {
                role = session!.RoleName,
                remotePosRuntimeEnabled = services.RuntimeGate.IsEnabled,
                hostLocalOnly = RemotePosAuthorizationMatrix.HostLocalOnlyActions(),
                message = "Remote admin status is read-only. Host-local LAN control is never available over LAN.",
            });
        });

        return app;
    }

    private static async Task<IResult> ExecuteCartAsync(
        HttpContext httpContext,
        RemotePosApiServices services,
        string command,
        CancellationToken cancellationToken,
        Func<RemotePosApiServices, RemotePosOperatorSession, Task<RemotePosCartOperationResult>> action)
    {
        (IResult? rejected, RemotePosOperatorSession? session) = await RemotePosApiAuthorization
            .AuthorizeCommandAsync(httpContext, services, command, cancellationToken)
            .ConfigureAwait(false);

        if (rejected is not null)
        {
            return rejected;
        }

        RemotePosCartOperationResult result = await action(services, session!).ConfigureAwait(false);

        if (!result.Ok || result.Cart is null)
        {
            int status = result.Code.Contains("ProductUnavailable", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;

            return Results.Json(new { error = result.Message, code = result.Code }, statusCode: status);
        }

        return Results.Ok(result.Cart);
    }

    private static async Task<IResult> ExecuteShiftAsync(
        HttpContext httpContext,
        RemotePosApiServices services,
        string command,
        CancellationToken cancellationToken,
        Func<RemotePosApiServices, RemotePosOperatorSession, Task<RemotePosShiftOperationResult>> action)
    {
        (IResult? rejected, RemotePosOperatorSession? session) = await RemotePosApiAuthorization
            .AuthorizeCommandAsync(httpContext, services, command, cancellationToken)
            .ConfigureAwait(false);

        if (rejected is not null)
        {
            return rejected;
        }

        RemotePosShiftOperationResult result = await action(services, session!).ConfigureAwait(false);

        if (!result.Ok || result.Shift is null)
        {
            return Results.Json(new { error = result.Message, code = result.Code }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(result.Shift);
    }

    private static IResult ToPairingResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        int status = result.ErrorDetail.Code.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status404NotFound
            : result.ErrorDetail.Type.ToString().Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status400BadRequest;

        return Results.Json(new { error = result.ErrorDetail.Message, code = result.ErrorDetail.Code }, statusCode: status);
    }

    private static RemotePosSessionResponse ToSessionResponse(
        RemotePosApiServices services,
        RemotePosOperatorSession session,
        string? token) =>
        new(
            RemotePosSessionState.Active,
            session.SessionId.ToString(),
            token,
            session.TerminalId.ToString(),
            session.HostId.ToString(),
            session.StoreId.ToString(),
            session.UserId.ToString(),
            session.Username,
            session.DisplayName,
            session.RoleName,
            session.Permissions,
            session.IssuedAtUtc.ToString("O"),
            session.ExpiresAtUtc.ToString("O"),
            services.RuntimeGate.IsEnabled);

    private static int StatusForLogin(string state) => state switch
    {
        RemotePosSessionState.AuthenticationFailed => StatusCodes.Status401Unauthorized,
        RemotePosSessionState.RemotePosDisabled => StatusCodes.Status403Forbidden,
        RemotePosSessionState.TrustRejected => StatusCodes.Status403Forbidden,
        RemotePosSessionState.RoleNotAllowed => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status403Forbidden,
    };

    private static int StatusForCheckout(string status) => status switch
    {
        RemotePosCheckoutStatus.Completed => StatusCodes.Status200OK,
        RemotePosCheckoutStatus.DuplicateRequest => StatusCodes.Status200OK,
        RemotePosCheckoutStatus.StockChanged => StatusCodes.Status409Conflict,
        RemotePosCheckoutStatus.ProductUnavailable => StatusCodes.Status409Conflict,
        RemotePosCheckoutStatus.OpenShiftRequired => StatusCodes.Status409Conflict,
        RemotePosCheckoutStatus.PriceChanged => StatusCodes.Status409Conflict,
        RemotePosCheckoutStatus.PaymentInsufficient => StatusCodes.Status400BadRequest,
        RemotePosCheckoutStatus.ValidationFailed => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
