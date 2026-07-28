using Microsoft.Data.Sqlite;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class U1AccountsReceivableLedgerChecks
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-u1-ar-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            paths.EnsureDirectories();
            var initialization = DefaultDatabaseInitializationFactory.Create(paths);
            var initialized = initialization.InitializeDatabase();
            Require(initialized.IsSuccess, "Isolated U1 database initialization must succeed.");
            Guid userId = SeedUser(paths.DatabasePath);
            SqliteAccountsReceivableService service = new(new SqliteConnectionFactory(paths));

            var customerResult = await service.CreateCustomerAsync(new(
                "Maria Santos", "09171234567", null, "Sari-sari account", null, userId));
            Require(customerResult.IsSuccess, "Customer creation must succeed.");
            Guid customerId = customerResult.Value.Id;

            var firstDebit = await service.AdjustAsync(new(customerId, 12_500, true, "Utang sale A", userId));
            var secondDebit = await service.AdjustAsync(new(customerId, 7_500, true, "Utang sale B", userId));
            Require(firstDebit.IsSuccess && secondDebit.IsSuccess, "Recurring Utang debits must succeed.");
            Require(secondDebit.Value.BalanceAfterCents == 20_000, "Recurring Utang must accumulate.");

            var collection = await service.CollectAsync(new(customerId, 5_000, "Cash", "Partial collection", userId));
            Require(collection.IsSuccess, "Collection must succeed.");
            var ledger = await service.GetLedgerAsync(customerId);
            Require(ledger.IsSuccess && ledger.Value.Count == 3, "Ledger history must remain append-only.");
            Require(ledger.Value[^1].BalanceAfterCents == 15_000, "Collection must reduce the balance.");
            Require(Count(paths.DatabasePath, "sales") == 0, "Collection must not create a second sale.");

            string saleRepository = ReadRepo("src", "Salryn.Infrastructure", "Data", "Repositories", "Sales", "SqliteSaleWriteRepository.cs");
            Require(saleRepository.Contains("InsertReceivableDebit(connection, transaction, sale)", StringComparison.Ordinal),
                "AR debit must participate in the sale transaction.");
            Require(saleRepository.Contains("ApplyStockMovement", StringComparison.Ordinal),
                "Utang checkout must retain immediate stock movement.");

            string remoteEndpoints = ReadRepo("src", "Salryn.Api.Local", "RemotePos", "RemotePosApiEndpoints.cs");
            Require(!remoteEndpoints.Contains("/api/receivables", StringComparison.Ordinal),
                "Terminal endpoints must not expose receivables in U1.");

            Console.WriteLine("U1 AR: recurring ledger, collection/no-second-sale, atomic source boundary, and terminal denial PASS.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Guid SeedUser(string databasePath)
    {
        Guid id = Guid.NewGuid();
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO users (id,username,display_name,password_hash,role_id,status,created_at)
            VALUES ($id,'u1-smoke','U1 Smoke','not-used',
                    '00000000-0000-0000-0000-000000000001',1,$at);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return id;
    }

    private static long Count(string databasePath, string table)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ReadRepo(params string[] parts)
    {
        string? root = Directory.GetCurrentDirectory();
        while (root is not null && !File.Exists(Path.Combine(root, "Salryn.sln"))) root = Directory.GetParent(root)?.FullName;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
