using Android.Content;

namespace Salryn.App.Android.Platform;

public sealed class AndroidAppPaths
{
    public AndroidAppPaths(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string filesRoot = context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android app-local files directory is unavailable.");

        RootPath = Path.Combine(filesRoot, "SalrynCafe");
        DatabasePath = Path.Combine(RootPath, "data", "salryn-cafe.db");

        // Preserve the existing P0 identity location so upgrading the launcher does not
        // silently regenerate the public device ID or protected authentication secret.
        string existingIdentityOwner = Path.Combine(filesRoot, "salryn-p0");
        IdentityRoot = Path.Combine(existingIdentityOwner, "identity");
        ProtectedSecretRoot = Path.Combine(existingIdentityOwner, "protected-secrets");
    }

    public string RootPath { get; }

    public string DatabasePath { get; }

    public string IdentityRoot { get; }

    public string ProtectedSecretRoot { get; }
}
