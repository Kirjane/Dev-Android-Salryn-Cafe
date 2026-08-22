namespace Salryn.Tests.SmokeProof;

/// <summary>
/// Classifies what a smoke check actually proves. This is intentionally small and descriptive;
/// it does not replace existing CoreSmoke runners or promote source inspection to runtime proof.
/// </summary>
public enum SmokeProofClass
{
    /// <summary>Reads source files or handoff text and asserts expected seams, names, or forbidden patterns.</summary>
    SourceInspection = 0,

    /// <summary>Proves a type or boundary compiles and is reachable from the smoke/test project.</summary>
    CompileBoundary = 1,

    /// <summary>Exercises service decisions with synthetic inputs, fakes, or in-memory harnesses.</summary>
    SyntheticBehavior = 2,

    /// <summary>Exercises controlled local filesystem behavior such as temp roots, safe names, backup files, or artifact writes.</summary>
    FileSystemBehavior = 3,

    /// <summary>Composes runtime services, routes, persistence, or local API surfaces without claiming full operator acceptance.</summary>
    RuntimeComposition = 4,

    /// <summary>Requires a human-run app, browser, Windows, LAN, installer, external compliance, or real-machine proof pack.</summary>
    ManualProofRequired = 5
}
