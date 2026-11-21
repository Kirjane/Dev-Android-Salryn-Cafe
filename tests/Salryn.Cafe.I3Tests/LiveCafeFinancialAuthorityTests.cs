using System.Net;
using System.Net.Http.Json;
using Salryn.Api.Local;
using Salryn.Composition.Local;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;
using Salryn.Infrastructure.Paths;
using Salryn.Infrastructure.Data.Services;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class LiveCafeFinancialAuthorityTests
{
    [Theory]
    [InlineData("pos-license", "Cash")]
    [InlineData("ar-license", "AccountsReceivable")]
    [InlineData("ar-license", "2")]
    [InlineData("credit-permission", "AccountsReceivable")]
    [InlineData("credit-permission", "2")]
    public async Task LiveCompletion_DeniesMissingFinancialAuthorityBeforeWrites(string scenario, string method)
    {
        string root = Path.Combine(Path.GetTempPath(), "cafe-live-authority-" + Guid.NewGuid().ToString("N"));
        var (factory, mutation) = CanonicalOrderCompletionFixture.Create(root);
        using var connection = factory.CreateConnection(); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE users SET password_hash=$hash WHERE id=$id;";
        command.Parameters.AddWithValue("$hash", SalrynPasswordHasher.HashPassword("Synthetic-HTTP!2026").Value);
        command.Parameters.AddWithValue("$id", mutation.ActorUserId.ToString("D")); command.ExecuteNonQuery();
        command.CommandText = "SELECT username FROM users WHERE id=$id;";
        string username = (string)command.ExecuteScalar()!;
        command.Parameters.Clear();
        // Synthetic feature decisions exercise the actual enforcement service; no signed license or Release bypass.
        var features = scenario == "pos-license" ? Array.Empty<string>() : scenario == "ar-license" ? new[] {LicenseFeatures.Pos} : LicenseFeatures.AllList;
        var license = new LocalLicenseService(new LocalLicenseFileStore(new(root, "absent-test-license.json")),
            new LocalLicenseValidator(new() {AllowDevelopmentMode = true, DevelopmentFeatures = features}));
        await using var host = await LocalApiApplicationFactory.CreateAsync([], new()
        {
            Composition = SalrynLocalComposition.Create(DbPaths.CreateAtRoot(root)), LicenseService = license,
            Url = "http://127.0.0.1:0", EnableStaticWebAssets = false, WriteDevelopmentDatabasePath = false
        });
        if (scenario == "credit-permission") { command.CommandText = "DELETE FROM role_permissions WHERE permission='Receivables.CreateCreditSale';"; command.ExecuteNonQuery(); }
        await host.StartAsync();
        using var client = new HttpClient(new HttpClientHandler {UseCookies = true}) {BaseAddress = new Uri(host.Urls.Single())};
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login",new {username,password="Synthetic-HTTP!2026"})).StatusCode);
        using var response = await client.PostAsJsonAsync($"/api/cafe/orders/{mutation.OrderId:D}/complete", new
        {
            mutation.CompletionOperationId, mutation.SaleCommittedOperationId, mutation.ExpectedVersion,
            mutation.FinancialDecisionId, mutation.FinancialDecisionOperationId, mutation.CustomerId,
            payments = new[] {new {method,amountCents=1000L}}
        });
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var error = System.Text.Json.JsonDocument.Parse(body);
        if (scenario == "credit-permission") Assert.Contains("permission", body, StringComparison.OrdinalIgnoreCase);
        else Assert.Equal(scenario == "pos-license" ? LicenseFeatures.Pos : LicenseFeatures.ReceivablesUtangLedger,
            error.RootElement.GetProperty("requiredFeature").GetString());
        foreach (string table in new[] {"sales","accounts_receivable_entries","cafe_sale_financial_decisions"})
        { command.CommandText=$"SELECT COUNT(*) FROM {table};"; Assert.Equal(0L,Convert.ToInt64(command.ExecuteScalar())); }
        await host.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Coordinator_RejectsCreditAuthorityRevocationBeforePostingAndReplay(bool alreadyPosted)
    {
        var (factory, mutation) = CanonicalOrderCompletionFixture.Create(Path.Combine(Path.GetTempPath(),"cafe-credit-revoked-"+Guid.NewGuid().ToString("N")));
        var coordinator = new SqliteCafeOrderCompletionCoordinator(factory);
        if (alreadyPosted) Assert.True(coordinator.Complete(mutation).IsSuccess);
        using var db = factory.CreateConnection(); db.Open(); using var command = db.CreateCommand();
        command.CommandText="DELETE FROM role_permissions WHERE permission='Receivables.CreateCreditSale';"; command.ExecuteNonQuery();
        var result=coordinator.Complete(mutation);
        Assert.True(result.IsFailure); Assert.Equal("CafeFinancial.CreditSalePermissionRequired",result.ErrorDetail.Code);
        command.CommandText="SELECT COUNT(*) FROM sales;"; Assert.Equal(alreadyPosted?1L:0L,Convert.ToInt64(command.ExecuteScalar()));
    }
}
