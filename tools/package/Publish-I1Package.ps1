# I1 package/publish proof (not a public installer).
# Builds React production dist, publishes Salryn.Api.Local, stages dist beside the API exe.
# Output: artifacts/i1-package/Salryn/

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "",
    [switch]$SkipWebBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "Salryn.sln"))) {
    throw "Could not locate Salryn.sln from tools/package."
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\i1-package\Salryn"
}

$webProject = Join-Path $repoRoot "src\Salryn.App.Web"
$apiProject = Join-Path $repoRoot "src\Salryn.Api.Local\Salryn.Api.Local.csproj"
$webDistSource = Join-Path $webProject "dist"

Write-Host "=== Salryn I1 package publish ==="
Write-Host "Repo:    $repoRoot"
Write-Host "Output:  $OutputRoot"
Write-Host "Config:  $Configuration"

if (-not $SkipWebBuild) {
    Write-Host "Building web production dist..."
    npm --prefix $webProject run build
    if ($LASTEXITCODE -ne 0) {
        throw "npm web build failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $webDistSource "index.html"))) {
    throw "Web dist missing index.html at $webDistSource. Run npm --prefix src/Salryn.App.Web run build first."
}

if (Test-Path -LiteralPath $OutputRoot) {
    Write-Host "Cleaning previous package output..."
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

Write-Host "Publishing Salryn.Api.Local ($Configuration)..."
dotnet publish $apiProject `
    -c $Configuration `
    -o $OutputRoot `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$packageDist = Join-Path $OutputRoot "dist"
Write-Host "Staging web dist -> $packageDist"
if (Test-Path -LiteralPath $packageDist) {
    Remove-Item -LiteralPath $packageDist -Recurse -Force
}
Copy-Item -LiteralPath $webDistSource -Destination $packageDist -Recurse

$stampPath = Join-Path $OutputRoot "salryn-i1-package-stamp.txt"
$gitSha = ""
try {
    Push-Location $repoRoot
    $gitSha = (git rev-parse HEAD 2>$null)
}
finally {
    Pop-Location
}

@(
    "Product=Salryn POS"
    "Lane=I1-PACKAGE-INSTALL-PROOF"
    "StagedUtc=$(Get-Date -Format 'yyyy-MM-ddTHH:mm:ss.fffZ')"
    "Configuration=$Configuration"
    "PublishMode=framework-dependent"
    "Entry=Salryn.Api.Local.exe"
    "HostUrl=http://127.0.0.1:5299/"
    "WebDist=dist\"
    "GitSha=$gitSha"
    "NotPublicInstaller=true"
    "NotAutoUpdater=true"
    "NotCloudActivation=true"
) | Set-Content -LiteralPath $stampPath -Encoding UTF8

$exe = Join-Path $OutputRoot "Salryn.Api.Local.exe"
$index = Join-Path $packageDist "index.html"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Package missing Salryn.Api.Local.exe"
}
if (-not (Test-Path -LiteralPath $index)) {
    throw "Package missing dist\index.html"
}

Write-Host ""
Write-Host "I1 package ready:"
Write-Host "  $OutputRoot"
Write-Host "Launch (dev data isolation for proof):"
Write-Host "  cd `"$OutputRoot`""
Write-Host "  .\Salryn.Api.Local.exe --dev"
Write-Host "Then open http://127.0.0.1:5299/"
Write-Host "Done."
