# Generates Salryn Beta RC1 branding assets from the approved source icon PNG.
# Run from repo root: powershell -File tools/branding/Generate-SalrynBrandingAssets.ps1

[CmdletBinding()]
param(
    [string]$SourceIcon = "C:\Users\kirch\Desktop\Salryn Icon\icon type.png",
    [string]$SourceBannerClear = "C:\Users\kirch\Desktop\Salryn Icon\banner type clear.png",
    [string]$SourceBannerFilled = "C:\Users\kirch\Desktop\Salryn Icon\banner type filled.png"
)

$ErrorActionPreference = "Stop"

function Find-RepoRoot {
    $current = (Resolve-Path $PSScriptRoot).Path
    while ($true) {
        if (Test-Path (Join-Path $current "Salryn.sln")) {
            return $current
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            throw "Could not locate Salryn.sln from $PSScriptRoot"
        }
        $current = $parent
    }
}

$repoRoot = Find-RepoRoot
$toolProject = Join-Path $repoRoot "tools\Salryn.Tools.BrandingAssets\Salryn.Tools.BrandingAssets.csproj"

foreach ($path in @($SourceIcon, $SourceBannerClear, $SourceBannerFilled)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing approved source asset: $path"
    }
}

& dotnet run --project $toolProject -- `
    $repoRoot `
    $SourceIcon `
    $SourceBannerClear `
    $SourceBannerFilled

if ($LASTEXITCODE -ne 0) {
    throw "Branding asset generation failed with exit code $LASTEXITCODE"
}
