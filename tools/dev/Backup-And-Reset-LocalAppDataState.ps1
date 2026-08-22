# Backup-And-Reset-LocalAppDataState.ps1
# SAFE local identity / license reset for local QA only.
#
# Resets:
#   %LOCALAPPDATA%\Salryn
# including:
#   Licensing\license.json
#   Licensing\installation-id.txt
#   Reports\NativePdf (if present)
#
# Renames the folder to a timestamped backup. Does NOT delete data.
# Does NOT touch {repo}\.salryn-dev or C:\ProgramData\Salryn.
# Does NOT clear browser localStorage/sessionStorage/cookies.

[CmdletBinding()]
param(
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    throw "LOCALAPPDATA is not set on this machine."
}

$source = Join-Path $env:LOCALAPPDATA "Salryn"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupLeaf = "Salryn.backup-" + $stamp
$backup = Join-Path $env:LOCALAPPDATA $backupLeaf

Write-Host "=== Salryn LocalAppData license/identity backup/reset ==="
Write-Host "Source: $source"
Write-Host "Backup: $backup"
Write-Host "This resets:  %LOCALAPPDATA%\Salryn (license, installation id, local reports)"
Write-Host "This does NOT reset: .salryn-dev, ProgramData\Salryn, browser storage"
Write-Host "Stop Salryn.Api.Local / Host processes first if running."

if (-not (Test-Path -LiteralPath $source)) {
    Write-Host "No LocalAppData Salryn state at $source - already fresh (or never launched)."
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
Write-Host "Done. LocalAppData state moved to: $backup"
Write-Host "Restore (app stopped): Rename-Item -LiteralPath '$backup' -NewName 'Salryn'"
Write-Host "Note: --dev SQLite users/store remain in .salryn-dev unless you also reset project state."
