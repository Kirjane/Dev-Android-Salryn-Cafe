using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

internal static class LocalBackupPackageEndpoints
{
    public static WebApplication MapLocalBackupPackageEndpoints(this WebApplication app, SalrynLocalComposition composition)
    {
        ControlledBackupPackageService service = new(composition.DbPaths);

        async Task<IResult?> GuardAsync(HttpContext c, LocalApiSessionService s, CancellationToken ct)
        {
            IResult? permission = LocalApiPermissionGuards.RequirePermission(c, s, LocalApiPermissions.BackupManage);
            if (permission is not null)
            {
                return permission;
            }

            return await ApiLicenseGuard.RequireLicensedFeatureAsync(LicenseFeatures.Backup, ct).ConfigureAwait(false);
        }

        app.MapPost("/api/backups", async (HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
            await GuardAsync(c, s, ct).ConfigureAwait(false) ?? Map(service.Create()));
        app.MapGet("/api/backups", async (HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
            await GuardAsync(c, s, ct).ConfigureAwait(false) ?? Map(service.List()));
        app.MapPost("/api/backups/{id}/verify", async (string id, HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
            await GuardAsync(c, s, ct).ConfigureAwait(false) ?? Map(service.Verify(id)));
        app.MapPost("/api/backups/{id}/restore", async (string id, RestoreRequest r, HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
            await GuardAsync(c, s, ct).ConfigureAwait(false) ?? Map(service.Restore(id, r.Confirmation)));

        // PRE-BETA-D: portable download of a verified package as .salryn-backup.zip
        app.MapGet("/api/backups/{id}/download", async (string id, HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
        {
            IResult? denied = await GuardAsync(c, s, ct).ConfigureAwait(false);
            if (denied is not null)
            {
                return denied;
            }

            var export = service.ExportPortable(id);
            if (export.IsFailure)
            {
                return Results.BadRequest(new { code = export.ErrorDetail.Code, error = export.ErrorDetail.Message });
            }

            ControlledBackupPortableExport value = export.Value;
            // Stream and delete temp zip after response completes.
            FileStream stream = File.OpenRead(value.ZipPath);
            c.Response.OnCompleted(() =>
            {
                try { stream.Dispose(); } catch { /* ignore */ }
                try { File.Delete(value.ZipPath); } catch { /* ignore */ }
                return Task.CompletedTask;
            });

            return Results.File(stream, value.ContentType, value.FileName);
        });

        // PRE-BETA-D: import portable package (multipart field "package")
        app.MapPost("/api/backups/import", async (HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
        {
            IResult? denied = await GuardAsync(c, s, ct).ConfigureAwait(false);
            if (denied is not null)
            {
                return denied;
            }

            if (!c.Request.HasFormContentType)
            {
                return Results.BadRequest(new
                {
                    code = "BackupPackage.ImportFormRequired",
                    error = "Upload the backup package as multipart form data with a file field named package."
                });
            }

            IFormCollection form = await c.Request.ReadFormAsync(ct).ConfigureAwait(false);
            IFormFile? file = form.Files.GetFile("package") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length <= 0)
            {
                return Results.BadRequest(new
                {
                    code = "BackupPackage.ImportRequired",
                    error = "Backup package file is required."
                });
            }

            if (file.Length > ControlledBackupPackageService.MaxPortableImportBytes)
            {
                return Results.BadRequest(new
                {
                    code = "BackupPackage.ImportTooLarge",
                    error = "Backup package must be 100 MB or smaller."
                });
            }

            await using Stream stream = file.OpenReadStream();
            var imported = service.ImportPortable(stream, file.Length);
            return Map(imported);
        });

        return app;
    }

    private static IResult Map<T>(Salryn.Core.Common.Result<T> r) =>
        r.IsSuccess
            ? Results.Ok(r.Value)
            : Results.BadRequest(new { code = r.ErrorDetail.Code, error = r.ErrorDetail.Message });

    private sealed record RestoreRequest(string Confirmation);
}
