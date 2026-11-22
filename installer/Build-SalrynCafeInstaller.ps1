param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$AppVersion = "1.0.0-rc1",
    [string]$IsccPath = "",
    [switch]$SkipWebBuild,
    [switch]$IncludeLegacyRootShell,
    [switch]$FrameworkDependent
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

function Invoke-WebDistBuild {
    param([string]$RelativeDir)
    $dir = Join-Path $repoRoot $RelativeDir
    if (-not (Test-Path (Join-Path $dir "package.json"))) {
        throw "Frontend package.json not found under $dir"
    }
    Push-Location $dir
    try {
        if (-not (Test-Path (Join-Path $dir "node_modules"))) {
            Write-Host "[Salryn Cafe Installer] Restoring npm dependencies in $RelativeDir..."
            npm ci --ignore-scripts --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed in $RelativeDir ($LASTEXITCODE)" }
        }
        Write-Host "[Salryn Cafe Installer] Building frontend bundle in $RelativeDir..."
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed in $RelativeDir ($LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
}

if (-not $SkipWebBuild) {
    Invoke-WebDistBuild "apps\cafe-web"
    if ($IncludeLegacyRootShell) {
        Invoke-WebDistBuild "src\Salryn.App.Web"
    }
}

$cafeDistSrc = Join-Path $repoRoot "apps\cafe-web\dist"
if (-not (Test-Path (Join-Path $cafeDistSrc "index.html"))) {
    throw "Required live Cafe frontend bundle missing at $cafeDistSrc\index.html"
}

$rev = (& git -C $repoRoot rev-parse HEAD 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $rev -notmatch '^[0-9a-f]{40}$') {
    throw "Unable to resolve full 40-character Git HEAD SHA for installer provenance."
}

if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir
}
New-Item -ItemType Directory -Force -Path $stagingDir, $outputDir | Out-Null

$selfContainedArg = if ($FrameworkDependent) { "false" } else { "true" }

Write-Host "[Salryn Cafe Installer] Publishing Salryn.Desktop.WebView ($Configuration / $Runtime / self-contained=$selfContainedArg)..."
dotnet publish (Join-Path $repoRoot "src\Salryn.Desktop.WebView\Salryn.Desktop.WebView.csproj") `
    -c $Configuration -r $Runtime --self-contained $selfContainedArg "-p:SourceRevisionId=$rev" -o $stagingDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Salryn.Desktop.WebView failed ($LASTEXITCODE)" }

Write-Host "[Salryn Cafe Installer] Publishing Salryn.Api.Local ($Configuration / $Runtime / self-contained=$selfContainedArg)..."
dotnet publish (Join-Path $repoRoot "src\Salryn.Api.Local\Salryn.Api.Local.csproj") `
    -c $Configuration -r $Runtime --self-contained $selfContainedArg "-p:SourceRevisionId=$rev" -o $stagingDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Salryn.Api.Local failed ($LASTEXITCODE)" }

if (-not $FrameworkDependent) {
    $depsPath = Join-Path $stagingDir "Salryn.Api.Local.deps.json"
    $pkcsDll = Join-Path $stagingDir "System.Security.Cryptography.Pkcs.dll"
    if (-not (Test-Path -LiteralPath $depsPath -PathType Leaf) -or -not (Test-Path -LiteralPath $pkcsDll -PathType Leaf)) {
        throw "Self-contained staging missing Salryn.Api.Local.deps.json or System.Security.Cryptography.Pkcs.dll"
    }
    $depsText = Get-Content -LiteralPath $depsPath -Raw
    if ($depsText -notmatch '"System\.Security\.Cryptography\.Pkcs/([0-9]+\.[0-9]+\.[0-9]+)"') {
        throw "Could not resolve expected System.Security.Cryptography.Pkcs version from Salryn.Api.Local.deps.json."
    }
    $expectedPkcs = $Matches[1]
    $pkcsInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($pkcsDll)
    if (-not ($pkcsInfo.ProductVersion -and $pkcsInfo.ProductVersion -match ("^" + [regex]::Escape($expectedPkcs) + "(\+|-|$)"))) {
        throw "Staged System.Security.Cryptography.Pkcs.dll ProductVersion '$($pkcsInfo.ProductVersion)' does not match Salryn.Api.Local.deps.json '$expectedPkcs'."
    }
    Write-Host "[Salryn Cafe Installer] Verified System.Security.Cryptography.Pkcs.dll ($($pkcsInfo.ProductVersion))"
}

$cafeDistDst = Join-Path $stagingDir "cafe-dist"
New-Item -ItemType Directory -Force -Path $cafeDistDst | Out-Null
Copy-Item -Path (Join-Path $cafeDistSrc "*") -Destination $cafeDistDst -Recurse -Force

if ($IncludeLegacyRootShell) {
    $legacyDistSrc = Join-Path $repoRoot "src\Salryn.App.Web\dist"
    if (-not (Test-Path (Join-Path $legacyDistSrc "index.html"))) {
        throw "Legacy host shell dist missing at $legacyDistSrc\index.html"
    }
    $legacyDistDst = Join-Path $stagingDir "wwwroot"
    New-Item -ItemType Directory -Force -Path $legacyDistDst | Out-Null
    Copy-Item -Path (Join-Path $legacyDistSrc "*") -Destination $legacyDistDst -Recurse -Force
}

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

$provenance = ([ordered]@{
    productId = "Salryn.Cafe"
    sourceSha = $rev
    configuration = $Configuration
    runtime = $Runtime
    selfContained = (-not $FrameworkDependent)
    appVersion = $AppVersion
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 4)
[IO.File]::WriteAllText((Join-Path $stagingDir "build-provenance.json"), $provenance)

foreach ($requiredRel in @(
    "Salryn.Api.Local.exe",
    "Salryn.Desktop.WebView.exe",
    "cafe-dist\index.html",
    "assets\branding\salryn-icon.ico",
    "build-provenance.json"
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $stagingDir $requiredRel) -PathType Leaf)) {
        throw "Incomplete installer staging directory: missing $requiredRel"
    }
}

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
Write-Host "[Salryn Cafe Installer] Source SHA: $rev"
Write-Host "[Salryn Cafe Installer] SHA-256: $hash"
