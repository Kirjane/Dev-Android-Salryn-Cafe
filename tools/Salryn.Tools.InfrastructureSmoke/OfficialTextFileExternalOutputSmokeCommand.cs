using System.Runtime.CompilerServices;

internal static class OfficialTextFileExternalOutputSmokeCommand
{
    [ModuleInitializer]
    public static void RunBeforeProgram()
    {
        string[] args = Environment.GetCommandLineArgs();

        if (args.Length < 2 ||
            !string.Equals(args[1], "official-output-text-file", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
            Console.WriteLine("===== SALRYN INFRASTRUCTURE SMOKE TOOL =====");
            Console.WriteLine($"Project root: {projectRoot}");
            Console.WriteLine("Command: official-output-text-file");
            Console.WriteLine();
            OfficialTextFileExternalOutputSmokeChecks.Run(projectRoot);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine("SMOKE TOOL FAILED.");
            Console.WriteLine(ex);
            Environment.Exit(1);
        }
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
}
