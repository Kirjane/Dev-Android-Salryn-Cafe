# Cafe legacy verification repair — bounded evidence

- Base: `59ebdcde85653307ec5aa99ff94684ec69d33382`.
- Tested source commit: `ea85cecabf89eb0046abd3d558af8697bea3e34a`, branch `codex/cafe-legacy-verification`.
- Environment: Windows NT 10.0.26300.0, .NET SDK 10.0.404, Debug/net10.0-windows. Dot-sourced `%LocalAppData%/SalrynForgeTools/forge-env.ps1` per session.
- Scope: test/runner changes only. No push, merge, production source or retained operator/retail data changes. No independent acceptance claim.

The LAN-M8/M9 module guards and ROLE-M3 source guard expected the retired retail URL literal in API source. They now execute the shared Cafe typed boundary check: default identity/5399 URL, LAN opt-in default, resolved default URL set, rejected retail/wildcard/LAN/scheme overrides, rejected wildcard LAN configuration, and local/remote/cross-origin operator authority. Existing source and security guards remain. The LAN-M6 registration guard now verifies canonical Cafe catalog wiring and typed membership of the original v12 credential/handshake migration.

The new `--only cafe-loopback-boundary` selection preserves existing module initializers. Its helper opens no database or listener. Module LAN-M5 data checks use `GetTempPath()/salryn-lanm5-status-<GUID>`; full-run development composition is redirected by `CoreSmokeDevelopmentDatabaseIsolation` to a GUID temporary root. U1 uses its own `GetTempPath()/salryn-u1-runtime-<GUID>` synthetic fixture and cleans up in `finally`.

## Frozen-source validation

Run from the isolated worktree after the environment script:

```powershell
dotnet build tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj -m:1 --nologo
dotnet run --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj --no-build -- --only cafe-loopback-boundary
dotnet run --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj --no-build
```

- Build: exit `0`, 20 warnings, zero errors. Build-only result; no test PASS inferred.
- Focused execution: **PASS**, exit `0`. Four successful helper invocations, each reporting 13 assertions: **52 explicit typed assertions**. Unchanged module checks also executed; they do not report an aggregate assertion count.
- Full execution: **FAIL**, exit `-532462766`. Four helper invocations again passed (52 explicit assertions), then the run stopped at `U1AccountsReceivableRuntimeChecks.cs:149`: `U1 backup must pass restore verification.` No overall suite PASS. Total full-run assertions are not instrumented.
- Remaining full-suite checks after that failure: **NOT RUN**. Physical hardware and production acceptance: **HUMAN_REQUIRED**.

## Product blocker and bounded stop

The U1 fixture initializes the canonical Cafe v41 database, creates a synthetic backup, and calls `SqliteBackupRecoveryService.VerifyBackup`. That service reads `SalrynDatabaseMigrationCatalog.SupportedMigrationVersion` (still **32**) and requires backup version <= supported version (`SqliteBackupRecoveryService.cs:139-148`). `BackupVerificationSummary.IsRestorable` rejects future versions. This is a production backup/catalog integration issue; raising a source-literal expectation or bypassing the restorable guard would not repair it. Production code is unchanged.

Three repair/test cycles were used. Cycle 1 build succeeded and the 13-assertion helper passed before stale LAN-M6 registration aborted the module initializer. Cycle 2 focused execution passed; full execution reached the stale ROLE-M3 URL guard. Cycle 3 is the frozen validation above. Further repair stopped at the product blocker, as authorized.

## Retained logs

Raw logs are outside Git at `C:/Users/arane/SalrynForgeRecovery/20261121-takeover/legacy-qa/`. SHA-256:

| Artifact | SHA-256 |
| --- | --- |
| `cycle3-build.log` | `5F695FCBE0E924114FEC64230248F1FA76AB852EC140FA710666891B0B201F8B` |
| `cycle3-focused.log` | `5141BF2EB590B8165DFE4F5F9CBF2461B2F6CD282C3AEE227867F745B34AF14F` |
| `cycle3-full.log` | `3508AB77D4085A3DB2EF58E787B90E7E89BAE41F998B3F995AB183E58076F68D` |

Earlier `cycle1-*` and `cycle2-*` logs are retained there. This evidence-only follow-up does not claim its own commit was the executable source tested.
