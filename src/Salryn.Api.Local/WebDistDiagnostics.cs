using System.Text.RegularExpressions;

namespace Salryn.Api.Local;

internal sealed record WebDistSnapshot(
    string? DistPath,
    bool DistDirectoryExists,
    bool IndexHtmlExists,
    DateTimeOffset? IndexHtmlLastWriteUtc,
    long? IndexHtmlSizeBytes,
    IReadOnlyList<string> ScriptAssetNames,
    IReadOnlyList<string> StylesheetAssetNames,
    string? BuildStampUtc,
    IReadOnlyList<string> Warnings)
{
    public bool HasWarnings => Warnings.Count > 0;
}

internal static class WebDistDiagnostics
{
    private const string BuildStampFileName = "salryn-build-stamp.txt";
    private static readonly Regex ScriptSrcRegex = new(
        @"src=""(/assets/[^""]+\.js)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StylesheetHrefRegex = new(
        @"href=""(/assets/[^""]+\.css)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static WebDistSnapshot Inspect(string? distPath)
    {
        List<string> warnings = new();

        if (string.IsNullOrWhiteSpace(distPath))
        {
            warnings.Add("Web dist path could not be resolved; static app shell is disabled.");
            return new WebDistSnapshot(
                null,
                false,
                false,
                null,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                null,
                warnings);
        }

        bool distExists = Directory.Exists(distPath);
        if (!distExists)
        {
            warnings.Add($"Web dist directory is missing: {distPath}");
        }

        string indexPath = Path.Combine(distPath, "index.html");
        bool indexExists = distExists && File.Exists(indexPath);
        DateTimeOffset? indexMtime = null;
        long? indexSize = null;
        List<string> scriptAssets = new();
        List<string> stylesheetAssets = new();
        string? buildStamp = null;

        if (!indexExists)
        {
            warnings.Add($"index.html is missing under web dist: {indexPath}");
        }
        else
        {
            FileInfo indexInfo = new(indexPath);
            indexMtime = indexInfo.LastWriteTimeUtc;
            indexSize = indexInfo.Length;

            try
            {
                string indexHtml = File.ReadAllText(indexPath);
                scriptAssets.AddRange(ExtractAssetNames(indexHtml, ScriptSrcRegex));
                stylesheetAssets.AddRange(ExtractAssetNames(indexHtml, StylesheetHrefRegex));

                if (scriptAssets.Count == 0)
                {
                    warnings.Add("index.html does not reference a hashed JS bundle under /assets/.");
                }
            }
            catch (IOException ex)
            {
                warnings.Add($"index.html could not be read: {ex.Message}");
            }
        }

        string stampPath = Path.Combine(distPath, BuildStampFileName);
        if (File.Exists(stampPath))
        {
            try
            {
                buildStamp = File.ReadAllText(stampPath).Trim();
            }
            catch (IOException ex)
            {
                warnings.Add($"Build stamp could not be read: {ex.Message}");
            }
        }
        else if (indexExists)
        {
            warnings.Add($"Build stamp file is missing ({BuildStampFileName}); run npm run build to regenerate dist.");
        }

        return new WebDistSnapshot(
            distPath,
            distExists,
            indexExists,
            indexMtime,
            indexSize,
            scriptAssets,
            stylesheetAssets,
            buildStamp,
            warnings);
    }

    public static void WriteStartupLog(WebDistSnapshot snapshot)
    {
        if (snapshot.DistPath is null)
        {
            Console.WriteLine("[Salryn Web] Static app shell: disabled (dist not resolved).");
            foreach (string warning in snapshot.Warnings)
            {
                Console.WriteLine($"[Salryn Web] WARNING: {warning}");
            }

            return;
        }

        Console.WriteLine($"[Salryn Web] Static root: {snapshot.DistPath}");
        Console.WriteLine(
            $"[Salryn Web] index.html: exists={snapshot.IndexHtmlExists}, "
            + $"mtimeUtc={snapshot.IndexHtmlLastWriteUtc?.ToString("O") ?? "n/a"}, "
            + $"sizeBytes={snapshot.IndexHtmlSizeBytes?.ToString() ?? "n/a"}");

        if (snapshot.ScriptAssetNames.Count > 0)
        {
            Console.WriteLine($"[Salryn Web] JS assets: {string.Join(", ", snapshot.ScriptAssetNames)}");
        }

        if (snapshot.StylesheetAssetNames.Count > 0)
        {
            Console.WriteLine($"[Salryn Web] CSS assets: {string.Join(", ", snapshot.StylesheetAssetNames)}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.BuildStampUtc))
        {
            Console.WriteLine($"[Salryn Web] Build stamp UTC: {snapshot.BuildStampUtc}");
        }

        foreach (string warning in snapshot.Warnings)
        {
            Console.WriteLine($"[Salryn Web] WARNING: {warning}");
        }
    }

    public static object ToApiResponse(WebDistSnapshot snapshot) =>
        new
        {
            distPath = snapshot.DistPath,
            distDirectoryExists = snapshot.DistDirectoryExists,
            indexHtmlExists = snapshot.IndexHtmlExists,
            indexHtmlLastWriteUtc = snapshot.IndexHtmlLastWriteUtc?.ToString("O"),
            indexHtmlSizeBytes = snapshot.IndexHtmlSizeBytes,
            scriptAssets = snapshot.ScriptAssetNames,
            stylesheetAssets = snapshot.StylesheetAssetNames,
            buildStampUtc = snapshot.BuildStampUtc,
            warnings = snapshot.Warnings,
        };

    public static void ApplyIndexHtmlNoCacheHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }

    private static IEnumerable<string> ExtractAssetNames(string indexHtml, Regex pattern)
    {
        foreach (Match match in pattern.Matches(indexHtml))
        {
            if (match.Groups.Count > 1)
            {
                yield return match.Groups[1].Value;
            }
        }
    }
}