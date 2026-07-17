# Backup-And-Reset-DevProjectState.ps1
# SAFE --dev project state reset for local QA only.
#
# Resets the repository development SQLite root used by:
#   dotnet run --project src\Salryn.Api.Local -- --dev
#
# Root:  {repo}\.salryn-dev
# DB:    {repo}\.salryn-dev\data\salryn.db
#
# Renames .salryn-dev to a timestamped backup folder. Does NOT delete data.
# Does NOT touch %LOCALAPPDATA%\Salryn or C:\ProgramData\Salryn.
# Does NOT clear browser localStorage/sessionStorage/cookies.

[CmdletBinding()]
param(
    [switch]$WhatIf,
    [string]$ProjectRoot
)

$ErrorActionPreference = "Stop"

function Get-RepoRootFromScript {
    $scriptDir = Split-Path -Parent $PSCommandPath
    $toolsDir = Split-Path -Parent $scriptDir
    $candidate = Split-Path -Parent $toolsDir
    if (Test-Path -LiteralPath (Join-Path $candidate "Salryn.sln")) {
        return (Resolve-Path -LiteralPath $candidate).Path
    }
    throw "Could not locate Salryn.sln from script path. Pass -ProjectRoot."
}

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Get-RepoRootFromScript
} else {
    $ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
}

if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot "Salryn.sln"))) {
    throw "ProjectRoot does not contain Salryn.sln: $ProjectRoot"
}

$source = Join-Path $ProjectRoot ".salryn-dev"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupLeaf = ".salryn-dev.backup-" + $stamp
$backup = Join-Path $ProjectRoot $backupLeaf

Write-Host "=== Salryn --dev PROJECT state backup/reset ==="
Write-Host "Project root: $ProjectRoot"
Write-Host "Source:       $source"
Write-Host "Backup:       $backup"
Write-Host "This resets:  .salryn-dev (SQLite users, store, audit, LAN, sessions for --dev)"
Write-Host "This does NOT reset: %LOCALAPPDATA%\Salryn, ProgramData\Salryn, browser storage"
Write-Host "Stop Salryn.Api.Local / Host processes first if running."

if (-not (Test-Path -LiteralPath $source)) {
    Write-Host "No .salryn-dev at $source - already fresh for --dev DB (or never launched --dev)."
    Write-Host "Expected after --dev + unlicensed LocalAppData: License or first-run path."
    exit 0
}

if ($WhatIf) {
    Write-Host "WhatIf: would Rename-Item source -> backup (no delete)."
    exit 0
}

$running = Get-Process -Name "Salryn.Api.Local","Salryn.App.WinForms" -ErrorAction SilentlyContinue
if ($running) {
    throw "Stop Salryn processes first: $($running.Name -join ', ')"
}

Rename-Item -LiteralPath $source -NewName $backupLeaf
Write-Host "Done. Project --dev state moved to: $backup"
Write-Host "Launch:  dotnet run --project src\Salryn.Api.Local -- --dev"
Write-Host "Open:    http://127.0.0.1:5299/license"
Write-Host "Expect:  no DEVKIRCH store / no users / no audit from the old .salryn-dev DB"
Write-Host "Restore (app stopped): Rename-Item -LiteralPath '$backup' -NewName '.salryn-dev'"
