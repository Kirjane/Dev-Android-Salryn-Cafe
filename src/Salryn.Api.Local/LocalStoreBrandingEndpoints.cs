using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local;

/// <summary>
/// PRE-BETA-C: store branding/logo for Host shell and safe authenticated reads.
/// Logo upload is Settings.Manage only. Branding read is any authenticated session.
/// </summary>
internal static class LocalStoreBrandingEndpoints
{
    public const int MaxLogoBytes = 512 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp"
    };

    public static WebApplication MapLocalStoreBrandingEndpoints(this WebApplication app, SalrynLocalComposition composition)
    {
        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);
        IStoreSettingsReadRepository reader = repositories.CreateStoreSettingsReadRepository();
        IStoreSettingsWriteRepository writer = repositories.CreateStoreSettingsWriteRepository();

        app.MapGet("/api/branding/store", async (HttpContext context, LocalApiSessionService sessions, CancellationToken ct) =>
        {
            if (ApiAuthExtensions.RequireAuthenticatedSession(context, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<StoreSettingsReadModel?> result = await reader.GetCurrentAsync(ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Results.BadRequest(new { code = result.ErrorDetail.Code, error = result.ErrorDetail.Message });
            }

            StoreSettingsReadModel? s = result.Value;
            if (s is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                storeName = s.StoreName,
                hasLogo = s.HasLogo,
                logoContentType = s.LogoContentType,
                logoUrl = s.HasLogo ? "/api/branding/store/logo" : null
            });
        });

        app.MapGet("/api/branding/store/logo", async (HttpContext context, LocalApiSessionService sessions, CancellationToken ct) =>
        {
            if (ApiAuthExtensions.RequireAuthenticatedSession(context, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<StoreLogoReadModel?> logo = await reader.GetLogoAsync(ct).ConfigureAwait(false);
            if (logo.IsFailure)
            {
                return Results.BadRequest(new { code = logo.ErrorDetail.Code, error = logo.ErrorDetail.Message });
            }

            if (logo.Value is null)
            {
                return Results.NotFound();
            }

            return Results.File(logo.Value.Bytes, logo.Value.ContentType);
        });

        app.MapPost("/api/settings/store/logo", async (HttpContext context, LocalApiSessionService sessions, CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.SettingsManage) is IResult denied)
            {
                return denied;
            }

            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new { code = "StoreSettings.LogoFormRequired", error = "Upload the logo as multipart form data with a file field named logo." });
            }

            IFormCollection form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
            IFormFile? file = form.Files.GetFile("logo") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length <= 0)
            {
                return Results.BadRequest(new { code = "StoreSettings.LogoRequired", error = "Logo file is required." });
            }

            if (file.Length > MaxLogoBytes)
            {
                return Results.BadRequest(new { code = "StoreSettings.LogoTooLarge", error = "Logo must be 512 KB or smaller." });
            }

            string contentType = (file.ContentType ?? string.Empty).Trim();
            if (string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "image/jpeg";
            }

            if (!AllowedContentTypes.Contains(contentType) ||
                contentType.Contains("svg", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { code = "StoreSettings.LogoTypeRejected", error = "Logo must be PNG, JPEG, or WebP. SVG is not accepted." });
            }

            await using Stream stream = file.OpenReadStream();
            using MemoryStream buffer = new();
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            byte[] bytes = buffer.ToArray();
            if (bytes.Length == 0)
            {
                return Results.BadRequest(new { code = "StoreSettings.LogoRequired", error = "Logo file is empty." });
            }

            Result<StoreSettingsReadModel?> current = await reader.GetCurrentAsync(ct).ConfigureAwait(false);
            if (current.IsFailure || current.Value is null)
            {
                return Results.NotFound();
            }

            Result set = await writer.SetLogoAsync(current.Value.Id, contentType, bytes, ct).ConfigureAwait(false);
            return set.IsSuccess
                ? Results.Ok(new { hasLogo = true, logoContentType = contentType, logoUrl = "/api/branding/store/logo" })
                : Results.BadRequest(new { code = set.ErrorDetail.Code, error = set.ErrorDetail.Message });
        });

        app.MapDelete("/api/settings/store/logo", async (HttpContext context, LocalApiSessionService sessions, CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.SettingsManage) is IResult denied)
            {
                return denied;
            }

            Result<StoreSettingsReadModel?> current = await reader.GetCurrentAsync(ct).ConfigureAwait(false);
            if (current.IsFailure || current.Value is null)
            {
                return Results.NotFound();
            }

            Result clear = await writer.ClearLogoAsync(current.Value.Id, ct).ConfigureAwait(false);
            return clear.IsSuccess
                ? Results.NoContent()
                : Results.BadRequest(new { code = clear.ErrorDetail.Code, error = clear.ErrorDetail.Message });
        });

        return app;
    }
}
