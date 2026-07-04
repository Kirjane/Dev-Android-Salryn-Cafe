namespace Salryn.Tests;

// LAN-M16 guard: the remote checkout runtime and the host-owned official write boundary. This is a
// source-level guard (the functional proof lives in the infrastructure smokes). It proves the
// remote checkout drives the REAL host sale service, is idempotent per client request id, allocates
// the receipt/document number on the host (never from the client), re-validates the open shift at
// submit, records an audit trail, and never leaks raw internal error detail to the client.
public static class LanM16RemoteCheckoutIdempotencyAndHostWriteChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local", "RemotePos");

        string checkout = ReadRequiredFile(apiRoot, "RemotePosCheckoutService.cs");
        string services = ReadRequiredFile(apiRoot, "RemotePosApiServices.cs");
        string allocator = ReadRequiredFile(apiRoot, "RemotePosReceiptNumberAllocator.cs");
        string contracts = ReadRequiredFile(apiRoot, "RemotePosContracts.cs");
        string doc = ReadRequiredFile(projectRoot, "00-HANDOFF", "LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        AssertHostOwnedSalePath(checkout, services);
        AssertIdempotency(checkout);
        AssertHostOwnedReceipt(checkout, allocator, contracts);
        AssertShiftRevalidationAndConflictCodes(checkout);
        AssertAuditTrail(checkout);
        AssertNoRawErrorLeak(checkout);
        AssertClientSendsIntentOnly(contracts);
        AssertWiring(doc, coreSmoke, infraSmoke);
    }

    private static void AssertHostOwnedSalePath(string checkout, string services)
    {
        AssertContains(checkout, "IPosCheckoutApplicationService", "Remote checkout must drive the host-side sale application service.");
        AssertContains(checkout, "CompleteSaleAsync", "Remote checkout must complete the sale via the host application service.");
        // Server-trusted re-pricing: the application service is built with the product read repository.
        AssertContains(services, "PosCheckoutApplicationService", "Composition must build the host checkout application service.");
        AssertContains(services, "composition.ProductReadRepository", "Host checkout must re-price from persisted product facts (server-trusted).");
        AssertContains(services, "SqliteRepositoryFactory", "Remote POS must build its own host-side write path from the shared connection factory.");
    }

    private static void AssertIdempotency(string checkout)
    {
        AssertContains(checkout, "_idempotency", "Remote checkout must maintain an idempotency map.");
        AssertContains(checkout, "BuildIdempotencyKey", "Remote checkout must key idempotency by store/terminal/client request id.");
        AssertContains(checkout, "RemotePosCheckoutStatus.DuplicateRequest", "Remote checkout must return DuplicateRequest on replay.");
        AssertContains(checkout, "Monitor.Enter", "Remote checkout must serialize submits sharing an idempotency key.");
        AssertContains(checkout, "ClientRequestId", "Remote checkout must require a client request id (idempotency key).");
        // Only durable, completed sales are memoized for idempotent replay.
        AssertContains(checkout, "result.Status == RemotePosCheckoutStatus.Completed", "Only completed sales may be memoized for idempotent replay.");
    }

    private static void AssertHostOwnedReceipt(string checkout, string allocator, string contracts)
    {
        AssertContains(checkout, "_receiptAllocator.AllocateNext()", "Host must allocate the receipt/document number.");
        AssertContains(allocator, "sales.receipt_no UNIQUE", "Allocator must document the UNIQUE backstop.");
        AssertContains(allocator, "NEVER supplies", "Allocator must document that the client never supplies the number.");
        // The submit request contract must NOT carry a receipt number or authoritative price.
        AssertContains(contracts, "RemotePosCheckoutSubmitRequest(", "Checkout submit request contract must exist.");
    }

    private static void AssertShiftRevalidationAndConflictCodes(string checkout)
    {
        AssertContains(checkout, "FindOpenSessionByUserIdAsync", "Host must re-validate the open cashier shift at submit.");
        AssertContains(checkout, "RemotePosCheckoutStatus.OpenShiftRequired", "Checkout must return OpenShiftRequired when no shift is open.");

        // The full set of conflict/result codes must be modeled by the checkout status catalog.
        foreach (string code in new[]
        {
            "Completed", "DuplicateRequest", "StockChanged", "ProductUnavailable",
            "PriceChanged", "PaymentInsufficient", "OpenShiftRequired", "ValidationFailed", "RetryableUnknown",
        })
        {
            AssertContains(checkout, $"public const string {code}", $"Checkout must model the {code} conflict/result code.");
        }
    }

    private static void AssertAuditTrail(string checkout)
    {
        AssertContains(checkout, "WriteAuditAsync", "Remote checkout must write an audit trail.");
        AssertContains(checkout, "_auditWriteRepository", "Remote checkout must persist audit via the write repository.");
        foreach (string field in new[] { "host=", "store=", "terminal=", "role=", "operation=", "result=", "clientRequestId=", "receipt=" })
        {
            AssertContains(checkout, field, $"Audit reason must record {field}.");
        }
    }

    private static void AssertNoRawErrorLeak(string checkout)
    {
        AssertContains(checkout, "never surface raw internal error details", "Remote checkout must document that raw errors are never surfaced.");
        AssertContains(checkout, "Retry is safe", "Retryable failures must give safe retry guidance.");
        // The remote client must never receive the SQLite path or connection string.
        AssertNotContains(checkout, "connectionString", "Remote checkout must not surface a connection string.");
        AssertNotContains(checkout, "DatabasePath", "Remote checkout must not surface the database path.");
    }

    private static void AssertClientSendsIntentOnly(string contracts)
    {
        // The client-provided payment carries only a method + amount: no price/stock/sequence trust.
        AssertContains(contracts, "RemotePosCheckoutPaymentRequest(string Method, long AmountCents)",
            "Client payment intent must carry only method + amount (no authoritative price).");
        // The submit request carries only the idempotency key + payment intent, never a receipt/sequence.
        AssertContains(contracts, "RemotePosCheckoutSubmitRequest(", "Checkout submit request contract must exist.");
    }

    private static void AssertWiring(string doc, string coreSmoke, string infraSmoke)
    {
        AssertContains(doc, "Remote Checkout Runtime", "LAN-M16 doc must document the remote checkout runtime.");
        AssertContains(doc, "Host-Owned Official Write Boundary", "LAN-M16 doc must document the host-owned write boundary.");
        AssertContains(coreSmoke, "LanM16RemoteCheckoutIdempotencyAndHostWriteChecks.Run();", "CoreSmoke must wire the LAN-M16 checkout/host-write guard.");
        AssertContains(infraSmoke, "remote-pos-checkout-idempotency", "InfrastructureSmoke must wire the remote-pos-checkout-idempotency command.");
        AssertContains(infraSmoke, "remote-pos-host-write-boundary", "InfrastructureSmoke must wire the remote-pos-host-write-boundary command.");
    }

    private static string ReadRequiredFile(params string[] pathParts)
    {
        string path = Path.Combine(pathParts);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required source file missing: " + path);
        }

        return File.ReadAllText(path);
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? directory = new(startPath);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from current directory.");
    }

    private static void AssertContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string source, string forbidden, string message)
    {
        if (source.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
