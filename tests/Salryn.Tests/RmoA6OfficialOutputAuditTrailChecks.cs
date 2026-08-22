using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.System;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class RmoA6OfficialOutputAuditTrailChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string factorySource = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Repositories", "SqliteRepositoryFactory.cs");
        string auditWriteSource = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Repositories", "System", "SqliteAuditWriteRepository.cs");
        string recorderSource = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowAuditRecorder.cs");
        string boundarySource = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowUiBoundary.cs");
        string appServices = Read(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string contractsSource = Read(repoRoot, "src", "Salryn.Core", "Services", "OfficialOutputTextFileWorkflowAuditContracts.cs");
        string navigation = Read(repoRoot, "src", "Salryn.App.Web", "src", "navigation.ts");
        string readOnlyEndpoints = Read(repoRoot, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs");
        string auditReadSource = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Repositories", "System", "SqliteLogReadRepositories.cs");

        RequireContains(coreSmoke, "RmoA6OfficialOutputAuditTrailChecks.Run();", "CoreSmoke must run RMO-A6 audit trail checks.");
        RequireContains(factorySource, "CreateAuditWriteRepository", "Repository factory must expose audit write repository.");
        RequireContains(auditWriteSource, "IAuditWriteRepository", "Sqlite audit write repository must implement IAuditWriteRepository.");
        RequireContains(appServices, "IAuditWriteRepository AuditWriteRepository", "AppServices must expose audit write repository.");
        RequireContains(appServices, "IOfficialOutputTextFileWorkflowAuditRecorder OfficialOutputTextFileWorkflowAudit", "AppServices must expose official output audit recorder.");
        RequireContains(appServices, "new OfficialOutputTextFileWorkflowAuditRecorder(AuditWriteRepository)", "AppServices must construct official output audit recorder.");
        RequireContains(appServices, "OfficialOutputTextFileWorkflowAudit);", "UI boundary must receive official output audit recorder.");
        RequireNotContains(recorderSource, "OfficialDocumentAuditEventService", "Official output audit recorder must not use lifecycle audit event service.");
        RequireContains(boundarySource, "FinalizeRunAsync", "UI boundary must finalize runs through audit-aware completion.");
        RequireContains(boundarySource, "RecordRunAttemptAsync", "UI boundary must record official output run attempts.");
        RequireContains(contractsSource, "OfficialOutputTextFileWorkflowUi.AuditRecordFailed", "Contracts must define the controlled post-output audit failure code.");
        RequireNotContains(recorderSource, "reprint", "Official output audit recorder must not introduce reprint wording.");
        RequireNotContains(contractsSource, "reprint", "Official output audit contracts must not introduce reprint wording.");
        RequireNotContains(navigation, "Official Output Text File", "Unsupported official-output work must not appear in store navigation.");
        RequireNotContains(navigation, "enabled: false", "Store navigation must not render disabled developer-only surfaces.");
        RequireNotContains(readOnlyEndpoints, "official-output", "Read-only API must not expose official-output endpoints.");
        RequireContains(auditReadSource, "FROM audit_logs", "Generic audit read path must remain available for WEB-B0 Audit page.");

        Require(
            OfficialOutputTextFileWorkflowAuditActions.ResolveAction(
                succeeded: true,
                readinessBlocked: false,
                errorCode: null) == OfficialOutputTextFileWorkflowAuditActions.RunSucceeded,
            "Success runs must map to OFFICIAL_OUTPUT_TEXT_FILE_RUN_SUCCEEDED.");
        Require(
            OfficialOutputTextFileWorkflowAuditActions.ResolveAction(
                succeeded: false,
                readinessBlocked: false,
                errorCode: "OfficialOutputTextFileWorkflowUi.PermissionRequired") ==
                OfficialOutputTextFileWorkflowAuditActions.PermissionDenied,
            "Permission denial must map to OFFICIAL_OUTPUT_TEXT_FILE_PERMISSION_DENIED.");
        Require(
            OfficialOutputTextFileWorkflowAuditActions.ResolveAction(
                succeeded: false,
                readinessBlocked: true,
                errorCode: "OfficialOutputTextFileWorkflowUi.ReadinessUnavailable") ==
                OfficialOutputTextFileWorkflowAuditActions.ReadinessBlocked,
            "Readiness blocks must map to OFFICIAL_OUTPUT_TEXT_FILE_READINESS_BLOCKED.");
        Require(
            OfficialOutputTextFileWorkflowAuditActions.ResolveAction(
                succeeded: false,
                readinessBlocked: false,
                errorCode: "OfficialTextFileOutput.OutputPackageAlreadyExists") ==
                OfficialOutputTextFileWorkflowAuditActions.OutputPackageAlreadyExists,
            "Duplicate package conflicts must map to OFFICIAL_OUTPUT_TEXT_FILE_OUTPUT_PACKAGE_ALREADY_EXISTS.");
        Require(
            OfficialOutputTextFileWorkflowAuditActions.ResolveAction(
                succeeded: false,
                readinessBlocked: false,
                errorCode: "OfficialTextFileOutput.OutputRootMustExist") ==
                OfficialOutputTextFileWorkflowAuditActions.OutputRootRejected,
            "Output root rejection must map to OFFICIAL_OUTPUT_TEXT_FILE_OUTPUT_ROOT_REJECTED.");

        OfficialOutputTextFileWorkflowAuditRecordRequest successRequest = new(
            UserId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            TerminalId: "POS-RMO-A6",
            PackageHash: "package-hash-123",
            WorkflowReference: "salryn://official-external-output-workflow/example",
            ErrorCode: null,
            Succeeded: true,
            ReadinessBlocked: false,
            Metadata: new Dictionary<string, string>
            {
                ["journalEntryId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                ["officialDocumentNumber"] = "OR-2026-000001"
            });

        Result<string> successJson = OfficialOutputTextFileWorkflowAuditDetailBuilder.BuildNewValueJson(
            successRequest,
            OfficialOutputTextFileWorkflowAuditActions.RunSucceeded);
        RequireSuccess(successJson, "Success audit detail JSON must build.");
        RequireContains(successJson.Value, "\"packageHash\":\"package-hash-123\"", "Audit detail must include package hash.");
        RequireContains(successJson.Value, "\"terminalId\":\"POS-RMO-A6\"", "Audit detail must include terminal id.");
        RequireContains(successJson.Value, "\"artifactFolderRelative\":\"official-output/package-hash-123\"", "Audit detail must include relative artifact folder only.");
        RequireNotContains(successJson.Value, "C:\\\\", "Audit detail must exclude full output root path.");
        RequireNotContains(successJson.Value, "PasswordHash", "Audit detail must exclude secrets.");
        RequireNotContains(successJson.Value, "reprint", "Audit detail must exclude reprint wording.");

        RunAuditFailureSanitizationChecks(boundarySource, recorderSource, contractsSource);

        RunAuditRepositoryIntegration();
    }

    private static void RunAuditFailureSanitizationChecks(
        string boundarySource,
        string recorderSource,
        string contractsSource)
    {
        RequireContains(contractsSource, "OfficialOutputTextFileWorkflowAuditFailure", "Contracts must expose the controlled audit-failure sanitizer.");
        RequireContains(boundarySource, "BuildSanitizedAuditFailure", "UI boundary must route audit failures through a controlled sanitizer.");
        RequireContains(boundarySource, "OfficialOutputTextFileWorkflowAuditFailure.SanitizedMessage", "UI boundary must use the controlled sanitized audit-failure message.");
        RequireContains(boundarySource, "OfficialOutputTextFileWorkflowAuditFailure.AuditRecordFailedCode", "UI boundary must use the controlled audit-failure code.");
        RequireSourceDoesNotContain(boundarySource, "auditResult.ErrorDetail", "UI boundary must not echo raw audit error detail to UI output.");
        RequireContains(recorderSource, "OfficialOutputTextFileWorkflowAuditFailure.ControlledError", "Recorder must convert repository failures into a controlled audit failure.");

        foreach (bool filesGenerated in new[] { true, false })
        {
            string message = OfficialOutputTextFileWorkflowAuditFailure.SanitizedMessage(filesGenerated);
            Require(!string.IsNullOrWhiteSpace(message), "Sanitized audit-failure message must be present.");
            Require(!message.Contains("rollback", StringComparison.OrdinalIgnoreCase), "Sanitized audit-failure message must not claim rollback.");
            Require(!message.Contains("deleted", StringComparison.OrdinalIgnoreCase), "Sanitized audit-failure message must not claim deletion.");

            foreach (string token in HostileAuditTokens())
            {
                RequireNotContains(message, token, "Sanitized audit-failure message must exclude hostile token: " + token);
            }
        }

        Require(
            OfficialOutputTextFileWorkflowAuditFailure.AuditRecordFailedCode ==
                "OfficialOutputTextFileWorkflowUi.AuditRecordFailed",
            "Controlled audit-failure code must stay stable.");

        Error controlledError = OfficialOutputTextFileWorkflowAuditFailure.ControlledError();
        Require(
            controlledError.Code == OfficialOutputTextFileWorkflowAuditFailure.AuditRecordFailedCode,
            "Controlled audit-failure error must use the controlled code.");

        foreach (string token in HostileAuditTokens())
        {
            RequireNotContains(controlledError.Message, token, "Controlled audit-failure error must exclude hostile token: " + token);
        }
    }

    private static IEnumerable<string> HostileAuditTokens()
    {
        yield return "SQLite";
        yield return "FOREIGN KEY";
        yield return "C:\\";
        yield return ".salryn-dev";
        yield return "salryn.db";
        yield return "stack trace";
        yield return "Exception";
    }

    private static void RunAuditRepositoryIntegration()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-rmo-a6-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SqliteConnectionFactory factory = new(paths);
            MigrationRunner runner = new(factory, new IDatabaseMigration[] { new InitialSchemaMigration() });
            runner.RunPendingMigrations();

            SqliteAuditWriteRepository writeRepository = new(factory);
            SqliteAuditReadRepository readRepository = new(factory);
            Guid userId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            SeedAuditUser(factory);

            Result successWrite = writeRepository.AddAsync(new AuditLog
            {
                UserId = userId,
                Action = OfficialOutputTextFileWorkflowAuditActions.RunSucceeded,
                EntityType = OfficialOutputTextFileWorkflowAuditActions.EntityType,
                EntityId = "package-hash-123",
                Reason = OfficialOutputTextFileWorkflowAuditActions.RunSucceeded,
                NewValueJson = "{\"result\":\"succeeded\",\"permission\":\"OfficialOutput.Run\",\"terminalId\":\"POS-RMO-A6\",\"packageHash\":\"package-hash-123\",\"artifactFolderRelative\":\"official-output/package-hash-123\"}"
            }).GetAwaiter().GetResult();
            RequireSuccess(successWrite, "Success audit write must succeed.");

            Result duplicateWrite = writeRepository.AddAsync(new AuditLog
            {
                UserId = userId,
                Action = OfficialOutputTextFileWorkflowAuditActions.OutputPackageAlreadyExists,
                EntityType = OfficialOutputTextFileWorkflowAuditActions.EntityType,
                EntityId = "package-hash-123",
                Reason = "OfficialTextFileOutput.OutputPackageAlreadyExists",
                NewValueJson = "{\"result\":\"failed\",\"permission\":\"OfficialOutput.Run\",\"errorCode\":\"OfficialTextFileOutput.OutputPackageAlreadyExists\"}"
            }).GetAwaiter().GetResult();
            RequireSuccess(duplicateWrite, "Duplicate package audit write must succeed.");

            Result permissionWrite = writeRepository.AddAsync(new AuditLog
            {
                UserId = userId,
                Action = OfficialOutputTextFileWorkflowAuditActions.PermissionDenied,
                EntityType = OfficialOutputTextFileWorkflowAuditActions.EntityType,
                EntityId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                Reason = "OfficialOutputTextFileWorkflowUi.PermissionRequired",
                NewValueJson = "{\"result\":\"failed\",\"permission\":\"OfficialOutput.Run\",\"errorCode\":\"OfficialOutputTextFileWorkflowUi.PermissionRequired\"}"
            }).GetAwaiter().GetResult();
            RequireSuccess(permissionWrite, "Permission denied audit write must succeed.");

            Result<PagedResult<AuditLogReadModel>> searchResult = readRepository
                .SearchAuditLogsAsync(new AuditLogSearchFilter(
                    Page: new PageRequest(1, 50),
                    From: null,
                    To: null,
                    Search: new SearchText("OFFICIAL_OUTPUT_TEXT_FILE")))
                .GetAwaiter()
                .GetResult();
            RequireSuccess(searchResult, "Audit read path must return official output audit events.");
            Require(searchResult.Value.Items.Count >= 3, "Audit read path must surface official output audit events.");

            foreach (AuditLogReadModel item in searchResult.Value.Items)
            {
                Require(
                    string.Equals(item.EntityType, OfficialOutputTextFileWorkflowAuditActions.EntityType, StringComparison.Ordinal),
                    "Audit event entity type must be OFFICIAL_OUTPUT_TEXT_FILE.");
                RequireNotContains(item.Reason ?? string.Empty, "Exception", "Audit reason must exclude raw exception text.");
            }

            AuditLogReadModel? successEvent = searchResult.Value.Items
                .FirstOrDefault(item => string.Equals(
                    item.Action,
                    OfficialOutputTextFileWorkflowAuditActions.RunSucceeded,
                    StringComparison.Ordinal));

            Require(successEvent is not null, "Success audit event must exist.");
            Require(successEvent!.UserId == userId, "Success audit event must include user id when available.");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void SeedAuditUser(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        using SqliteCommand roleCommand = connection.CreateCommand();
        roleCommand.CommandText = """
            INSERT INTO roles (id, name, is_system_role, created_at)
            VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd', 'Admin', 1, $createdAt);
            """;
        roleCommand.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        roleCommand.ExecuteNonQuery();

        using SqliteCommand userCommand = connection.CreateCommand();
        userCommand.CommandText = """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES (
                'cccccccc-cccc-cccc-cccc-cccccccccccc',
                'rmo-a6-audit',
                'RMO A6 Audit',
                'HASH',
                'dddddddd-dddd-dddd-dddd-dddddddddddd',
                1,
                $createdAt
            );
            """;
        userCommand.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        userCommand.ExecuteNonQuery();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireSourceDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireSuccess(Result result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message + $" Error: {result.ErrorDetail.Code}");
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message + $" Error: {result.ErrorDetail.Code}");
        }
    }
}
