namespace Salryn.Api.Local.Auth;

internal static class LocalApiPermissions
{
    public const string ProductsView = "Products.View";
    public const string ProductsManage = "Products.Manage";
    public const string InventoryView = "Inventory.View";
    public const string SalesViewHistory = "Sales.ViewHistory";
    public const string UsersManage = "Users.Manage";
    public const string AuditView = "Audit.View";
    public const string StoreTerminalView = "StoreTerminal.View";
    public const string StoreTerminalManage = "StoreTerminal.Manage";
    public const string SettingsManage = "Settings.Manage";
    public const string ShiftOpenClose = "Shift.OpenClose";
    public const string SalesCreate = "Sales.Create";
}

internal static class LocalApiPermissionGuards
{
    public static IResult? RequireAuthenticatedSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
        {
            return localOnly;
        }

        return GetSession(httpContext, sessions) is null
            ? AuthenticationRequired()
            : null;
    }

    public static IResult? RequirePermission(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string permission)
    {
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
        {
            return localOnly;
        }

        LocalApiSession? session = GetSession(httpContext, sessions);

        if (session is null)
        {
            return AuthenticationRequired();
        }

        return session.Permissions.Contains(permission, StringComparer.Ordinal)
            ? null
            : PermissionDenied(permission);
    }

    public static IResult? RequireAnyPermission(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        IReadOnlyCollection<string> permissions)
    {
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
        {
            return localOnly;
        }

        LocalApiSession? session = GetSession(httpContext, sessions);

        if (session is null)
        {
            return AuthenticationRequired();
        }

        return permissions.Any(permission => session.Permissions.Contains(permission, StringComparer.Ordinal))
            ? null
            : PermissionDenied(string.Join(", ", permissions));
    }

    private static LocalApiSession? GetSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
        return sessions.GetRequiredSession(sessionId);
    }

    private static IResult AuthenticationRequired()
    {
        return Results.Json(
            new
            {
                error = "Authentication is required.",
                code = "Auth.AuthenticationRequired"
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static IResult PermissionDenied(string requiredPermission)
    {
        return Results.Json(
            new
            {
                error = "Permission denied.",
                code = "Auth.PermissionDenied",
                requiredPermission
            },
            statusCode: StatusCodes.Status403Forbidden);
    }
}
