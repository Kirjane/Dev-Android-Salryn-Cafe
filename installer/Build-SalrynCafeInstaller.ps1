param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$AppVersion = "1.0.0-rc1",
    [string]$IsccPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$stagingDir = Join-Path $repoRoot "artifacts\installer\staging\SalrynCafe"
$outputDir = Join-Path $repoRoot "artifacts\installer\setup"
$issPath = Join-Path $PSScriptRoot "SalrynCafeSetup.iss"

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates = @(
        (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }

    $IsccPath = $candidates | Select-Object -First 1
}

if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    throw "Inno Setup 6 compiler (ISCC.exe) not found. Install Inno Setup 6 or pass -IsccPath."
}

if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir
}
New-Item -ItemType Directory -Force -Path $stagingDir, $outputDir | Out-Null

Write-Host "[Salryn Cafe Installer] Publishing Salryn.Api.Local ($Configuration / $Runtime)..."
dotnet publish (Join-Path $repoRoot "src\Salryn.Api.Local\Salryn.Api.Local.csproj") `
    -c $Configuration -r $Runtime --self-contained false -o $stagingDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Salryn.Api.Local failed ($LASTEXITCODE)" }

Write-Host "[Salryn Cafe Installer] Publishing Salryn.Desktop.WebView ($Configuration / $Runtime)..."
dotnet publish (Join-Path $repoRoot "src\Salryn.Desktop.WebView\Salryn.Desktop.WebView.csproj") `
    -c $Configuration -r $Runtime --self-contained false -o $stagingDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Salryn.Desktop.WebView failed ($LASTEXITCODE)" }

$brandSrc = Join-Path $repoRoot "assets\brand"
if (Test-Path $brandSrc) {
    $brandDst = Join-Path $stagingDir "assets\brand"
    New-Item -ItemType Directory -Force -Path $brandDst | Out-Null
    Copy-Item -Path (Join-Path $brandSrc "*") -Destination $brandDst -Recurse -Force
}

$iconSrc = Join-Path $repoRoot "assets\branding\salryn-icon.ico"
if (Test-Path $iconSrc) {
    $iconDst = Join-Path $stagingDir "assets\branding"
    New-Item -ItemType Directory -Force -Path $iconDst | Out-Null
    Copy-Item -Path $iconSrc -Destination $iconDst -Force
}

$rev = (git -C $repoRoot rev-parse --short HEAD 2>$null)
if (-not $rev) { $rev = "unknown" }

Write-Host "[Salryn Cafe Installer] Compiling SalrynCafeSetup.iss via $IsccPath..."
& $IsccPath `
    "/DMyAppVersion=$AppVersion" `
    "/DMySourceRevision=$rev" `
    "/DStagingDir=$stagingDir" `
    "/DSetupOutputDir=$outputDir" `
    $issPath
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe failed ($LASTEXITCODE)" }

$setupExe = Join-Path $outputDir "Salryn-Cafe-Setup.exe"
if (-not (Test-Path $setupExe)) {
    throw "Expected installer output not found at $setupExe"
}

$hash = (Get-FileHash -Path $setupExe -Algorithm SHA256).Hash
$size = (Get-Item $setupExe).Length
Write-Host "[Salryn Cafe Installer] PASS: $setupExe ($size bytes)"
Write-Host "[Salryn Cafe Installer] SHA-256: $hash"
