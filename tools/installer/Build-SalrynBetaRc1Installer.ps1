# Salryn POS Beta RC1 — staging and installer build script.
#
# Stages Salryn.Desktop.WebView + Salryn.Api.Local + React dist + branding assets.
# Optionally compiles installer\SalrynSetup.iss via Inno Setup 6 when -BuildInstaller is set.
#
# ISCC discovery order (when -BuildInstaller is requested):
#   1. -InnoSetupCompilerPath parameter
#   2. INNO_SETUP_ISCC environment variable
#   3. ISCC.exe on the system PATH
#   4. %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe  (per-user install)
#   5. C:\Program Files (x86)\Inno Setup 6\ISCC.exe
#   6. C:\Program Files\Inno Setup 6\ISCC.exe
#
# If -BuildInstaller is passed and ISCC.exe is not found the script fails with a
# clear actionable message.  Running without -BuildInstaller stages the app only.
#
# Usage:
#   # Stage only (no Inno Setup required):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1
#
#   # Stage + compile setup EXE:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller
#
#   # Stage + compile using explicit ISCC path:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller -InnoSetupCompilerPath "D:\Tools\InnoSetup6\ISCC.exe"

[CmdletBinding()]
param(
    [string]$Version = "0.1.0-rc1",
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [string]$InnoSetupCompilerPath = "",
    [switch]$SkipWebBuild,
    [switch]$BuildInstaller,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$Message) { Write-Host "[installer-rc1] $Message" }

if ($Help) {
    Write-Host @"
Build-SalrynBetaRc1Installer.ps1

  Stages Salryn.Desktop.WebView, Salryn.Api.Local, branding assets, and React dist.
  Compiles installer\SalrynSetup.iss via Inno Setup 6 when -BuildInstaller is set.

Parameters:
  -Version <semver>              Product version (default 0.1.0-rc1)
  -Configuration <name>          dotnet publish configuration (default Release)
  -OutputDir <path>              Artifact root (default artifacts\installer)
  -InnoSetupCompilerPath <path>  Explicit path to ISCC.exe (overrides auto-discovery)
  -SkipWebBuild                  Reuse existing src/Salryn.App.Web/dist
  -BuildInstaller                Compile installer\SalrynSetup.iss via Inno Setup 6
  -Help                          Show this help

ISCC discovery order (when -BuildInstaller is set):
  1. -InnoSetupCompilerPath parameter
  2. INNO_SETUP_ISCC environment variable
  3. ISCC.exe on the system PATH
  4. %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
  5. C:\Program Files (x86)\Inno Setup 6\ISCC.exe
  6. C:\Program Files\Inno Setup 6\ISCC.exe

If Inno Setup 6 is not installed:
  Download and install the official Inno Setup 6 installer from https://jrsoftware.org/isinfo.php
  or set the INNO_SETUP_ISCC environment variable to the full path of ISCC.exe.
  Inno Setup is only needed by the person who builds the setup EXE, not by end users.
"@
    exit 0
}

function Find-RepoRoot {
    $current = (Resolve-Path $PSScriptRoot).Path
    while ($true) {
        if (Test-Path (Join-Path $current "Salryn.sln")) { return $current }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            throw "Could not locate Salryn.sln from $PSScriptRoot"
        }
        $current = $parent
    }
}

function Resolve-IsccPath {
    param([string]$ExplicitPath)

    # 1. Explicit parameter
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path -LiteralPath $ExplicitPath) { return $ExplicitPath }
        throw "ISCC.exe not found at the path supplied via -InnoSetupCompilerPath: $ExplicitPath"
    }

    # 2. Environment variable
    $fromEnv = [System.Environment]::GetEnvironmentVariable("INNO_SETUP_ISCC")
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        if (Test-Path -LiteralPath $fromEnv) { return $fromEnv }
        throw "ISCC.exe not found at path set in INNO_SETUP_ISCC environment variable: $fromEnv"
    }

    # 3. System PATH
    $fromPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    if ($fromPath -and (Test-Path -LiteralPath $fromPath)) { return $fromPath }

    # 4-6. Standard Inno Setup 6 installation directories
    $standardPaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $standardPaths) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    throw @"
Inno Setup 6 compiler (ISCC.exe) was not found.

To fix this, choose one of:
  A) Install Inno Setup 6 from the official installer:
       https://jrsoftware.org/isinfo.php
     After installation ISCC.exe will be at:
       C:\Program Files (x86)\Inno Setup 6\ISCC.exe
     and this script will find it automatically.

  B) Set the INNO_SETUP_ISCC environment variable to the full path of ISCC.exe:
       [System.Environment]::SetEnvironmentVariable('INNO_SETUP_ISCC', 'D:\Tools\InnoSetup6\ISCC.exe', 'User')

  C) Pass the path directly via the -InnoSetupCompilerPath parameter:
       -InnoSetupCompilerPath "D:\Tools\InnoSetup6\ISCC.exe"

Inno Setup is only needed by the person building the setup EXE.
End users receive Salryn-POS-BetaRC1-Setup.exe and do not need Inno Setup.
"@
}

# ---------------------------------------------------------------------------

$repoRoot = Find-RepoRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $artifactRoot = Join-Path $repoRoot "artifacts\installer"
} else {
    $artifactRoot = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $repoRoot $OutputDir }
}

$stagingRoot  = Join-Path $artifactRoot "staging"
$publishDir   = Join-Path $stagingRoot "Salryn"
$setupDir     = Join-Path $artifactRoot "setup"
$shellTemp    = Join-Path $artifactRoot "shell-publish"
$webDistSrc   = Join-Path $repoRoot "src\Salryn.App.Web\dist"
$stagedDist   = Join-Path $publishDir "dist"
$iconSource   = Join-Path $repoRoot "assets\branding\salryn-icon.ico"
$issScript    = Join-Path $repoRoot "installer\SalrynSetup.iss"
$webview2RedistDir = Join-Path $repoRoot "installer\redist"
$webview2Bootstrapper = Join-Path $webview2RedistDir "MicrosoftEdgeWebview2Setup.exe"
# Official Microsoft Evergreen Bootstrapper redirect (see installer/redist/README.md)
$webview2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"

New-Item -ItemType Directory -Force -Path $stagingRoot, $setupDir, $webview2RedistDir | Out-Null

if (-not (Test-Path -LiteralPath $iconSource)) {
    throw "Missing installer icon at $iconSource. Run: powershell -File tools\branding\Generate-SalrynBrandingAssets.ps1"
}

# ---------------------------------------------------------------------------
# WebView2 Evergreen Bootstrapper (required for -BuildInstaller)
# ---------------------------------------------------------------------------
function Ensure-WebView2Bootstrapper {
    if (Test-Path -LiteralPath $webview2Bootstrapper) {
        $hash = (Get-FileHash -Algorithm SHA256 -Path $webview2Bootstrapper).Hash.ToLowerInvariant()
        Write-Info "WebView2 bootstrapper present: $webview2Bootstrapper"
        Write-Info "WebView2 bootstrapper SHA-256: $hash"
        return
    }

    Write-Info "WebView2 bootstrapper missing; downloading from Microsoft..."
    Write-Info "URL: $webview2BootstrapperUrl"
    try {
        Invoke-WebRequest -Uri $webview2BootstrapperUrl -OutFile $webview2Bootstrapper -UseBasicParsing
    }
    catch {
        throw "Failed to download Microsoft Edge WebView2 Evergreen Bootstrapper from $webview2BootstrapperUrl. $_"
    }

    if (-not (Test-Path -LiteralPath $webview2Bootstrapper)) {
        throw "WebView2 bootstrapper download completed but file is missing: $webview2Bootstrapper"
    }

    $hash = (Get-FileHash -Algorithm SHA256 -Path $webview2Bootstrapper).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $webview2Bootstrapper).Length
    Write-Info "Downloaded WebView2 bootstrapper ($size bytes)"
    Write-Info "WebView2 bootstrapper SHA-256: $hash"
}

Ensure-WebView2Bootstrapper

# ---------------------------------------------------------------------------
# Web build
# ---------------------------------------------------------------------------
if (-not $SkipWebBuild) {
    Write-Info "Building React web (npm run build)..."
    npm --prefix (Join-Path $repoRoot "src\Salryn.App.Web") run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed (exit $LASTEXITCODE)" }
} else {
    Write-Info "SkipWebBuild set; reusing existing dist if present."
}

if (-not (Test-Path (Join-Path $webDistSrc "index.html"))) {
    throw "Web dist missing index.html at $webDistSrc. Run: npm --prefix src\Salryn.App.Web run build"
}

# ---------------------------------------------------------------------------
# Publish Salryn.Api.Local
# ---------------------------------------------------------------------------
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Info "Publishing Salryn.Api.Local..."
& dotnet publish (Join-Path $repoRoot "src\Salryn.Api.Local\Salryn.Api.Local.csproj") `
    -c $Configuration -o $publishDir --self-contained false `
    /p:UseAppHost=true /p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Salryn.Api.Local failed (exit $LASTEXITCODE)" }

# ---------------------------------------------------------------------------
# Publish Salryn.Desktop.WebView (into temp, then merge into stage)
# ---------------------------------------------------------------------------
Write-Info "Publishing Salryn.Desktop.WebView..."
if (Test-Path $shellTemp) { Remove-Item -Recurse -Force $shellTemp }
New-Item -ItemType Directory -Force -Path $shellTemp | Out-Null

& dotnet publish (Join-Path $repoRoot "src\Salryn.Desktop.WebView\Salryn.Desktop.WebView.csproj") `
    -c $Configuration -o $shellTemp --self-contained false `
    /p:UseAppHost=true /p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Salryn.Desktop.WebView failed (exit $LASTEXITCODE)" }

# Merge shell publish into the API stage. Use wildcard copy so existing
# directories (especially runtimes\) merge instead of nesting as runtimes\runtimes.
Copy-Item -Path (Join-Path $shellTemp '*') -Destination $publishDir -Recurse -Force

# WebView2Loader.dll is required for CoreWebView2Environment / GetAvailableBrowserVersionString.
# Place it under the RID path AND beside the EXE (most reliable for installed layout).
$webview2LoaderSrc = Join-Path $shellTemp "runtimes\win-x64\native\WebView2Loader.dll"
if (-not (Test-Path -LiteralPath $webview2LoaderSrc)) {
    # Fall back to any published loader under the shell output
    $webview2LoaderSrc = Get-ChildItem -Path $shellTemp -Recurse -Filter "WebView2Loader.dll" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'win-x64' } |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $webview2LoaderSrc -or -not (Test-Path -LiteralPath $webview2LoaderSrc)) {
    throw "Shell publish is missing WebView2Loader.dll under $shellTemp"
}

$webview2NativeDir = Join-Path $publishDir "runtimes\win-x64\native"
New-Item -ItemType Directory -Force -Path $webview2NativeDir | Out-Null
Copy-Item -Force $webview2LoaderSrc (Join-Path $webview2NativeDir "WebView2Loader.dll")
Copy-Item -Force $webview2LoaderSrc (Join-Path $publishDir "WebView2Loader.dll")
Write-Info "WebView2Loader staged: $(Join-Path $publishDir 'WebView2Loader.dll')"

# Also stage arm64/x86 loaders under RID paths when present (optional completeness)
foreach ($rid in @("win-arm64", "win-x86")) {
    $src = Join-Path $shellTemp "runtimes\$rid\native\WebView2Loader.dll"
    if (Test-Path -LiteralPath $src) {
        $destDir = Join-Path $publishDir "runtimes\$rid\native"
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        Copy-Item -Force $src (Join-Path $destDir "WebView2Loader.dll")
    }
}

$shellExe = Join-Path $publishDir "Salryn.Desktop.WebView.exe"
if (-not (Test-Path $shellExe)) {
    throw "Published app shell missing: $shellExe"
}
if (-not (Test-Path (Join-Path $publishDir "WebView2Loader.dll"))) {
    throw "Published app shell missing WebView2Loader.dll beside EXE."
}

# ---------------------------------------------------------------------------
# Stage React dist
# ---------------------------------------------------------------------------
if (Test-Path $stagedDist) { Remove-Item -Recurse -Force $stagedDist }
Copy-Item -Recurse -Force $webDistSrc $stagedDist

# ---------------------------------------------------------------------------
# Stage branding assets (icon referenced by installer shortcuts)
# ---------------------------------------------------------------------------
$brandingStage = Join-Path $publishDir "assets\branding"
New-Item -ItemType Directory -Force -Path $brandingStage | Out-Null
foreach ($asset in @("salryn-icon.ico","salryn-icon.png","salryn-banner-clear.png","salryn-banner-filled.png")) {
    Copy-Item -Force (Join-Path $repoRoot "assets\branding\$asset") (Join-Path $brandingStage $asset)
}

# ---------------------------------------------------------------------------
# Version stamp
# ---------------------------------------------------------------------------
@(
    "Product=Salryn POS"
    "Publisher=Kirjane Labs"
    "ProductVersion=$Version"
    "StagedUtc=$((Get-Date).ToUniversalTime().ToString('o'))"
    "Entry=Salryn.Desktop.WebView"
    "HostProcess=Salryn.Api.Local"
    "HostUrl=http://127.0.0.1:5299/"
    "WebDist=dist\"
    "InstallerIcon=assets\branding\salryn-icon.ico"
    "ProductionDataPath=%ProgramData%\Salryn\data\salryn.db"
    "WebView2=EvergreenBootstrapper"
    "WebView2Bootstrapper=installer\redist\MicrosoftEdgeWebview2Setup.exe"
) | Set-Content -Path (Join-Path $publishDir "salryn-beta-rc1-installer-stage.txt") -Encoding UTF8

Write-Host ""
Write-Info "Staging complete: $publishDir"

# ---------------------------------------------------------------------------
# Installer EXE compile (requires Inno Setup 6)
# ---------------------------------------------------------------------------
if (-not $BuildInstaller) {
    Write-Host ""
    Write-Host "BETA RC1 INSTALLER STAGE COMPLETE (staging only)"
    Write-Host "App stage : $publishDir"
    Write-Host "Setup dir : $setupDir"
    Write-Host ""
    Write-Host "Installer EXE not compiled because -BuildInstaller was not requested."
    Write-Host "To compile: rerun with -BuildInstaller (requires Inno Setup 6)."
    exit 0
}

Write-Info "Resolving Inno Setup 6 compiler (ISCC.exe)..."
$iscc = Resolve-IsccPath -ExplicitPath $InnoSetupCompilerPath

if (-not (Test-Path -LiteralPath $webview2Bootstrapper)) {
    throw "WebView2 bootstrapper required for installer compile is missing: $webview2Bootstrapper"
}

Write-Info "Found ISCC: $iscc"
Write-Info "Compiling $issScript ..."

& $iscc $issScript
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$setupExe = Get-ChildItem -Path $setupDir -Filter "*.exe" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $setupExe) {
    throw "Setup EXE not found in $setupDir after ISCC run."
}

$sha256 = (Get-FileHash -Algorithm SHA256 -Path $setupExe.FullName).Hash.ToLowerInvariant()
$shaFile = "$($setupExe.FullName).sha256.txt"
@(
    "File=$($setupExe.Name)"
    "SHA256=$sha256"
    "Version=$Version"
    "ComputedUtc=$((Get-Date).ToUniversalTime().ToString('o'))"
    "Note=Attach this file alongside the setup EXE in the GitHub Release."
) | Set-Content -Path $shaFile -Encoding UTF8

Write-Host ""
Write-Host "BETA RC1 INSTALLER EXE COMPILED"
Write-Host "Setup EXE : $($setupExe.FullName)"
Write-Host "SHA-256   : $sha256"
Write-Host "SHA file  : $shaFile"
Write-Host "App stage : $publishDir"

exit 0
