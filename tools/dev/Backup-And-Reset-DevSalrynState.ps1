# Backup-And-Reset-DevSalrynState.ps1
# SAFE fresh-state helper for local QA only.
# Renames %LOCALAPPDATA%\Salryn to a timestamped backup folder. Does NOT delete data.
# After reset: launch Salryn; expect License landing (no license, no users, no login).
# Restore: rename the backup folder back to Salryn after stopping the app.

[CmdletBinding()]
param(
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$source = Join-Path $env:LOCALAPPDATA "Salryn"
if (-not (Test-Path -LiteralPath $source)) {
    Write-Host "No local Salryn state at $source — already fresh (or never launched)."
    exit 0
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $env:LOCALAPPDATA ("Salryn.backup-" + $stamp)

Write-Host "=== Salryn fresh-state backup/reset ==="
Write-Host "Source: $source"
Write-Host "Backup: $backup"
Write-Host "Stop Salryn.Api.Local / Host processes first if running."

if ($WhatIf) {
    Write-Host "WhatIf: would rename source -> backup."
    exit 0
}

$running = Get-Process -Name "Salryn.Api.Local","Salryn.App.WinForms" -ErrorAction SilentlyContinue
if ($running) {
    throw "Stop Salryn processes first: $($running.Name -join ', ')"
}

Rename-Item -LiteralPath $source -NewName (Split-Path $backup -Leaf)
Write-Host "Done. Local state moved to: $backup"
Write-Host "Launch app and open /license — expect fresh activation landing."
Write-Host "To restore later (app stopped): Rename-Item '$backup' 'Salryn'"
