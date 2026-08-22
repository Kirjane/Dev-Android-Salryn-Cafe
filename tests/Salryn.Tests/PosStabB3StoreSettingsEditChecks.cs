namespace Salryn.Tests;

public static class PosStabB3StoreSettingsEditChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string settingsFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Settings",
            "StoreSettingsForm.cs");
        string appServicesText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "SalrynAppServices.cs");
        string writeContractsText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Repositories",
            "WriteRepositoryInterfaces.cs");
        string repositoryFactoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Repositories",
            "SqliteRepositoryFactory.cs");
        string sqliteWriteRepositoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Repositories",
            "System",
            "SqliteStoreSettingsWriteRepository.cs");
        string shellText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Shell",
            "SalrynShellForm.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(settingsFormText, "_editButton", "B3 Store Settings must expose an Edit action.");
        RequireContains(settingsFormText, "_saveButton", "B3 Store Settings must expose a Save action.");
        RequireContains(settingsFormText, "_cancelButton", "B3 Store Settings must expose a Cancel action.");
        RequireContains(settingsFormText, "SetEditMode", "B3 Store Settings must have explicit edit-mode control state.");
        RequireContains(settingsFormText, "SaveAsync", "B3 Store Settings must have a visible save workflow.");
        RequireContains(settingsFormText, "StoreSettingsUpdateRequest", "B3 Store Settings must build a typed update request.");
        RequireContains(settingsFormText, "_services.StoreSettingsWriteRepository.UpdateAsync", "B3 Store Settings must save through AppServices write repository.");
        RequireContains(settingsFormText, "Receipt width must be 58 or 80", "B3 Store Settings must validate sane receipt widths.");
        RequireContains(settingsFormText, "Tax Mode must be Enabled or Disabled.", "B3 Store Settings must validate tax mode input.");
        RequireContains(settingsFormText, "Shift Mode must be Enabled or Disabled.", "B3 Store Settings must validate shift mode input.");

        RequireNoDirectSql(settingsFormText, "Store Settings form");
        RequireNoForbiddenMarkers(settingsFormText, "Store Settings form");

        RequireContains(writeContractsText, "StoreSettingsUpdateRequest", "B3 Core must define a typed store settings update request.");
        RequireContains(writeContractsText, "IStoreSettingsWriteRepository", "B3 Core must define a store settings write repository boundary.");
        RequireContains(writeContractsText, "Task<Result> UpdateAsync(", "B3 Core store settings write repository must expose an update operation.");

        RequireContains(sqliteWriteRepositoryText, "SqliteStoreSettingsWriteRepository", "B3 Infrastructure must implement the store settings write repository.");
        RequireContains(sqliteWriteRepositoryText, "IStoreSettingsWriteRepository", "B3 SQLite store settings repository must implement the Core write boundary.");
        RequireContains(sqliteWriteRepositoryText, "StoreSettings.InvalidReceiptWidth", "B3 store settings write repository must guard receipt width.");
        RequireContains(sqliteWriteRepositoryText, "StoreSettings.StoreNameRequired", "B3 store settings write repository must require store name.");
        RequireContains(sqliteWriteRepositoryText, "StoreSettings.CurrencyRequired", "B3 store settings write repository must require currency label.");
        RequireContains(sqliteWriteRepositoryText, "updated_at = $updatedAt", "B3 store settings write repository must update the existing updated_at field.");

        RequireContains(repositoryFactoryText, "CreateStoreSettingsWriteRepository", "B3 repository factory must expose store settings writer creation.");
        RequireContains(appServicesText, "IStoreSettingsWriteRepository storeSettingsWriteRepository", "B3 AppServices must accept the store settings write repository.");
        RequireContains(appServicesText, "StoreSettingsWriteRepository = storeSettingsWriteRepository;", "B3 AppServices must store the settings write repository.");
        RequireContains(appServicesText, "public IStoreSettingsWriteRepository StoreSettingsWriteRepository { get; }", "B3 AppServices must expose the settings write repository.");
        RequireContains(appServicesText, "repositories.CreateStoreSettingsWriteRepository();", "B3 AppServices must build settings write repository through the repository factory.");

        RequireContains(shellText, "new StoreSettingsForm(_services)", "B3 Shell must keep Store Settings routed through AppServices.");
        RequireContains(coreSmokeText, "PosStabB3StoreSettingsEditChecks.Run();", "CoreSmoke must run POS-STAB-B3 store settings edit checks.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(
        DirectoryInfo repoRoot,
        params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireNoDirectSql(string text, string surface)
    {
        RequireDoesNotContain(text, "Microsoft.Data.Sqlite", $"B3 {surface} must not reference SQLite directly.");
        RequireDoesNotContain(text, "SqliteConnection", $"B3 {surface} must not create SQLite connections.");
        RequireDoesNotContain(text, "INSERT ", $"B3 {surface} must not issue direct insert SQL.");
        RequireDoesNotContain(text, "UPDATE ", $"B3 {surface} must not issue direct update SQL.");
        RequireDoesNotContain(text, "DELETE ", $"B3 {surface} must not issue direct delete SQL.");
    }

    private static void RequireNoForbiddenMarkers(string text, string surface)
    {
        foreach (string marker in ForbiddenMarkers())
        {
            RequireDoesNotContain(text, marker, $"B3 {surface} must not introduce forbidden marker: {marker}");
        }
    }

    private static string[] ForbiddenMarkers()
    {
        return new[]
        {
            "P" + "DF",
            "Cry" + "stal",
            "." + "rpt",
            "ESC" + "/" + "POS",
            "raw " + "USB",
            "scan" + "ner",
            "hard" + "ware"
        };
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
