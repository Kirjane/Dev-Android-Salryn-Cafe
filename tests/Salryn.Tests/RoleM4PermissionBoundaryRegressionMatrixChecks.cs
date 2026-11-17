using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Tests;

public static class RoleM4PermissionBoundaryRegressionMatrixChecks
{
    private const string SessionCookieName = LocalApiSessionService.SessionCookieName;

    public static async Task RunAsync()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        RunSourceGuards(projectRoot);
        await RunRuntimeRoleMatrixAsync().ConfigureAwait(false);
        await RunThreeRoleAndLegacyBoundaryChecksAsync(projectRoot).ConfigureAwait(false);
    }

    private static async Task RunRuntimeRoleMatrixAsync()
    {
        InMemoryLocalApiSessionStore sessionStore = new();
        await using WebApplication app = await LocalApiApplicationFactory
            .CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    SessionStore = sessionStore,
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false
                })
            .ConfigureAwait(false);

        await app.StartAsync().ConfigureAwait(false);

        try
        {
            Uri baseAddress = ResolveStartedBaseAddress(app);

            using HttpClient anonymous = CreateClient(baseAddress);
            await AssertStatusAsync(anonymous, HttpMethod.Get, "/api/dashboard/summary", HttpStatusCode.Unauthorized, "Dashboard without session must return 401.").ConfigureAwait(false);
            await AssertStatusAsync(anonymous, HttpMethod.Get, "/api/products", HttpStatusCode.Unauthorized, "Products without session must return 401.").ConfigureAwait(false);
            await AssertStatusAsync(anonymous, HttpMethod.Get, "/api/remote-readonly/status", HttpStatusCode.Unauthorized, "Remote status without session must return 401.").ConfigureAwait(false);

            using HttpClient admin = CreateClient(
                baseAddress,
                CreateSession(sessionStore, "Admin", DefaultSeedData.AdminPermissions));
            await AssertAllowedAsync(admin, "/api/products", "Admin should reach product reads.").ConfigureAwait(false);
            await AssertAllowedAsync(admin, "/api/inventory/low-stock", "Admin should reach inventory reads.").ConfigureAwait(false);
            await AssertAllowedAsync(admin, "/api/sales/history", "Admin should reach sales history reads.").ConfigureAwait(false);
            await AssertAllowedAsync(admin, "/api/users", "Admin should reach user reads.").ConfigureAwait(false);
            await AssertAllowedAsync(admin, "/api/audit/logs", "Admin should reach audit reads.").ConfigureAwait(false);
            await AssertAllowedAsync(admin, "/api/remote-readonly/products?page=1&pageSize=25", "Admin should reach remote product reads.").ConfigureAwait(false);
            await AssertAllowedAsync(admin, "/api/remote-readonly/terminal-status", "Admin should reach remote terminal status.").ConfigureAwait(false);
            await AssertAllowedAsync(admin, "/api/remote-readonly/audit?page=1&pageSize=25", "Admin should reach remote audit visibility.").ConfigureAwait(false);

            using HttpClient manager = CreateClient(
                baseAddress,
                CreateSession(sessionStore, "Manager", DefaultSeedData.ManagerPermissions));
            await AssertAllowedAsync(manager, "/api/products", "Manager should reach product reads.").ConfigureAwait(false);
            await AssertAllowedAsync(manager, "/api/inventory/low-stock", "Manager should reach inventory reads.").ConfigureAwait(false);
            await AssertAllowedAsync(manager, "/api/sales/history", "Manager should reach sales history reads.").ConfigureAwait(false);
            await AssertAllowedAsync(manager, "/api/remote-readonly/products?page=1&pageSize=25", "Manager should reach remote product reads.").ConfigureAwait(false);
            if (DefaultSeedData.ManagerPermissions.Contains(DefaultSeedData.Permissions.StoreTerminalView, StringComparer.Ordinal))
            {
                await AssertAllowedAsync(manager, "/api/remote-readonly/terminal-status", "Manager should reach terminal status when StoreTerminal.View is seeded.").ConfigureAwait(false);
            }

            await AssertStatusAsync(manager, HttpMethod.Get, "/api/users", HttpStatusCode.Forbidden, "Manager without Users.Manage must return 403 for users.").ConfigureAwait(false);
            await AssertStatusAsync(manager, HttpMethod.Get, "/api/audit/logs", HttpStatusCode.Forbidden, "Manager without Audit.View must return 403 for audit logs.").ConfigureAwait(false);
            await AssertStatusAsync(manager, HttpMethod.Get, "/api/remote-readonly/audit?page=1&pageSize=25", HttpStatusCode.Forbidden, "Manager without Audit.View must return 403 for remote audit.").ConfigureAwait(false);

            using HttpClient cashier = CreateClient(
                baseAddress,
                CreateSession(sessionStore, "Cashier", DefaultSeedData.CashierPermissions));
            await AssertAllowedAsync(cashier, "/api/products", "Cashier should reach product reads.").ConfigureAwait(false);
            await AssertAllowedAsync(cashier, "/api/sales/history", "Cashier should reach sales history reads.").ConfigureAwait(false);
            await AssertAllowedAsync(cashier, "/api/remote-readonly/products?page=1&pageSize=25", "Cashier should reach remote product reads.").ConfigureAwait(false);
            await AssertStatusAsync(cashier, HttpMethod.Get, "/api/inventory/low-stock", HttpStatusCode.Forbidden, "Cashier without Inventory.View must return 403 for inventory.").ConfigureAwait(false);
            await AssertStatusAsync(cashier, HttpMethod.Get, "/api/users", HttpStatusCode.Forbidden, "Cashier without Users.Manage must return 403 for users.").ConfigureAwait(false);
            await AssertStatusAsync(cashier, HttpMethod.Get, "/api/audit/logs", HttpStatusCode.Forbidden, "Cashier without Audit.View must return 403 for audit logs.").ConfigureAwait(false);
            await AssertStatusAsync(cashier, HttpMethod.Get, "/api/remote-readonly/terminal-status", HttpStatusCode.Forbidden, "Cashier without StoreTerminal.View must return 403 for remote terminal status.").ConfigureAwait(false);
            await AssertStatusAsync(cashier, HttpMethod.Get, "/api/remote-readonly/audit?page=1&pageSize=25", HttpStatusCode.Forbidden, "Cashier without Audit.View must return 403 for remote audit.").ConfigureAwait(false);

            using HttpClient adminRoleWithoutPermission = CreateClient(
                baseAddress,
                CreateSession(sessionStore, "Admin", Array.Empty<string>()));
            await AssertStatusAsync(adminRoleWithoutPermission, HttpMethod.Get, "/api/products", HttpStatusCode.Forbidden, "Admin roleName without Products.View must not authorize product reads.").ConfigureAwait(false);

            using HttpClient cashierRoleWithInventoryPermission = CreateClient(
                baseAddress,
                CreateSession(sessionStore, "Cashier", new[] { DefaultSeedData.Permissions.InventoryView }));
            await AssertAllowedAsync(cashierRoleWithInventoryPermission, "/api/inventory/low-stock", "Cashier roleName with Inventory.View permission must reach inventory reads.").ConfigureAwait(false);

            await AssertMutationRouteNotIntroducedAsync(anonymous, "/api/products").ConfigureAwait(false);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private static HttpClient CreateClient(Uri baseAddress, LocalApiSession? session = null)
    {
        HttpClient client = new()
        {
            BaseAddress = baseAddress
        };

        if (session is not null)
        {
            client.DefaultRequestHeaders.Add(
                "Cookie",
                $"{SessionCookieName}={session.SessionId}");
        }

        return client;
    }

    private static LocalApiSession CreateSession(
        InMemoryLocalApiSessionStore sessionStore,
        string roleName,
        IReadOnlyList<string> permissions)
    {
        AuthenticatedUserReadModel user = new(
            Guid.NewGuid(),
            "role-m4-runtime-" + Guid.NewGuid().ToString("N"),
            "ROLE-M4 Runtime",
            roleName,
            UserStatus.Active,
            permissions)
        {
            RoleId = Guid.NewGuid()
        };

        return sessionStore.Create(user);
    }

    private static async Task AssertAllowedAsync(
        HttpClient client,
        string path,
        string message)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"{message} Got auth boundary status {(int)response.StatusCode}.");
        }
    }

    private static async Task AssertStatusAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpStatusCode expectedStatus,
        string message)
    {
        using HttpRequestMessage request = new(method, path);
        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException(
                $"{message} Expected {(int)expectedStatus}, got {(int)response.StatusCode}.");
        }
    }

    private static async Task AssertMutationRouteNotIntroducedAsync(
        HttpClient client,
        string path)
    {
        foreach (HttpMethod method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using HttpRequestMessage request = new(method, path);
            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            if ((int)response.StatusCode is >= 200 and <= 299)
            {
                throw new InvalidOperationException(
                    $"ROLE-M4 must not introduce mutation route {method.Method} {path}.");
            }
        }
    }

    private static Uri ResolveStartedBaseAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(item =>
            item.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(address) || address.EndsWith(":0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ROLE-M4 runtime harness could not resolve started loopback address.");
        }

        return new Uri(address);
    }

    private static void RunSourceGuards(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string appWebRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src", "security");
        string seedFile = ReadRequiredFile(Path.Combine(
            projectRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Seeding",
            "DefaultSeedData.cs"));
        string frontendPermissions = ReadRequiredFile(Path.Combine(appWebRoot, "permissions.ts"));
        string apiPermissions = ReadRequiredFile(Path.Combine(apiRoot, "Auth", "LocalApiPermissionGuards.cs"));
        string readOnlyEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "ReadOnlyEndpointExtensions.cs"));
        string remoteReadOnlyEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "RemoteReadOnlyApiEndpoints.cs"));
        string roleM3Harness = ReadRequiredFile(Path.Combine(projectRoot, "tests", "Salryn.Tests", "RoleM3HttpApiPermissionRuntimeChecks.cs"));
        string coreSmokeProgram = ReadRequiredFile(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        AssertSeedRoleBoundaries(seedFile);
        AssertFrontendBackendPermissionConsistency(frontendPermissions, apiPermissions, readOnlyEndpoints, remoteReadOnlyEndpoints);
        AssertContains(roleM3Harness, "LocalApiApplicationFactory", "ROLE-M4 must reuse the ROLE-M3 runtime seam.");
        AssertContains(roleM3Harness, "AssertMutationRouteNotIntroducedAsync", "ROLE-M3 mutation-negative harness must remain present.");
        AssertContains(coreSmokeProgram, "RoleM4PermissionBoundaryRegressionMatrixChecks.RunAsync()", "ROLE-M4 must be explicitly wired into CoreSmoke.");

        string navigationSource = ReadRequiredFile(Path.Combine(projectRoot, "src", "Salryn.App.Web", "src", "navigation.ts"));
        string shellSource = ReadRequiredFile(Path.Combine(projectRoot, "src", "Salryn.App.Web", "src", "components", "RoleAwareAppShell.tsx"));
        int salesNavIdx = navigationSource.IndexOf("id: \"sales\"", StringComparison.Ordinal);
        int ordersNavIdx = navigationSource.IndexOf("id: \"customer-orders\"", StringComparison.Ordinal);
        if (salesNavIdx < 0 || ordersNavIdx < 0 || salesNavIdx >= ordersNavIdx)
        {
            throw new InvalidOperationException("Issue #49 regression: Sales & Receipts (Cashier group) must precede Customer Orders (Manager group) so Cashier items do not render beneath Manager.");
        }
        AssertContains(shellSource, "index === 0 || navItems[index - 1].group !== item.group", "RoleAwareAppShell must compute firstInGroup from adjacent item transitions.");

        AssertChangedFilesStayInRoleM4Scope(projectRoot);
    }

    private static async Task RunThreeRoleAndLegacyBoundaryChecksAsync(string projectRoot)
    {
        // 1. Fresh seed data must only contain the three product roles: Admin, Manager, Cashier.
        if (DefaultSeedData.Roles.Count != 3 ||
            !DefaultSeedData.Roles.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(new[] { "Admin", "Cashier", "Manager" }, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Issue #64: DefaultSeedData.Roles must define strictly Admin, Manager, and Cashier.");
        }

        if (DefaultSeedData.RolePermissions.Any(rp => !DefaultSeedData.IsSupportedProductRoleId(rp.RoleId)))
        {
            throw new InvalidOperationException("Issue #64: DefaultSeedData.RolePermissions must not grant permissions to legacy specialist roles.");
        }

        string isolatedRoot = Path.Combine(Path.GetTempPath(), "salryn-w2-roles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedRoot);
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.CreateDevelopment(isolatedRoot);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            // 2. Freshly initialized DB has strictly 3 seeded roles.
            Result<IReadOnlyList<UserRoleReadModel>> cleanRoles =
                await composition.UserReadRepository.GetRolesAsync().ConfigureAwait(false);
            if (cleanRoles.IsFailure || cleanRoles.Value.Count != 3 || cleanRoles.Value.Any(r => !r.IsActive))
            {
                throw new InvalidOperationException("Issue #64: Fresh DB must seed exactly 3 active roles (Admin, Manager, Cashier).");
            }

            // 3. Simulate an upgraded DB containing a historical legacy role, legacy role_permissions rows,
            //    an Admin account, and a historical Purchasing Officer user (non-destructive preservation).
            Guid legacyUserId = Guid.NewGuid();
            Guid adminUserId = Guid.NewGuid();
            string legacyPasswordHash = SalrynPasswordHasher.HashPassword("LegacyUser123!").Value!;
            string adminPasswordHash = SalrynPasswordHasher.HashPassword("AdminUser123!").Value!;

            using (SqliteConnection conn = new($"Data Source={composition.DbPaths.DatabasePath}"))
            {
                conn.Open();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO roles (id, name, is_system_role, created_at)
                    VALUES ($legacyRoleId, 'Purchasing Officer', 1, $now);

                    INSERT INTO role_permissions (id, role_id, permission, created_at)
                    VALUES
                        ($perm1, $legacyRoleId, 'Products.View', $now),
                        ($perm2, $legacyRoleId, 'Procurement.ManagePurchaseOrder', $now);

                    INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
                    VALUES
                        ($adminUserId, 'w2-admin', 'W2 Admin', $adminHash, $adminRoleId, 1, $now),
                        ($legacyUserId, 'w2-legacy-po', 'Legacy Purchasing Officer', $legacyHash, $legacyRoleId, 1, $now);
                    """;
                cmd.Parameters.AddWithValue("$legacyRoleId", DefaultSeedData.PurchasingOfficerRoleId);
                cmd.Parameters.AddWithValue("$adminRoleId", DefaultSeedData.AdminRoleId);
                cmd.Parameters.AddWithValue("$perm1", Guid.NewGuid().ToString("D"));
                cmd.Parameters.AddWithValue("$perm2", Guid.NewGuid().ToString("D"));
                cmd.Parameters.AddWithValue("$adminUserId", adminUserId.ToString("D"));
                cmd.Parameters.AddWithValue("$legacyUserId", legacyUserId.ToString("D"));
                cmd.Parameters.AddWithValue("$adminHash", adminPasswordHash);
                cmd.Parameters.AddWithValue("$legacyHash", legacyPasswordHash);
                cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            // Historical role row remains preserved in DB, marked inactive (IsActive == false).
            Result<IReadOnlyList<UserRoleReadModel>> upgradedRoles =
                await composition.UserReadRepository.GetRolesAsync().ConfigureAwait(false);
            if (upgradedRoles.IsFailure)
            {
                throw new InvalidOperationException("Failed reading roles from upgraded DB simulation.");
            }
            UserRoleReadModel? preservedLegacy = upgradedRoles.Value.FirstOrDefault(
                r => r.RoleId == Guid.Parse(DefaultSeedData.PurchasingOfficerRoleId));
            if (preservedLegacy is null || preservedLegacy.IsActive)
            {
                throw new InvalidOperationException("Issue #64: Historical legacy role in upgraded DB must be preserved non-destructively with IsActive == false.");
            }

            // Assigning a legacy role via write repository must fail closed.
            IUserWriteRepository userWrites = new SqliteRepositoryFactory(composition.ConnectionFactory).CreateUserWriteRepository();
            Result createWithLegacy = await userWrites.CreateAsync(new UserAdminCreateRequest(
                "blocked-legacy-user",
                "Blocked Legacy",
                Guid.Parse(DefaultSeedData.PurchasingOfficerRoleId),
                legacyPasswordHash,
                adminUserId)).ConfigureAwait(false);
            if (createWithLegacy.IsSuccess || createWithLegacy.ErrorDetail.Code != "Users.RoleRequired")
            {
                throw new InvalidOperationException($"Issue #64: Assigning legacy role must fail with Users.RoleRequired, got {createWithLegacy.ErrorDetail?.Code}.");
            }

            // Reading permissions for a historical legacy user must strip/ignore legacy role_permissions rows.
            Result<UserPermissionReadModel?> legacyPermProfile =
                await composition.UserPermissionReadRepository.FindByUserIdAsync(legacyUserId).ConfigureAwait(false);
            if (legacyPermProfile.IsFailure || legacyPermProfile.Value is null || legacyPermProfile.Value.Permissions.Count != 0)
            {
                throw new InvalidOperationException("Issue #64: Legacy role user must receive zero active permissions even when upgraded DB has historical role_permissions.");
            }

            // Verify HTTP runtime fail-closed behavior for legacy login via LocalApiApplicationFactory.
            await using WebApplication app = await LocalApiApplicationFactory
                .CreateAsync(
                    new[] { "--dev" },
                    new LocalApiApplicationOptions
                    {
                        Url = "http://127.0.0.1:0",
                        Composition = composition,
                        InitializeDatabase = false,
                        EnableStaticWebAssets = false,
                        WriteDevelopmentDatabasePath = false
                    })
                .ConfigureAwait(false);

            await app.StartAsync().ConfigureAwait(false);
            try
            {
                Uri baseAddress = ResolveStartedBaseAddress(app);
                using HttpClient legacyClient = new(new HttpClientHandler { UseCookies = false }) { BaseAddress = baseAddress };
                using HttpResponseMessage loginResp = await legacyClient.PostAsJsonAsync(
                    "/api/auth/login",
                    new { username = "w2-legacy-po", password = "LegacyUser123!" }).ConfigureAwait(false);
                if (loginResp.StatusCode != HttpStatusCode.OK)
                {
                    throw new InvalidOperationException($"Issue #64: Expected non-destructive legacy user login 200 OK with empty permissions, got {(int)loginResp.StatusCode}.");
                }
                string cookie = loginResp.Headers.GetValues("Set-Cookie").First().Split(';')[0];

                using HttpRequestMessage prodReq = new(HttpMethod.Get, "/api/products");
                prodReq.Headers.Add("Cookie", cookie);
                using HttpResponseMessage prodResp = await legacyClient.SendAsync(prodReq).ConfigureAwait(false);
                if (prodResp.StatusCode != HttpStatusCode.Forbidden)
                {
                    throw new InvalidOperationException($"Issue #64: Expected legacy user /api/products to fail closed with 403 Forbidden, got {(int)prodResp.StatusCode}.");
                }
            }
            finally
            {
                await app.StopAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(isolatedRoot))
                {
                    Directory.Delete(isolatedRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static void AssertSeedRoleBoundaries(string seedFile)
    {
        AssertContains(seedFile, "new(AdminRoleId, \"Admin\", true)", "Admin seed role must exist.");
        AssertContains(seedFile, "new(ManagerRoleId, \"Manager\", true)", "Manager seed role must exist.");
        AssertContains(seedFile, "new(CashierRoleId, \"Cashier\", true)", "Cashier seed role must exist.");

        string adminBlock = ExtractBlock(seedFile, "AdminPermissions", "ManagerPermissions");
        string managerBlock = ExtractBlock(seedFile, "ManagerPermissions", "CashierPermissions");
        string cashierBlock = ExtractBlock(seedFile, "CashierPermissions", "RolePermissions");

        foreach (string permission in new[]
        {
            "UsersManage",
            "ProductsView",
            "InventoryView",
            "SalesViewHistory",
            "AuditView",
            "StoreTerminalView"
        })
        {
            AssertContains(adminBlock, "Permissions." + permission, "Admin seed must include " + permission + ".");
        }

        foreach (string permission in new[]
        {
            "ProductsView",
            "InventoryView",
            "SalesViewHistory",
            "StoreTerminalView"
        })
        {
            AssertContains(managerBlock, "Permissions." + permission, "Manager seed must include " + permission + ".");
        }

        AssertNotContains(managerBlock, "Permissions.UsersManage", "Manager seed must not include Users.Manage.");
        AssertNotContains(managerBlock, "Permissions.AuditView", "Manager seed must not include Audit.View.");

        AssertContains(cashierBlock, "Permissions.ProductsView", "Cashier seed must include Products.View.");
        AssertContains(cashierBlock, "Permissions.SalesViewHistory", "Cashier seed must include Sales.ViewHistory.");
        AssertContains(cashierBlock, "Permissions.SalesVoid", "Engineer-approved cashier role must include completed-sale Sales.Void without Manager approval.");
        AssertNotContains(cashierBlock, "Permissions.OfficialDocumentRefund", "Cashier void must not grant official-document refunds.");
        AssertNotContains(cashierBlock, "Permissions.OfficialDocumentVoid", "Cashier Sales.Void must not imply official fiscal document void.");
        AssertNotContains(cashierBlock, "Permissions.InventoryView", "Cashier seed must not include Inventory.View.");
        AssertNotContains(cashierBlock, "Permissions.UsersManage", "Cashier seed must not include Users.Manage.");
        AssertNotContains(cashierBlock, "Permissions.AuditView", "Cashier seed must not include Audit.View.");
        AssertNotContains(cashierBlock, "Permissions.StoreTerminalView", "Cashier seed must not include StoreTerminal.View.");
    }

    private static void AssertFrontendBackendPermissionConsistency(
        string frontendPermissions,
        string apiPermissions,
        string readOnlyEndpoints,
        string remoteReadOnlyEndpoints)
    {
        AssertContains(frontendPermissions, "const authenticatedOnlyPages = new Set<AppPage>([\"dashboard\"])", "Only Dashboard may remain authenticated-only in the host page catalog.");
        AssertContains(frontendPermissions, "\"host-lan-control\": [", "Host LAN Control must be permission-gated.");
        AssertContains(frontendPermissions, "Permissions.StoreTerminalView", "Host LAN Control must allow terminal view permission.");
        AssertContains(frontendPermissions, "Permissions.StoreTerminalManage", "Host LAN Control must allow terminal manage permission.");
        AssertContains(frontendPermissions, "Permissions.StoreDeploymentManage", "Host LAN Control must allow deployment permission.");
        AssertNotContains(frontendPermissions, "\"remote-pos\"", "Remote POS Client must not be a host AppPage.");
        AssertNotContains(frontendPermissions, "\"remote-readonly\"", "Remote Read-Only Client must not be a host AppPage.");
        AssertPageAndRoutes(frontendPermissions, readOnlyEndpoints, "products", "ProductsView", "Products.View", new[]
        {
            "/api/products",
            "/api/products/{id:guid}",
            "/api/products/catalog/categories",
            "/api/products/catalog/brands"
        });
        AssertPageAndRoutes(frontendPermissions, readOnlyEndpoints, "inventory", "InventoryView", "Inventory.View", new[]
        {
            "/api/inventory/low-stock",
            "/api/inventory/movements"
        });
        AssertPageAndRoutes(frontendPermissions, readOnlyEndpoints, "sales", "SalesViewHistory", "Sales.ViewHistory", new[]
        {
            "/api/sales/history",
            "/api/sales/{id:guid}"
        });
        AssertPageAndRoutes(frontendPermissions, readOnlyEndpoints, "users", "UsersManage", "Users.Manage", new[]
        {
            "/api/users",
            "/api/users/roles",
            "/api/users/{id:guid}"
        });
        AssertPageAndRoutes(frontendPermissions, readOnlyEndpoints, "audit", "AuditView", "Audit.View", new[]
        {
            "/api/audit/logs",
            "/api/audit/errors"
        });

        foreach (string permission in new[]
        {
            "Products.View",
            "Inventory.View",
            "Sales.ViewHistory",
            "Users.Manage",
            "Audit.View",
            "StoreTerminal.View"
        })
        {
            AssertContains(apiPermissions, "\"" + permission + "\"", "Api.Local permission catalog must include " + permission + ".");
        }

        AssertRemoteRouteAuthenticatedOnly(remoteReadOnlyEndpoints, "/status");
        AssertRemoteRouteAuthenticatedOnly(remoteReadOnlyEndpoints, "/capabilities");
        AssertRemoteRouteAuthenticatedOnly(remoteReadOnlyEndpoints, "/dashboard-summary");
        AssertRemoteRouteAuthenticatedOnly(remoteReadOnlyEndpoints, "/bind-policy");
        AssertRouteRequiresPermission(remoteReadOnlyEndpoints, "group.MapGet(\"/products\"", "LocalApiPermissions.ProductsView");
        AssertRouteRequiresPermission(remoteReadOnlyEndpoints, "group.MapGet(\"/terminal-status\"", "LocalApiPermissions.StoreTerminalView");
        AssertRouteRequiresPermission(remoteReadOnlyEndpoints, "group.MapGet(\"/audit\"", "LocalApiPermissions.AuditView");
    }

    private static void AssertPageAndRoutes(
        string frontendPermissions,
        string endpointSource,
        string page,
        string permissionToken,
        string permissionName,
        IReadOnlyList<string> routes)
    {
        AssertContains(
            frontendPermissions,
            page + ": [Permissions." + permissionToken + "]",
            "Frontend page must map to " + permissionName + ": " + page);

        foreach (string route in routes)
        {
            AssertRouteRequiresPermission(
                endpointSource,
                "app.MapGet(\"" + route + "\"",
                "LocalApiPermissions." + permissionToken);
        }
    }

    private static void AssertRemoteRouteAuthenticatedOnly(string source, string route)
    {
        string block = ExtractRouteBlock(source, "group.MapGet(\"" + route + "\"");
        AssertContains(block, "LocalApiPermissionGuards.RequireAuthenticatedSession", "Remote route must remain authenticated-only: " + route);
        AssertNotContains(block, "RequirePermission", "Remote authenticated-only route must not gain a permission gate: " + route);
    }

    private static void AssertRouteRequiresPermission(string source, string routeMarker, string permissionToken)
    {
        string block = ExtractRouteBlock(source, routeMarker);
        AssertContains(block, "LocalApiPermissionGuards.RequirePermission", "Route must use permission guard: " + routeMarker);
        AssertContains(block, permissionToken, "Route must use expected permission: " + routeMarker);
        AssertNotContains(block, "RoleName", "Route block must not authorize by RoleName: " + routeMarker);
        AssertNotContains(block, "roleName", "Route block must not authorize by roleName: " + routeMarker);
    }

    private static string ExtractRouteBlock(string source, string routeMarker)
    {
        int routeIndex = source.IndexOf(routeMarker, StringComparison.Ordinal);
        if (routeIndex < 0)
        {
            throw new InvalidOperationException("Expected route marker not found: " + routeMarker);
        }

        int nextAppRoute = source.IndexOf("app.MapGet(", routeIndex + routeMarker.Length, StringComparison.Ordinal);
        int nextGroupRoute = source.IndexOf("group.MapGet(", routeIndex + routeMarker.Length, StringComparison.Ordinal);
        int nextRoute = MinPositive(nextAppRoute, nextGroupRoute);

        return nextRoute < 0
            ? source[routeIndex..]
            : source[routeIndex..nextRoute];
    }

    private static int MinPositive(int first, int second)
    {
        if (first < 0)
        {
            return second;
        }

        if (second < 0)
        {
            return first;
        }

        return Math.Min(first, second);
    }

    private static string ExtractBlock(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Expected block start missing: " + startMarker);
        }

        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("Expected block end missing: " + endMarker);
        }

        return source[start..end];
    }

    private static void AssertChangedFilesStayInRoleM4Scope(string projectRoot)
    {
        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("ROLE-M4 changed-file scope guard skipped: no branch-diff paths were available. Static source checks remain active.");
            return;
        }

        HashSet<string> allowedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "00-HANDOFF/ROLE_M4_PERMISSION_BOUNDARY_REGRESSION_MATRIX_STUDY.md",
            "00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md",
            "tests/Salryn.Tests/RoleM4PermissionBoundaryRegressionMatrixChecks.cs",
            "tools/Salryn.Tools.CoreSmoke/Program.cs"
        };

        string[] roleM4ChangedPaths = changedPaths
            .Where(IsRoleM4OwnedPath)
            .ToArray();

        if (roleM4ChangedPaths.Length == 0)
        {
            Console.WriteLine("ROLE-M4 changed-file scope guard skipped: no ROLE-M4-owned branch-diff paths were present. Static source checks remain active.");
            return;
        }

        foreach (string changedPath in roleM4ChangedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (!allowedPaths.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"ROLE-M4 changed an out-of-scope file: {normalizedPath}");
            }

            string lowerPath = normalizedPath.ToLowerInvariant();

            if (lowerPath.Contains("/seeding/", StringComparison.Ordinal)
                || lowerPath.Contains("/migrations/", StringComparison.Ordinal)
                || lowerPath.Contains("checkout", StringComparison.Ordinal)
                || lowerPath.Contains("settings", StringComparison.Ordinal)
                || lowerPath.Contains("backup", StringComparison.Ordinal)
                || lowerPath.Contains("rmo", StringComparison.Ordinal)
                || lowerPath.Contains("official", StringComparison.Ordinal)
                || lowerPath.Contains("pdf", StringComparison.Ordinal)
                || lowerPath.Contains("crystal", StringComparison.Ordinal)
                || lowerPath.Contains("escpos", StringComparison.Ordinal)
                || lowerPath.Contains("esc-pos", StringComparison.Ordinal)
                || lowerPath.Contains("printer", StringComparison.Ordinal)
                || lowerPath.Contains("receipt", StringComparison.Ordinal)
                || lowerPath.Contains("bind", StringComparison.Ordinal)
                || lowerPath.Contains("transport", StringComparison.Ordinal)
                || lowerPath.Contains("discovery", StringComparison.Ordinal)
                || lowerPath.Contains("mobile", StringComparison.Ordinal)
                || lowerPath.Contains("cloud", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"ROLE-M4 changed a forbidden boundary file: {normalizedPath}");
            }
        }
    }

    private static bool IsRoleM4OwnedPath(string path)
    {
        string normalizedPath = path.Replace('\\', '/');

        return normalizedPath.Equals("00-HANDOFF/ROLE_M4_PERMISSION_BOUNDARY_REGRESSION_MATRIX_STUDY.md", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("00-HANDOFF/CURRENT_STABLE_STATE_AND_NEXT_WORK.md", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("tests/Salryn.Tests/RoleM4PermissionBoundaryRegressionMatrixChecks.cs", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("tools/Salryn.Tools.CoreSmoke/Program.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only origin/KIRCH-SANDBOX...HEAD"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only --cached"));
        AddPaths(paths, TryRunGit(projectRoot, "ls-files --others --exclude-standard"));

        if (paths.Count == 0)
        {
            AddPaths(paths, TryRunGit(projectRoot, "diff --name-only KIRCH-SANDBOX...HEAD"));
        }

        return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddPaths(HashSet<string> paths, string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            paths.Add(line.Trim().Replace('\\', '/'));
        }
    }

    private static string? TryRunGit(string projectRoot, string arguments)
    {
        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadRequiredFile(string path)
    {
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
