internal static class RmoEngineeringCandidateSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== RMO ENGINEERING CANDIDATE GATE =====");

        OfficialCheckoutCompletionTrustSmokeChecks.Run(projectRoot);
        OfficialXReadingSmokeChecks.Run(projectRoot);
        OfficialZReadingSmokeChecks.Run(projectRoot);
        OfficialDocumentAuditEventSmokeChecks.Run(projectRoot);
        OfficialDocumentLifecycleSmokeChecks.Run(projectRoot);
        OfficialOutputSourceSmokeChecks.Run(projectRoot);
        OfficialDocumentReprintSmokeChecks.Run(projectRoot);
        OfficialTextFileExternalOutputSmokeChecks.Run(projectRoot);

        Console.WriteLine("RMO engineering candidate smoke gate passed.");
        Console.WriteLine();
    }
}
