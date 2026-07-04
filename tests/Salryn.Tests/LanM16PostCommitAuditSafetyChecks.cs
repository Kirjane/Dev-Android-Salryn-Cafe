namespace Salryn.Tests;

// LAN-M16-QA-A guard: post-commit audit safety. This is a source-level guard (the functional proof
// lives in the remote-pos-post-commit-audit-safety infrastructure smoke). It proves the committed
// sale is memoized to the idempotency map and the cart is cleared BEFORE the best-effort audit runs,
// that the audit write is wrapped so a failure can never fail a committed sale or leak exception
// detail, and that a retry after commit replays the original result instead of double-posting.
public static class LanM16PostCommitAuditSafetyChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string remotePosRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local", "RemotePos");
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");

        string checkout = ReadRequiredFile(remotePosRoot, "RemotePosCheckoutService.cs");
        string doc = ReadRequiredFile(handoffRoot, "LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        AssertSuccessMemoizedBeforeBestEffort(checkout);
        AssertAuditIsWrappedBestEffort(checkout);
        AssertNoRawExceptionLeak(checkout);
        AssertWiring(doc, coreSmoke, infraSmoke);
    }

    private static void AssertSuccessMemoizedBeforeBestEffort(string checkout)
    {
        int executeStart = checkout.IndexOf("private async Task<RemotePosCheckoutResultResponse> ExecuteSubmitAsync(", StringComparison.Ordinal);
        AssertTrue(executeStart >= 0, "ExecuteSubmitAsync must exist.");
        string execute = checkout.Substring(executeStart);

        int commitCall = execute.IndexOf("CompleteSaleAsync", StringComparison.Ordinal);
        int commitFailureGuard = execute.IndexOf("if (completion.IsFailure)", StringComparison.Ordinal);
        int memoize = execute.IndexOf("_idempotency[idempotencyKey] = completed;", StringComparison.Ordinal);
        int cartClear = execute.IndexOf("cart.Lines.Clear();", StringComparison.Ordinal);
        int auditCall = execute.IndexOf("WriteAuditAsync(", StringComparison.Ordinal);

        AssertTrue(commitCall >= 0, "Host sale must be completed via CompleteSaleAsync.");
        AssertTrue(commitFailureGuard >= 0, "A pre-commit failure must be handled before memoization.");
        AssertTrue(memoize >= 0, "The committed success must be memoized to the idempotency map.");
        AssertTrue(cartClear >= 0, "The cart must be cleared after a committed sale.");
        AssertTrue(auditCall >= 0, "The checkout must write an audit trail.");

        // Ordering: commit -> memoize success -> clear cart -> best-effort audit.
        AssertTrue(commitCall < memoize, "Sale must commit before the success is memoized.");
        AssertTrue(commitFailureGuard < memoize, "A pre-commit failure must be rejected before memoization.");
        AssertTrue(memoize < auditCall, "Success must be memoized BEFORE the best-effort audit runs.");
        AssertTrue(cartClear < auditCall, "The cart must be cleared BEFORE the best-effort audit runs.");

        // The idempotency key flows into ExecuteSubmitAsync so memoization is keyed identically.
        AssertContains(checkout, "string idempotencyKey,", "ExecuteSubmitAsync must receive the idempotency key for immediate memoization.");
        AssertContains(checkout, "await ExecuteSubmitAsync(session, request, key, cancellationToken)",
            "SubmitAsync must pass the idempotency key into ExecuteSubmitAsync.");
    }

    private static void AssertAuditIsWrappedBestEffort(string checkout)
    {
        int auditStart = checkout.IndexOf("private async Task WriteAuditAsync(", StringComparison.Ordinal);
        AssertTrue(auditStart >= 0, "WriteAuditAsync must exist.");
        string audit = checkout.Substring(auditStart);
        // Trim to the method body by stopping at the next private helper if present.
        int nextMethod = audit.IndexOf("private RemotePosCartResponse BuildCartResponse", StringComparison.Ordinal);
        if (nextMethod > 0)
        {
            audit = audit.Substring(0, nextMethod);
        }

        int tryIndex = audit.IndexOf("try", StringComparison.Ordinal);
        int addAsyncIndex = audit.IndexOf("_auditWriteRepository.AddAsync", StringComparison.Ordinal);
        int catchIndex = audit.IndexOf("catch (Exception", StringComparison.Ordinal);

        AssertTrue(tryIndex >= 0, "The audit write must be wrapped in a try block.");
        AssertTrue(addAsyncIndex > tryIndex, "The audit repository call must be inside the try block.");
        AssertTrue(catchIndex > addAsyncIndex, "The audit write must be followed by a catch that swallows failures.");
        AssertContains(audit, "best-effort", "The audit write must be documented as best-effort.");
        // A failed audit Result is also ignored (not only thrown exceptions).
        AssertContains(audit, "auditResult", "A failed audit Result must be captured and ignored (best-effort).");
    }

    private static void AssertNoRawExceptionLeak(string checkout)
    {
        // The best-effort audit catch must not rethrow, and must not surface the exception to the client.
        int auditStart = checkout.IndexOf("private async Task WriteAuditAsync(", StringComparison.Ordinal);
        AssertTrue(auditStart >= 0, "WriteAuditAsync must exist.");
        string audit = checkout.Substring(auditStart);
        int nextMethod = audit.IndexOf("private RemotePosCartResponse BuildCartResponse", StringComparison.Ordinal);
        if (nextMethod > 0)
        {
            audit = audit.Substring(0, nextMethod);
        }

        AssertNotContains(audit, "throw", "The best-effort audit must never rethrow after a committed sale.");
        AssertNotContains(audit, "ex.Message", "The audit path must never surface raw exception detail.");

        // The retryable-unknown catch in SubmitAsync gives safe retry guidance and leaks nothing raw.
        AssertContains(checkout, "Retry is safe", "Retryable failures must give safe retry guidance.");
        AssertContains(checkout, "the host sale commits", "SubmitAsync must document that only pre-commit errors are retryable-unknown.");
    }

    private static void AssertWiring(string doc, string coreSmoke, string infraSmoke)
    {
        AssertContains(doc, "Post-commit audit safety", "LAN-M16 doc must document post-commit audit safety.");
        AssertContains(coreSmoke, "LanM16PostCommitAuditSafetyChecks.Run();", "CoreSmoke must wire the LAN-M16 post-commit audit safety guard.");
        AssertContains(infraSmoke, "remote-pos-post-commit-audit-safety", "InfrastructureSmoke must wire the remote-pos-post-commit-audit-safety command.");
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

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
