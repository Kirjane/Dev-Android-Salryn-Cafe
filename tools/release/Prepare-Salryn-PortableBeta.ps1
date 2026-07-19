# Prepares the machine-owned folders used by a normal Salryn portable beta launch.
# Run from an Administrator PowerShell when the current user cannot modify ProgramData.

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Join-Path $env:ProgramData "Salryn"
$folders = @("backups", "config", "data", "exports", "logs", "receipts")
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name

try {
    Write-Host "Preparing Salryn portable beta folders for $identity"
    Write-Host "Root: $root"

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    foreach ($folder in $folders) {
        $path = Join-Path $root $folder
        New-Item -ItemType Directory -Force -Path $path | Out-Null
        Write-Host "Ready: $path"
    }

    $rootAcl = (& icacls.exe $root 2>&1 | Out-String)
    $escapedIdentity = [Regex]::Escape($identity)
    $hasRequiredAce = $rootAcl -match "${escapedIdentity}:.*\(OI\)\(CI\)\(M\)"

    if ($hasRequiredAce) {
        Write-Host "Current Windows user already has inheritable Modify access."
    }
    else {
        Write-Host "Granting the current Windows user Modify access..."
        $grantOutput = (& icacls.exe $root /grant "${identity}:(OI)(CI)M" /T /C 2>&1 | Out-String)
        $grantOutput | Write-Host
        if ($LASTEXITCODE -ne 0 -or $grantOutput -match "Failed processing [1-9][0-9]* files") {
            throw "Folder permission update did not complete. Re-run this script from an Administrator PowerShell."
        }
    }

    $dataPath = Join-Path $root "data"
    $probePath = Join-Path $dataPath (".salryn-write-test-" + [Guid]::NewGuid().ToString("N") + ".tmp")
    try {
        [IO.File]::WriteAllText($probePath, "Salryn write test")
        Remove-Item -LiteralPath $probePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $probePath) {
            Remove-Item -LiteralPath $probePath -Force
        }
    }

    Write-Host "SUCCESS: Salryn folders are ready and the data folder write test passed." -ForegroundColor Green
    exit 0
}
catch {
    Write-Error ("FAILED: " + $_.Exception.Message)
    Write-Host "No store data was deleted. Re-run from an Administrator PowerShell if permission was denied." -ForegroundColor Yellow
    exit 1
}
