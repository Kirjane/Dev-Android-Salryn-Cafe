# Backup-And-Reset-DevSalrynState.ps1
# COMPATIBILITY WRAPPER for local --dev QA.
#
# This resets BOTH:
#   - {repo}\.salryn-dev              (--dev SQLite: users, store, audit, LAN)
#   - %LOCALAPPDATA%\Salryn           (license / installation id / local reports)
#
# Historical note:
# The previous LocalAppData-only version misled operators who launched with --dev
# and still saw DEVKIRCH store/users/audit because SQLite lives under .salryn-dev.
#
# Prefer calling Backup-And-Reset-AllDevState.ps1 directly.
# Does NOT delete data. Uses Rename-Item via companion scripts.

[CmdletBinding()]
param(
    [switch]$WhatIf,
    [string]$ProjectRoot
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$allScript = Join-Path $scriptDir "Backup-And-Reset-AllDevState.ps1"

if (-not (Test-Path -LiteralPath $allScript)) {
    throw "Missing companion script: $allScript"
}

Write-Host "=== Compatibility wrapper: Backup-And-Reset-DevSalrynState.ps1 ==="
Write-Host "This resets BOTH .salryn-dev and LocalAppData for local --dev QA."
Write-Host "Delegating to Backup-And-Reset-AllDevState.ps1"
Write-Host ""

if ($WhatIf) {
    if (-not [string]::IsNullOrWhiteSpace($ProjectRoot)) {
        & $allScript -WhatIf -ProjectRoot $ProjectRoot
    } else {
        & $allScript -WhatIf
    }
} else {
    if (-not [string]::IsNullOrWhiteSpace($ProjectRoot)) {
        & $allScript -ProjectRoot $ProjectRoot
    } else {
        & $allScript
    }
}
