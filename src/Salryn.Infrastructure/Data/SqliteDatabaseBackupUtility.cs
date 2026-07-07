using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data;

internal static class SqliteDatabaseBackupUtility
{
    public static void CreateOnlineBackup(string sourceDatabasePath, string destinationDatabasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDatabasePath);

        if (!File.Exists(sourceDatabasePath))
        {
            throw new FileNotFoundException("Source database file was not found.", sourceDatabasePath);
        }

        if (File.Exists(destinationDatabasePath))
        {
            throw new IOException("Backup destination already exists.");
        }

        string? destinationDirectory = Path.GetDirectoryName(destinationDatabasePath);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        SqliteConnectionFactory.ClearAllPools();
        RemoveWalShmSidecars(destinationDatabasePath);

        SqliteConnectionStringBuilder sourceBuilder = new()
        {
            DataSource = sourceDatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
        };

        SqliteConnectionStringBuilder destinationBuilder = new()
        {
            DataSource = destinationDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        };

        using SqliteConnection sourceConnection = new(sourceBuilder.ToString());
        using SqliteConnection destinationConnection = new(destinationBuilder.ToString());

        sourceConnection.Open();
        destinationConnection.Open();

        SqlitePragma.Apply(sourceConnection);
        sourceConnection.BackupDatabase(destinationConnection);
    }

    public static void RemoveWalShmSidecars(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        foreach (string suffix in new[] { "-wal", "-shm" })
        {
            string sidecarPath = databasePath + suffix;

            if (File.Exists(sidecarPath))
            {
                File.Delete(sidecarPath);
            }
        }
    }
}