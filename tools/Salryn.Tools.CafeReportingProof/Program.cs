using Salryn.Infrastructure.Data;
using Salryn.Tools.CafeReportingProof;

string root =
    Path.Combine(
        Path.GetTempPath(),
        "salryn-c11-reporting-" +
        Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(root);

try
{
    C11AReportSourceCogsProof.Run(
        Path.Combine(root, "c11a"));

    C11BProfitabilityOperationsProof.Run(
        Path.Combine(root, "c11b"));

    C11CSyncCompletenessMobileProof.Run(
        Path.Combine(root, "c11c"),
        Path.Combine(root, "mobile"));

    C11DApiReactSurfaceProof.Run();

    Console.WriteLine(
        "PASS C11 Cafe Reporting / COGS / Profitability proof");
}
finally
{
    SqliteConnectionFactory.ClearAllPools();

    try
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
    catch
    {
    }
}
