# Reset-SalrynLocalState.ps1
#
# Safely reset local Salryn QA runtime state (DB + license/config) with a timestamped backup.
# Default: reset DB + license/config only. Receipts/exports/logs only when switches are passed.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\Reset-SalrynLocalState.ps1 -Force
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\Reset-SalrynLocalState.ps1 -Force -IncludeReceipts -IncludeExports -IncludeLogs

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$Force,
    [switch]$IncludeReceipts,
    [switch]$IncludeExports,
    [switch]$IncludeLogs,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

if ($Help) {
    Write-Host @"
Reset-SalrynLocalState.ps1

  Stops Salryn processes, backs up ProgramData (+ LocalAppData Licensing), then resets
  salryn.db and license/config for a fresh installed-app QA state.

Parameters:
  -Force              Skip interactive confirmation
  -IncludeReceipts    Also move ProgramData\Salryn\receipts into the backup
  -IncludeExports     Also move ProgramData\Salryn\exports into the backup
  -IncludeLogs        Also move ProgramData\Salryn\logs into the backup
  -Help               Show this help

Default reset (DB + license/config only):
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\Reset-SalrynLocalState.ps1 -Force

Full clean QA reset:
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\qa\Reset-SalrynLocalState.ps1 -Force -IncludeReceipts -IncludeExports -IncludeLogs

License path (not under ProgramData):
  %LOCALAPPDATA%\Salryn\Licensing\
"@
    exit 0
}

function Write-Info([string]$Message) { Write-Host "[qa-reset] $Message" }
function Write-WarnLine([string]$Message) { Write-Warning "[qa-reset] $Message" }

function Stop-SalrynProcesses {
    $stopped = New-Object System.Collections.Generic.List[string]
    $names = @("Salryn.Desktop.WebView", "Salryn.Api.Local")

    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Info "Stopping process $($_.ProcessName) (PID $($_.Id))"
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            $stopped.Add("$($_.ProcessName)#$($_.Id)")
        }
    }

    # Best-effort: stop anything still listening on 5299
    try {
        $listeners = Get-NetTCPConnection -LocalPort 5299 -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique
        foreach ($owningPid in $listeners) {
            if ($owningPid -and $owningPid -gt 0) {
                $proc = Get-Process -Id $owningPid -ErrorAction SilentlyContinue
                if ($proc) {
                    Write-Info "Stopping port-5299 owner $($proc.ProcessName) (PID $owningPid)"
                    Stop-Process -Id $owningPid -Force -ErrorAction SilentlyContinue
                    $stopped.Add("$($proc.ProcessName)#$owningPid")
                }
            }
        }
    }
    catch {
        Write-WarnLine "Port 5299 listener scan skipped: $($_.Exception.Message)"
    }

    Start-Sleep -Seconds 1
    return $stopped
}

function Move-ItemSafe {
    param(
        [string]$Source,
        [string]$DestinationDir,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        return $false
    }

    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
    $leaf = Split-Path -Leaf $Source
    $dest = Join-Path $DestinationDir $leaf
    if (Test-Path -LiteralPath $dest) {
        $dest = Join-Path $DestinationDir ("{0}.{1}" -f $leaf, [Guid]::NewGuid().ToString("N").Substring(0, 8))
    }

    Write-Info "Moving $Label -> $dest"
    Move-Item -LiteralPath $Source -Destination $dest -Force
    return $true
}

function Copy-ItemSafe {
    param(
        [string]$Source,
        [string]$DestinationDir,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        return $false
    }

    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
    $leaf = Split-Path -Leaf $Source
    $dest = Join-Path $DestinationDir $leaf
    Write-Info "Backing up $Label -> $dest"
    if ((Get-Item -LiteralPath $Source).PSIsContainer) {
        Copy-Item -LiteralPath $Source -Destination $dest -Recurse -Force
    }
    else {
        Copy-Item -LiteralPath $Source -Destination $dest -Force
    }
    return $true
}

# ---------------------------------------------------------------------------
$programDataRoot = Join-Path $env:ProgramData "Salryn"
$localLicenseRoot = Join-Path $env:LOCALAPPDATA "Salryn\Licensing"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $programDataRoot "qa-state-backups\$timestamp"

Write-Host ""
Write-Host "Salryn Local QA State Reset"
Write-Host "ProgramData root : $programDataRoot"
Write-Host "License root     : $localLicenseRoot"
Write-Host "Backup folder    : $backupRoot"
Write-Host "Include receipts : $IncludeReceipts"
Write-Host "Include exports  : $IncludeExports"
Write-Host "Include logs     : $IncludeLogs"
Write-Host ""

if (-not $Force) {
    $answer = Read-Host "This will stop Salryn and reset local DB + license after backup. Type YES to continue"
    if ($answer -ne "YES") {
        Write-Host "Aborted."
        exit 1
    }
}

$stopped = Stop-SalrynProcesses

New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
$backupProgramData = Join-Path $backupRoot "ProgramData-Salryn"
$backupLocalLicense = Join-Path $backupRoot "LocalAppData-Salryn-Licensing"
New-Item -ItemType Directory -Force -Path $backupProgramData, $backupLocalLicense | Out-Null

$dbPath = Join-Path $programDataRoot "data\salryn.db"
$configPath = Join-Path $programDataRoot "config"
$receiptsPath = Join-Path $programDataRoot "receipts"
$exportsPath = Join-Path $programDataRoot "exports"
$logsPath = Join-Path $programDataRoot "logs"

$dbBackedUp = Copy-ItemSafe -Source $dbPath -DestinationDir (Join-Path $backupProgramData "data") -Label "salryn.db"
$configBackedUp = Copy-ItemSafe -Source $configPath -DestinationDir $backupProgramData -Label "config"
$null = Copy-ItemSafe -Source $receiptsPath -DestinationDir $backupProgramData -Label "receipts"
$null = Copy-ItemSafe -Source $exportsPath -DestinationDir $backupProgramData -Label "exports"
$null = Copy-ItemSafe -Source $logsPath -DestinationDir $backupProgramData -Label "logs"

# Backup entire LocalAppData Licensing tree when present
$licenseTreeBackedUp = Copy-ItemSafe -Source $localLicenseRoot -DestinationDir (Join-Path $backupRoot "LocalAppData-Salryn") -Label "Licensing"

# Also scoop any ProgramData license-looking files (rare; contract usually uses LocalAppData)
$licenseMovedCount = 0
if (Test-Path -LiteralPath $programDataRoot) {
    $licenseNamePattern = 'license|activation|entitlement|\.lic$|\.license$'
    Get-ChildItem -LiteralPath $programDataRoot -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notlike "*\qa-state-backups\*" -and
            ($_.Name -match $licenseNamePattern)
        } |
        ForEach-Object {
            $rel = $_.FullName.Substring($programDataRoot.Length).TrimStart('\')
            $destDir = Join-Path $backupProgramData (Split-Path $rel -Parent)
            if ([string]::IsNullOrWhiteSpace((Split-Path $rel -Parent))) {
                $destDir = $backupProgramData
            }
            New-Item -ItemType Directory -Force -Path $destDir | Out-Null
            $dest = Join-Path $destDir $_.Name
            Write-Info "Backing up ProgramData license-looking item: $($_.FullName)"
            if ($_.PSIsContainer) {
                Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
            }
            else {
                Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
            }
        }
}

# --- Reset DB ---
$dbReset = $false
if (Test-Path -LiteralPath $dbPath) {
    # Prefer move of live DB into backup "reset" folder so original is gone from data\
    $resetDbDir = Join-Path $backupRoot "reset-moved\data"
    $dbReset = Move-ItemSafe -Source $dbPath -DestinationDir $resetDbDir -Label "live salryn.db"
}
# Also remove sidecar WAL/SHM if present
foreach ($sidecar in @("salryn.db-wal", "salryn.db-shm", "salryn.db-journal")) {
    $sidePath = Join-Path (Join-Path $programDataRoot "data") $sidecar
    if (Test-Path -LiteralPath $sidePath) {
        Move-ItemSafe -Source $sidePath -DestinationDir (Join-Path $backupRoot "reset-moved\data") -Label $sidecar | Out-Null
    }
}

# --- Reset license (LocalAppData) ---
$licenseReset = $false
if (Test-Path -LiteralPath $localLicenseRoot) {
    Get-ChildItem -LiteralPath $localLicenseRoot -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $moved = Move-ItemSafe -Source $_.FullName -DestinationDir (Join-Path $backupRoot "reset-moved\Licensing") -Label $_.Name
        if ($moved) {
            $licenseReset = $true
            $licenseMovedCount++
        }
    }
}

# Move ProgramData license-looking live files (after backup already taken)
if (Test-Path -LiteralPath $programDataRoot) {
    $licenseNamePattern = 'license|activation|entitlement|\.lic$|\.license$'
    Get-ChildItem -LiteralPath $programDataRoot -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            -not $_.PSIsContainer -and
            $_.FullName -notlike "*\qa-state-backups\*" -and
            ($_.Name -match $licenseNamePattern)
        } |
        ForEach-Object {
            $relParent = Split-Path $_.FullName.Substring($programDataRoot.Length).TrimStart('\') -Parent
            $destDir = Join-Path (Join-Path $backupRoot "reset-moved\ProgramData") $relParent
            if (Move-ItemSafe -Source $_.FullName -DestinationDir $destDir -Label $_.Name) {
                $licenseReset = $true
                $licenseMovedCount++
            }
        }
}

# Optional content resets
$receiptsReset = $false
$exportsReset = $false
$logsReset = $false

if ($IncludeReceipts -and (Test-Path -LiteralPath $receiptsPath)) {
    Get-ChildItem -LiteralPath $receiptsPath -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if (Move-ItemSafe -Source $_.FullName -DestinationDir (Join-Path $backupRoot "reset-moved\receipts") -Label $_.Name) {
            $receiptsReset = $true
        }
    }
}

if ($IncludeExports -and (Test-Path -LiteralPath $exportsPath)) {
    Get-ChildItem -LiteralPath $exportsPath -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if (Move-ItemSafe -Source $_.FullName -DestinationDir (Join-Path $backupRoot "reset-moved\exports") -Label $_.Name) {
            $exportsReset = $true
        }
    }
}

if ($IncludeLogs -and (Test-Path -LiteralPath $logsPath)) {
    Get-ChildItem -LiteralPath $logsPath -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if (Move-ItemSafe -Source $_.FullName -DestinationDir (Join-Path $backupRoot "reset-moved\logs") -Label $_.Name) {
            $logsReset = $true
        }
    }
}

# Config: move contents (preserve folder)
$configReset = $false
if (Test-Path -LiteralPath $configPath) {
    Get-ChildItem -LiteralPath $configPath -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if (Move-ItemSafe -Source $_.FullName -DestinationDir (Join-Path $backupRoot "reset-moved\config") -Label $_.Name) {
            $configReset = $true
        }
    }
}

# Recreate required folders
$requiredFolders = @(
    (Join-Path $programDataRoot "backups"),
    (Join-Path $programDataRoot "config"),
    (Join-Path $programDataRoot "data"),
    (Join-Path $programDataRoot "exports"),
    (Join-Path $programDataRoot "logs"),
    (Join-Path $programDataRoot "receipts"),
    (Join-Path $programDataRoot "qa-state-backups"),
    $localLicenseRoot
)
foreach ($folder in $requiredFolders) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}

# Write test
$writeTestPath = Join-Path $programDataRoot "data\write-test.tmp"
$writeTestPass = $false
try {
    Set-Content -LiteralPath $writeTestPath -Value "salryn-qa-write-test" -Encoding UTF8
    if (Test-Path -LiteralPath $writeTestPath) {
        Remove-Item -LiteralPath $writeTestPath -Force
        $writeTestPass = -not (Test-Path -LiteralPath $writeTestPath)
    }
}
catch {
    Write-WarnLine "Write test failed: $($_.Exception.Message)"
    $writeTestPass = $false
}

Write-Host ""
Write-Host "QA STATE RESET SUMMARY"
Write-Host "Stopped processes     : $(if ($stopped.Count) { ($stopped -join ', ') } else { '(none)' })"
Write-Host "Backup path           : $backupRoot"
Write-Host "DB backed up          : $dbBackedUp"
Write-Host "DB reset              : $dbReset"
Write-Host "Config backed up      : $configBackedUp"
Write-Host "Config reset          : $configReset"
Write-Host "License tree backed up: $licenseTreeBackedUp"
Write-Host "License files moved   : $licenseReset (count=$licenseMovedCount)"
Write-Host "Receipts reset        : $receiptsReset (requested=$IncludeReceipts)"
Write-Host "Exports reset         : $exportsReset (requested=$IncludeExports)"
Write-Host "Logs reset            : $logsReset (requested=$IncludeLogs)"
Write-Host "Folders recreated     : yes"
Write-Host "Write test            : $(if ($writeTestPass) { 'PASS' } else { 'FAIL' })"
Write-Host ""
Write-Host "Next: launch Salryn POS from the desktop/Start Menu shortcut for a fresh QA session."
Write-Host "Old state is preserved under: $backupRoot"

if (-not $writeTestPass) {
    exit 1
}

exit 0
