using Salryn.Api.Local.Services;
using Salryn.Composition.Local;

namespace Salryn.Tools.CafeReportingProof;

// Exercises the production adapter and shared SQLite service on disposable data.
// This is in-process API-service coverage, not a live HTTP/browser acceptance claim.
internal static class C11EApiAdapterProof
{
    public static void Run(string root)
    {
        using C11ProofFixture fixture = C11ProofFixture.Create(root);
        OwnerReportsApiService adapter = new(SalrynLocalComposition.Create(fixture.Paths));
        var now = C11ProofFixture.Day.AddHours(23);
        var result = ProofAssert.Value(adapter.GetSummaryAsync(
            fixture.ManagerUserId, C11ProofFixture.Day, C11ProofFixture.Day.AddDays(1), now)
            .GetAwaiter().GetResult(), "authenticated Cafe adapter");
        ProofAssert.That(result.CafeManagement is not null, "C11 report must be returned for the explicit authorized actor.");
        ProofAssert.That(result.Daily.RangeStart.Offset == TimeSpan.FromHours(8) &&
            result.Daily.RangeStart.Hour == 0 && result.Daily.RangeStart.Day == 25,
            "Daily summary must retain Manila midnight even across UTC date boundaries.");
        ProofAssert.That(result.CafeManagement!.RangeStartUtc == C11ProofFixture.Day &&
            result.CafeManagement.RangeEndUtc == C11ProofFixture.Day.AddDays(1),
            "Explicit report range must reach shared C11 service unchanged.");
        ProofAssert.That(result.CafeManagement.AccountsReceivable is not null && result.CafeManagement.Cash is not null,
            "Authorized receivables details must remain available.");
        var redacted = ProofAssert.Value(adapter.GetSummaryAsync(
            fixture.ManagerUserId, nowOverride: now, includeReceivables: false).GetAwaiter().GetResult(), "redacted report");
        ProofAssert.That(redacted.Receivables is null && redacted.CafeManagement!.AccountsReceivable is null &&
            redacted.CafeManagement.Cash is null, "No receivables entitlement must redact both legacy and C11 AR-bearing sections.");
        var denied = adapter.GetSummaryAsync(fixture.CashierUserId, nowOverride: now).GetAwaiter().GetResult();
        ProofAssert.That(denied.IsFailure, "Reports.View must be checked by shared service for the actual actor.");
        var unknown = adapter.GetSummaryAsync(Guid.NewGuid(), nowOverride: now).GetAwaiter().GetResult();
        ProofAssert.That(unknown.IsFailure, "Unknown caller must not borrow another principal.");
        var invalid = adapter.GetSummaryAsync(fixture.ManagerUserId, C11ProofFixture.Day.AddDays(1), C11ProofFixture.Day)
            .GetAwaiter().GetResult();
        ProofAssert.That(invalid.IsFailure && invalid.ErrorDetail.Code == "OwnerReports.RangeInvalid", "Reversed range must fail.");
        Console.WriteLine("C11E API ADAPTER / RBAC / RANGE / REDACTION: PASS (in-process, synthetic)");
    }
}
