# Backup-And-Reset-AllDevState.ps1
# SAFE full local --dev QA reset for local QA only.
#
# Resets BOTH:
#   1) {repo}\.salryn-dev          (--dev SQLite: users, store, audit, LAN, sessions)
#   2) %LOCALAPPDATA%\Salryn       (license, installation id, local reports)
#
# Does NOT touch C:\ProgramData\Salryn (production).
# Does NOT delete data (rename only).
# Does NOT clear browser storage automatically — see checklist below.

[CmdletBinding()]
param(
    [switch]$WhatIf,
    [string]$ProjectRoot
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectScript = Join-Path $scriptDir "Backup-And-Reset-DevProjectState.ps1"
$localScript = Join-Path $scriptDir "Backup-And-Reset-LocalAppDataState.ps1"

if (-not (Test-Path -LiteralPath $projectScript)) {
    throw "Missing companion script: $projectScript"
}
if (-not (Test-Path -LiteralPath $localScript)) {
    throw "Missing companion script: $localScript"
}

Write-Host "=== Salryn FULL local --dev QA reset ==="
Write-Host "This resets BOTH .salryn-dev and %LOCALAPPDATA%\Salryn (or `$env:LOCALAPPDATA) for local --dev QA."
Write-Host "Production ProgramData is NOT touched."
Write-Host ""

if ($WhatIf) {
    if (-not [string]::IsNullOrWhiteSpace($ProjectRoot)) {
        & $projectScript -WhatIf -ProjectRoot $ProjectRoot
    } else {
        & $projectScript -WhatIf
    }
    & $localScript -WhatIf
} else {
    if (-not [string]::IsNullOrWhiteSpace($ProjectRoot)) {
        & $projectScript -ProjectRoot $ProjectRoot
    } else {
        & $projectScript
    }
    & $localScript
}

Write-Host ""
Write-Host "=== Browser storage cleanup checklist (manual) ==="
Write-Host "Filesystem scripts cannot clear browser storage / site data."
Write-Host "1. Stop API/host processes (already required by sub-scripts)."
Write-Host "2. Open browser DevTools for http://127.0.0.1:5299"
Write-Host "3. Application / Storage -> Clear site data (cookies, localStorage, sessionStorage)."
Write-Host "4. If testing LAN terminal client on another origin, clear that origin too."
Write-Host "5. Close and reopen the tab (or hard reload Ctrl+F5)."
Write-Host ""
Write-Host "=== Next commands ==="
Write-Host "  cd <repo>"
Write-Host "  dotnet run --project src\Salryn.Api.Local -- --dev"
Write-Host "  Open http://127.0.0.1:5299/license"
Write-Host "Expect: no DEVKIRCH store, no signed-in user, no audit history, no LAN DB footprints."
Write-Host "If license env SALRYN_LICENSE_DEVELOPMENT_MODE=1 is set, License page may still be skipped."
