# Salryn POS Beta RC1 — source-bound staging and installer build script.
#
# Enforces source custody (Git HEAD SHA, clean worktree, optional -ExpectedSourceSha)
# before any network download, staging cleanup, web build, .NET publish, or packaging.
# Emits a deterministic SHA-bound manifest inside staging (packaged into {app}) and
# binds the compiled setup EXE to the verified source commit and stage inventory.
#
# ISCC discovery order (when -BuildInstaller is requested):
#   1. -InnoSetupCompilerPath parameter
#   2. INNO_SETUP_ISCC environment variable
#   3. ISCC.exe on the system PATH
#   4. %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe  (per-user install)
#   5. C:\Program Files (x86)\Inno Setup 6\ISCC.exe
#   6. C:\Program Files\Inno Setup 6\ISCC.exe
#
# Usage:
#   # Stage only (clean worktree required):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1
#
#   # Stage + compile setup EXE:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller
#
#   # Stage + compile asserting expected 40-char Git commit SHA:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer\Build-SalrynBetaRc1Installer.ps1 -BuildInstaller -ExpectedSourceSha <40-hex-sha>

[CmdletBinding()]
param(
    [string]$Version = "0.1.0-rc1",
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [string]$InnoSetupCompilerPath = "",
    [string]$ExpectedSourceSha = "",
    [string]$ExpectedStageManifestSha256 = "",
    [string[]]$AdditionalProtectedRoots = @(),
    [switch]$SkipWebBuild,
    [switch]$BuildInstaller,
    [switch]$CompileExistingStageOnly,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$Message) { Write-Host "[installer-rc1] $Message" }

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Write-ProvenanceFailure {
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$Detail
    )
    $safeSha = if ([string]::IsNullOrWhiteSpace($HeadSha)) { "UNKNOWN" } else { $HeadSha }
    $msg = "[PROVENANCE_ERROR] code=$Code head=$safeSha detail=$Detail"
    Write-Host $msg
    throw $msg
}

if ($Help) {
    Write-Host @"
Build-SalrynBetaRc1Installer.ps1

  Stages Salryn.Desktop.WebView, Salryn.Api.Local, branding assets, and React dist
  from a verified clean Git commit, generates salryn-installer-provenance-manifest.json,
  and compiles installer\SalrynSetup.iss via Inno Setup 6 when -BuildInstaller is set.

Parameters:
  -Version <semver>                    Product version (default 0.1.0-rc1)
  -Configuration <name>                dotnet publish configuration (default Release)
  -OutputDir <path>                    Artifact root (default artifacts\installer)
  -InnoSetupCompilerPath <path>        Explicit path to ISCC.exe (overrides auto-discovery)
  -ExpectedSourceSha <40-hex>          Require HEAD to exactly match this 40-char Git SHA
  -ExpectedStageManifestSha256 <hex>   Required 64-hex manifest SHA256 when -CompileExistingStageOnly is set
  -AdditionalProtectedRoots <paths>    Extra directory roots treated as protected output boundaries
  -SkipWebBuild                        Reuse src\Salryn.App.Web\dist ONLY if its .salryn-web-build-provenance.json
                                       stamp matches HEAD, source inputs, and every byte in dist
  -BuildInstaller                      Verify stage custody and compile installer\SalrynSetup.iss via Inno Setup 6
  -CompileExistingStageOnly            Requires -ExpectedSourceSha AND external -ExpectedStageManifestSha256
  -Help                                Show this help
"@
    exit 0
}

function Find-RepoRoot {
    $current = (Resolve-Path $PSScriptRoot).Path
    while ($true) {
        if (Test-Path -LiteralPath (Join-Path $current "Salryn.sln")) { return $current }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            throw "Could not locate Salryn.sln from $PSScriptRoot"
        }
        $current = $parent
    }
}

if (-not ("SalrynNativePathResolver" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class SalrynNativePathResolver
{
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint VOLUME_NAME_DOS = 0x0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    public static string GetFinalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty.");

        using (SafeFileHandle handle = CreateFileW(
            path,
            0,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero))
        {
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open path for canonical resolution: " + path);

            StringBuilder sb = new StringBuilder(512);
            uint len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, VOLUME_NAME_DOS);
            if (len == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFinalPathNameByHandleW failed: " + path);

            if (len >= sb.Capacity)
            {
                sb = new StringBuilder((int)len + 2);
                len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, VOLUME_NAME_DOS);
                if (len == 0 || len >= sb.Capacity)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFinalPathNameByHandleW buffer resize failed: " + path);
            }

            string raw = sb.ToString();
            if (raw.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
                return "\\\\" + raw.Substring(8);
            if (raw.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase))
                return raw.Substring(4);
            return raw;
        }
    }
}
"@ -ErrorAction Stop
}

function Normalize-PathWithSeparator {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full -match '^[A-Za-z]:$') {
        return "$full\"
    }
    return "$full\"
}

function Get-ProtectedRootsList {
    param([string[]]$ExtraProtectedRoots = @())
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramData)) {
        $candidates.Add((Join-Path $env:ProgramData "Salryn"))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates.Add((Join-Path $env:LOCALAPPDATA "Salryn"))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates.Add((Join-Path $env:ProgramFiles "Kirjane Information Technology Services"))
    }
    $pf86 = ${env:ProgramFiles(x86)}
    if (-not [string]::IsNullOrWhiteSpace($pf86)) {
        $candidates.Add((Join-Path $pf86 "Kirjane Information Technology Services"))
    }
    foreach ($extra in @($ExtraProtectedRoots)) {
        if (-not [string]::IsNullOrWhiteSpace($extra)) {
            $candidates.Add($extra)
        }
    }
    $fromEnv = [System.Environment]::GetEnvironmentVariable("SALRYN_EXTRA_PROTECTED_ROOTS")
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        foreach ($seg in ($fromEnv -split ';')) {
            if (-not [string]::IsNullOrWhiteSpace($seg)) {
                $candidates.Add($seg.Trim())
            }
        }
    }

    $expanded = New-Object System.Collections.Generic.List[string]
    foreach ($c in $candidates) {
        $lex = [System.IO.Path]::GetFullPath($c).TrimEnd('\', '/')
        $expanded.Add($lex)
        if (Test-Path -LiteralPath $lex) {
            try {
                $resolved = ([SalrynNativePathResolver]::GetFinalPath($lex)).TrimEnd('\', '/')
                if (-not [string]::IsNullOrWhiteSpace($resolved) -and -not $resolved.Equals($lex, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $expanded.Add($resolved)
                }
            } catch {
                # Keep lexical root even if handle query fails
            }
        }
    }
    return ,@($expanded | Select-Object -Unique)
}

function Assert-SupportedLocalDrivePath {
    param(
        [Parameter(Mandatory = $true)][string]$RawPath,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    if ([string]::IsNullOrWhiteSpace($RawPath)) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "${ContextLabel} cannot be empty."
    }
    if ($RawPath.StartsWith("\\", [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "UNC/device path not supported for ${ContextLabel}: $RawPath"
    }
    $full = [System.IO.Path]::GetFullPath($RawPath)
    if ($full -notmatch '^[A-Za-z]:\\') {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "${ContextLabel} must resolve to a local drive-letter path: $full"
    }
    $driveRoot = $full.Substring(0, 3)
    try {
        $driveInfo = New-Object System.IO.DriveInfo($driveRoot)
        if ($driveInfo.DriveType -eq [System.IO.DriveType]::Network -or
            $driveInfo.DriveType -eq [System.IO.DriveType]::Unknown) {
            Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Unsupported drive type '$($driveInfo.DriveType)' for ${ContextLabel}: $full"
        }
    } catch {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Failed to validate drive for ${ContextLabel}: $_"
    }
    return $full
}

function Resolve-CanonicalPathThroughAncestors {
    param(
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    $full = Assert-SupportedLocalDrivePath -RawPath $TargetPath -HeadSha $HeadSha -ContextLabel $ContextLabel
    $trimmed = $full.TrimEnd('\')
    if ($trimmed -match '^[A-Za-z]:$') {
        $trimmed = "$trimmed\"
    }

    # Walk top-down through every component from drive root to target leaf (including non-existent children)
    $root = [System.IO.Path]::GetPathRoot($full)
    $remainder = $trimmed.Substring($root.Length)
    $segments = if ([string]::IsNullOrEmpty($remainder)) { @() } else { $remainder.Split('\') }

    $current = $root.TrimEnd('\') + "\"
    $deepestExisting = $current
    $nonExistentParts = New-Object System.Collections.Generic.List[string]
    $sawNonExistent = $false

    $rootItem = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
    if ($null -eq $rootItem) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Drive root unavailable for ${ContextLabel}: $current"
    }
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point/junction/symlink forbidden in ${ContextLabel} ancestry: $current"
    }

    foreach ($seg in $segments) {
        $current = Join-Path $current $seg
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item) {
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point/junction/symlink forbidden in ${ContextLabel} ancestry: $($item.FullName)"
            }
            if ($sawNonExistent) {
                Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Inconsistent filesystem state along ${ContextLabel} ancestry: $current"
            }
            $deepestExisting = $current
        } else {
            $sawNonExistent = $true
            $nonExistentParts.Add($seg)
        }
    }

    try {
        $kernelResolved = [SalrynNativePathResolver]::GetFinalPath($deepestExisting)
    } catch {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Failed canonical path resolution for ${ContextLabel} ($deepestExisting): $_"
    }

    if ($kernelResolved.StartsWith("\\", [System.StringComparison]::OrdinalIgnoreCase) -or
        $kernelResolved -notmatch '^[A-Za-z]:\\') {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "Canonical resolution left supported local drive namespace for ${ContextLabel}: $kernelResolved"
    }

    # Verify kernel-resolved deepest existing ancestor itself has no reparse attribute
    $resolvedWalk = $kernelResolved
    while (-not [string]::IsNullOrWhiteSpace($resolvedWalk)) {
        $rItem = Get-Item -LiteralPath $resolvedWalk -Force -ErrorAction SilentlyContinue
        if ($null -ne $rItem -and (($rItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
            Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Canonical target ancestry contains reparse point for ${ContextLabel}: $resolvedWalk"
        }
        $parentWalk = Split-Path -Parent $resolvedWalk
        if ([string]::IsNullOrWhiteSpace($parentWalk) -or $parentWalk -eq $resolvedWalk) { break }
        $resolvedWalk = $parentWalk
    }

    $canonicalFull = $kernelResolved
    foreach ($part in $nonExistentParts) {
        $canonicalFull = Join-Path $canonicalFull $part
    }

    return [ordered]@{
        LexicalFull     = $full
        CanonicalFull   = [System.IO.Path]::GetFullPath($canonicalFull)
        DeepestExisting = $deepestExisting
    }
}

function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-ProvenanceFailure -Code "REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point/symlink forbidden in ${ContextLabel}: $($item.FullName)"
    }
}

function Assert-NoReparsePointsOrTraversal {
    param(
        [Parameter(Mandatory = $true)][string]$RootDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    if (-not (Test-Path -LiteralPath $RootDir)) {
        Write-ProvenanceFailure -Code "MISSING_REQUIRED_DIRECTORY" -HeadSha $HeadSha -Detail "Required directory missing in ${ContextLabel}: $RootDir"
    }
    Assert-NotReparsePoint -Path $RootDir -HeadSha $HeadSha -ContextLabel $ContextLabel
    $canonicalPrefix = Normalize-PathWithSeparator -Path $RootDir

    $items = Get-ChildItem -LiteralPath $RootDir -Recurse -Force -ErrorAction Stop
    foreach ($item in $items) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            Write-ProvenanceFailure -Code "REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point/symlink forbidden in ${ContextLabel}: $($item.FullName)"
        }
        $resolvedFull = [System.IO.Path]::GetFullPath($item.FullName)
        if (-not $resolvedFull.StartsWith($canonicalPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Path escapes ${ContextLabel} root: $resolvedFull"
        }
    }
}

function Assert-SafeManifestRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$RootDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ContextLabel
    )
    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Empty relativePath in ${ContextLabel}."
    }
    if ($RelativePath.StartsWith("/") -or $RelativePath.StartsWith("\") -or $RelativePath.Contains("\") -or $RelativePath -match '^[A-Za-z]:') {
        Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Non-normalized relativePath in ${ContextLabel}: '$RelativePath'"
    }
    $segments = $RelativePath.Split('/')
    foreach ($seg in $segments) {
        if ([string]::IsNullOrEmpty($seg) -or $seg -eq "." -or $seg -eq "..") {
            Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Path traversal segment in ${ContextLabel}: '$RelativePath'"
        }
    }
    $canonicalPrefix = Normalize-PathWithSeparator -Path $RootDir
    $combined = [System.IO.Path]::GetFullPath((Join-Path $RootDir ($RelativePath -replace '/', '\')))
    if (-not $combined.StartsWith($canonicalPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-ProvenanceFailure -Code "PATH_TRAVERSAL_FORBIDDEN" -HeadSha $HeadSha -Detail "Manifest relativePath escapes root in ${ContextLabel}: '$RelativePath'"
    }
}

function Get-Sha256HexFromString {
    param([Parameter(Mandatory = $true)][string]$Text)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $bytes = $utf8NoBom.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
        return ([System.BitConverter]::ToString($hashBytes) -replace '-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Get-FileSha256Lower {
    param([Parameter(Mandatory = $true)][string]$FilePath)
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $FilePath).Hash.ToLowerInvariant()
}

function Get-DirectoryFileHashInventory {
    param(
        [Parameter(Mandatory = $true)][string]$RootDir,
        [string[]]$ExcludeRelativePaths = @(),
        [switch]$IncludeBinaryVersions
    )
    $canonicalPrefix = Normalize-PathWithSeparator -Path $RootDir
    $excludeSet = @{}
    foreach ($ex in $ExcludeRelativePaths) {
        if (-not [string]::IsNullOrWhiteSpace($ex)) {
            $excludeSet[$ex.Replace('\', '/').ToLowerInvariant()] = $true
        }
    }

    $rawFiles = Get-ChildItem -LiteralPath $RootDir -Recurse -File -Force -ErrorAction Stop
    $entries = New-Object System.Collections.Generic.List[object]

    foreach ($file in $rawFiles) {
        $full = [System.IO.Path]::GetFullPath($file.FullName)
        $rel = $full.Substring($canonicalPrefix.Length).Replace('\', '/')
        if ($excludeSet.ContainsKey($rel.ToLowerInvariant())) {
            continue
        }
        $sha256 = Get-FileSha256Lower -FilePath $full
        $entry = [ordered]@{
            relativePath = $rel
            bytes        = [int64]$file.Length
            sha256       = $sha256
        }
        if ($IncludeBinaryVersions -and ($file.Extension -eq ".exe" -or $file.Extension -eq ".dll")) {
            $fvi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($full)
            $entry["fileVersion"] = $fvi.FileVersion
            $entry["productVersion"] = $fvi.ProductVersion
        }
        $entries.Add($entry)
    }

    $sorted = $entries | Sort-Object -Property { $_.relativePath } -Culture ([System.Globalization.CultureInfo]::InvariantCulture)
    return ,@($sorted)
}

function Get-CanonicalInventorySha256 {
    param([AllowEmptyCollection()][array]$InventoryEntries)
    $sb = New-Object System.Text.StringBuilder
    foreach ($entry in $InventoryEntries) {
        [void]$sb.Append($entry.relativePath).Append("`n").Append([string]$entry.bytes).Append("`n").Append($entry.sha256).Append("`n")
    }
    return (Get-Sha256HexFromString -Text $sb.ToString())
}

function Get-WebSourceInputDigest {
    param(
        [Parameter(Mandatory = $true)][string]$WebRoot,
        [Parameter(Mandatory = $true)][string]$HeadSha
    )
    if (-not (Test-Path -LiteralPath $WebRoot)) {
        Write-ProvenanceFailure -Code "MISSING_WEB_ROOT" -HeadSha $HeadSha -Detail "Web project folder missing: $WebRoot"
    }
    Assert-NotReparsePoint -Path $WebRoot -HeadSha $HeadSha -ContextLabel "web-root"
    $canonicalPrefix = Normalize-PathWithSeparator -Path $WebRoot
    $entries = New-Object System.Collections.Generic.List[object]

    $topItems = Get-ChildItem -LiteralPath $WebRoot -Force -ErrorAction Stop | Where-Object {
        $_.Name -ne "node_modules" -and $_.Name -ne "dist" -and $_.Name -ne "tsconfig.tsbuildinfo"
    }
    $allFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    foreach ($top in $topItems) {
        if (($top.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            Write-ProvenanceFailure -Code "REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point in web source: $($top.FullName)"
        }
        if ($top.PSIsContainer) {
            foreach ($child in (Get-ChildItem -LiteralPath $top.FullName -Recurse -Force -ErrorAction Stop)) {
                if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    Write-ProvenanceFailure -Code "REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point in web source: $($child.FullName)"
                }
                if (-not $child.PSIsContainer -and $child.Name -ne "tsconfig.tsbuildinfo") {
                    $allFiles.Add($child)
                }
            }
        } else {
            $allFiles.Add($top)
        }
    }

    foreach ($file in $allFiles) {
        $full = [System.IO.Path]::GetFullPath($file.FullName)
        $rel = $full.Substring($canonicalPrefix.Length).Replace('\', '/')
        $entries.Add([ordered]@{
            relativePath = $rel
            bytes        = [int64]$file.Length
            sha256       = (Get-FileSha256Lower -FilePath $full)
        })
    }

    $sorted = @($entries | Sort-Object -Property { $_.relativePath } -Culture ([System.Globalization.CultureInfo]::InvariantCulture))
    return (Get-CanonicalInventorySha256 -InventoryEntries $sorted)
}

function Resolve-GitSourceProvenance {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$ExpectedSha
    )
    Assert-NotReparsePoint -Path $RepoRoot -HeadSha "UNKNOWN" -ContextLabel "repo-root"
    $gitCmd = Get-Command "git" -ErrorAction SilentlyContinue
    if (-not $gitCmd) {
        Write-ProvenanceFailure -Code "GIT_UNAVAILABLE" -HeadSha "UNKNOWN" -Detail "git executable was not found on PATH."
    }

    $inside = (& git -C $RepoRoot rev-parse --is-inside-work-tree 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $inside -ne "true") {
        Write-ProvenanceFailure -Code "NOT_A_GIT_WORKTREE" -HeadSha "UNKNOWN" -Detail "Repository root is not a valid Git worktree: $RepoRoot"
    }

    $headRaw = (& git -C $RepoRoot rev-parse --verify HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $headRaw -notmatch '^[0-9a-fA-F]{40}$') {
        Write-ProvenanceFailure -Code "INVALID_GIT_HEAD" -HeadSha "UNKNOWN" -Detail "Could not resolve full 40-character HEAD commit SHA (got '$headRaw')."
    }
    $headSha = $headRaw.ToLowerInvariant()

    $branchRaw = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branchRaw)) {
        Write-ProvenanceFailure -Code "INVALID_GIT_BRANCH" -HeadSha $headSha -Detail "Could not resolve Git branch for HEAD."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) {
        $trimmedExpected = $ExpectedSha.Trim()
        if ($trimmedExpected -notmatch '^[0-9a-fA-F]{40}$') {
            Write-ProvenanceFailure -Code "EXPECTED_SHA_INVALID" -HeadSha $headSha -Detail "-ExpectedSourceSha must be a full 40-character hexadecimal SHA; got '$ExpectedSha'."
        }
        if ($trimmedExpected.ToLowerInvariant() -ne $headSha) {
            Write-ProvenanceFailure -Code "EXPECTED_SHA_MISMATCH" -HeadSha $headSha -Detail "Expected source SHA '$($trimmedExpected.ToLowerInvariant())' does not match HEAD '$headSha'."
        }
    }

    $statusOutput = @(& git -C $RepoRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        Write-ProvenanceFailure -Code "GIT_STATUS_FAILED" -HeadSha $headSha -Detail "git status --porcelain failed with exit code $LASTEXITCODE."
    }
    $dirtyLines = @($statusOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($dirtyLines.Count -gt 0) {
        $sample = ($dirtyLines | Select-Object -First 10) -join "; "
        Write-ProvenanceFailure -Code "DIRTY_WORKTREE" -HeadSha $headSha -Detail "Worktree contains $($dirtyLines.Count) dirty/untracked item(s): $sample"
    }

    return [ordered]@{
        HeadSha      = $headSha
        GitBranch    = $branchRaw
        DetachedHead = ($branchRaw -eq "HEAD")
        WorktreeClean = $true
    }
}

function Assert-OutputDirSafe {
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [string[]]$ExtraProtectedRoots = @()
    )
    $resolvedArtifact = Resolve-CanonicalPathThroughAncestors -TargetPath $ArtifactRoot -HeadSha $HeadSha -ContextLabel "OutputDir"
    $resolvedRepo = Resolve-CanonicalPathThroughAncestors -TargetPath $RepoRoot -HeadSha $HeadSha -ContextLabel "RepoRoot"

    $candidateArtifactPaths = @(
        ($resolvedArtifact.LexicalFull),
        ($resolvedArtifact.CanonicalFull)
    ) | Select-Object -Unique

    # Check child lifecycle subpaths if they already exist on disk
    foreach ($subName in @("staging", "staging\Salryn", "setup", "shell-publish")) {
        foreach ($base in $candidateArtifactPaths) {
            $subFull = Join-Path $base $subName
            $subItem = Get-Item -LiteralPath $subFull -Force -ErrorAction SilentlyContinue
            if ($null -ne $subItem -and (($subItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
                Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Reparse point/junction/symlink forbidden in OutputDir child: $subFull"
            }
        }
    }

    $repoCandidates = @(
        (Normalize-PathWithSeparator -Path $resolvedRepo.LexicalFull),
        (Normalize-PathWithSeparator -Path $resolvedRepo.CanonicalFull)
    ) | Select-Object -Unique
    $allowedRepoArtifactsNorms = @(
        (Normalize-PathWithSeparator -Path (Join-Path $resolvedRepo.LexicalFull "artifacts")),
        (Normalize-PathWithSeparator -Path (Join-Path $resolvedRepo.CanonicalFull "artifacts"))
    ) | Select-Object -Unique

    foreach ($candPath in $candidateArtifactPaths) {
        $candNorm = Normalize-PathWithSeparator -Path $candPath
        foreach ($repoNorm in $repoCandidates) {
            if ($candNorm.Equals($repoNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
                $repoNorm.StartsWith($candNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "OutputDir cannot be the repository root or an ancestor of it: $candPath"
            }
            if ($candNorm.StartsWith($repoNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                $underArtifacts = $false
                foreach ($artNorm in $allowedRepoArtifactsNorms) {
                    if ($candNorm.StartsWith($artNorm, [System.StringComparison]::OrdinalIgnoreCase) -and
                        -not $candNorm.Equals($artNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $underArtifacts = $true
                    }
                }
                if (-not $underArtifacts) {
                    Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $HeadSha -Detail "OutputDir inside repository must reside strictly beneath artifacts\: $candPath"
                }
            }
        }
    }

    $protectedRoots = Get-ProtectedRootsList -ExtraProtectedRoots $ExtraProtectedRoots
    foreach ($candPath in $candidateArtifactPaths) {
        $artifactNorm = Normalize-PathWithSeparator -Path $candPath
        foreach ($prot in $protectedRoots) {
            $protNorm = Normalize-PathWithSeparator -Path $prot
            if ($artifactNorm.StartsWith($protNorm, [System.StringComparison]::OrdinalIgnoreCase) -or
                $protNorm.StartsWith($artifactNorm, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-ProvenanceFailure -Code "PROTECTED_OUTPUT_DIR_FORBIDDEN" -HeadSha $HeadSha -Detail "OutputDir overlaps or resolves to protected Salryn/stand-in directory '$prot' (resolved='$candPath')"
            }
        }
    }

    return $resolvedArtifact
}

function Remove-StaleSetupArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$SetupDir,
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [string[]]$ExtraProtectedRoots = @()
    )
    [void](Assert-OutputDirSafe -ArtifactRoot $ArtifactRoot -RepoRoot $RepoRoot -HeadSha $HeadSha -ExtraProtectedRoots $ExtraProtectedRoots)
    $setupDirItem = Get-Item -LiteralPath $SetupDir -Force -ErrorAction SilentlyContinue
    if ($null -eq $setupDirItem) { return }
    if (($setupDirItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Setup directory cannot be a reparse point: $SetupDir"
    }
    Assert-NoReparsePointsOrTraversal -RootDir $SetupDir -HeadSha $HeadSha -ContextLabel "setup-dir"
    $setupExePath = Join-Path $SetupDir "Salryn-POS-BetaRC1-Setup.exe"
    foreach ($staleTarget in @($setupExePath, "$setupExePath.sha256.txt", "$setupExePath.provenance.json")) {
        $staleItem = Get-Item -LiteralPath $staleTarget -Force -ErrorAction SilentlyContinue
        if ($null -ne $staleItem) {
            if (($staleItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Write-ProvenanceFailure -Code "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -HeadSha $HeadSha -Detail "Setup artifact target cannot be a reparse point: $staleTarget"
            }
            Remove-Item -Force -LiteralPath $staleTarget
        }
    }
}

function Resolve-IsccPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path -LiteralPath $ExplicitPath) { return $ExplicitPath }
        throw "ISCC.exe not found at the path supplied via -InnoSetupCompilerPath: $ExplicitPath"
    }

    $fromEnv = [System.Environment]::GetEnvironmentVariable("INNO_SETUP_ISCC")
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        if (Test-Path -LiteralPath $fromEnv) { return $fromEnv }
        throw "ISCC.exe not found at path set in INNO_SETUP_ISCC environment variable: $fromEnv"
    }

    $fromPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    if ($fromPath -and (Test-Path -LiteralPath $fromPath)) { return $fromPath }

    $standardPaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $standardPaths) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6, set INNO_SETUP_ISCC, or pass -InnoSetupCompilerPath."
}

function Assert-StagedBinariesAndAssets {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion
    )

    $requiredFiles = @(
        "Salryn.Desktop.WebView.exe",
        "Salryn.Desktop.WebView.dll",
        "Salryn.Api.Local.exe",
        "Salryn.Api.Local.dll",
        "Salryn.Core.dll",
        "Salryn.Infrastructure.dll",
        "Salryn.Composition.Local.dll",
        "Salryn.Host.Local.dll",
        "WebView2Loader.dll",
        "runtimes\win-x64\native\WebView2Loader.dll",
        "dist\index.html",
        "assets\branding\salryn-icon.ico",
        "assets\branding\salryn-icon.png",
        "assets\branding\salryn-banner-clear.png",
        "assets\branding\salryn-banner-filled.png",
        "salryn-beta-rc1-installer-stage.txt"
    )

    foreach ($rel in $requiredFiles) {
        $full = Join-Path $PublishDir $rel
        if (-not (Test-Path -LiteralPath $full)) {
            Write-ProvenanceFailure -Code "MISSING_STAGED_FILE" -HeadSha $HeadSha -Detail "Required staged artifact missing: $rel"
        }
        $item = Get-Item -LiteralPath $full -Force
        if ($item.Length -le 0) {
            Write-ProvenanceFailure -Code "EMPTY_STAGED_FILE" -HeadSha $HeadSha -Detail "Required staged artifact is empty: $rel"
        }
    }

    $versionCheckedBinaries = @(
        "Salryn.Desktop.WebView.exe",
        "Salryn.Desktop.WebView.dll",
        "Salryn.Api.Local.exe",
        "Salryn.Api.Local.dll",
        "Salryn.Core.dll",
        "Salryn.Infrastructure.dll",
        "Salryn.Composition.Local.dll",
        "Salryn.Host.Local.dll"
    )

    $expectedSuffix = "+$HeadSha"
    foreach ($binRel in $versionCheckedBinaries) {
        $binPath = Join-Path $PublishDir $binRel
        $prodVer = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($binPath).ProductVersion
        if ([string]::IsNullOrWhiteSpace($prodVer) -or
            (-not $prodVer.EndsWith($expectedSuffix, [System.StringComparison]::OrdinalIgnoreCase)) -or
            (-not $prodVer.StartsWith("$ExpectedVersion+", [System.StringComparison]::OrdinalIgnoreCase))) {
            Write-ProvenanceFailure -Code "STALE_BINARY_PROVENANCE" -HeadSha $HeadSha -Detail "Staged binary '$binRel' ProductVersion '$prodVer' does not match expected '$ExpectedVersion$expectedSuffix'."
        }
    }
}

function Assert-StageManifestMatchesDisk {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$ExpectedConfiguration,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$WebRoot,
        [Parameter(Mandatory = $true)][string]$WebDistDir,
        [Parameter(Mandatory = $true)][string]$WebProvenanceStampPath,
        [string]$ExpectedManifestSha256 = "",
        [switch]$RequireSourceTreeAssetParity
    )

    Assert-NoReparsePointsOrTraversal -RootDir $PublishDir -HeadSha $HeadSha -ContextLabel "staging"
    Assert-StagedBinariesAndAssets -PublishDir $PublishDir -HeadSha $HeadSha -ExpectedVersion $ExpectedVersion

    $manifestPath = Join-Path $PublishDir "salryn-installer-provenance-manifest.json"
    $manifestShaPath = Join-Path $PublishDir "salryn-installer-provenance-manifest.json.sha256"
    if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $manifestShaPath)) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_MISSING" -HeadSha $HeadSha -Detail "Stage manifest or companion .sha256 missing in staging directory."
    }

    $actualManifestSha = Get-FileSha256Lower -FilePath $manifestPath
    $shaLine = (Get-Content -LiteralPath $manifestShaPath -Raw).Trim()
    if ($shaLine -notmatch '^([0-9a-fA-F]{64})\s+\*?salryn-installer-provenance-manifest\.json$') {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_SHA_FORMAT_INVALID" -HeadSha $HeadSha -Detail "Malformed manifest .sha256 file: '$shaLine'"
    }
    $recordedManifestSha = $Matches[1].ToLowerInvariant()
    if ($actualManifestSha -ne $recordedManifestSha) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_HASH_MISMATCH" -HeadSha $HeadSha -Detail "Manifest SHA256 mismatch: expected $recordedManifestSha, actual $actualManifestSha."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedManifestSha256)) {
        $trimmedManifestExpected = $ExpectedManifestSha256.Trim()
        if ($trimmedManifestExpected -notmatch '^[0-9a-fA-F]{64}$') {
            Write-ProvenanceFailure -Code "EXPECTED_STAGE_MANIFEST_SHA_INVALID" -HeadSha $HeadSha -Detail "-ExpectedStageManifestSha256 must be a valid 64-char hex SHA-256."
        }
        $normalizedExpectedManifestSha = $trimmedManifestExpected.ToLowerInvariant()
        if ($actualManifestSha -ne $normalizedExpectedManifestSha) {
            Write-ProvenanceFailure -Code "EXPECTED_STAGE_MANIFEST_SHA_MISMATCH" -HeadSha $HeadSha -Detail "Staged manifest SHA-256 '$actualManifestSha' does not match external pinned digest '$normalizedExpectedManifestSha'."
        }
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    } catch {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_PARSE_FAILED" -HeadSha $HeadSha -Detail "Failed to parse manifest JSON: $_"
    }

    if ($manifest.schemaVersion -ne "1.0" -or
        $manifest.source.commitSha -ne $HeadSha -or
        $manifest.source.worktreeClean -ne $true -or
        $manifest.productVersion -ne $ExpectedVersion -or
        $manifest.configuration -ne $ExpectedConfiguration) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_METADATA_MISMATCH" -HeadSha $HeadSha -Detail "Manifest metadata does not match active source HEAD='$HeadSha', version='$ExpectedVersion', config='$ExpectedConfiguration'."
    }

    $manifestFiles = @($manifest.files)
    foreach ($mf in $manifestFiles) {
        Assert-SafeManifestRelativePath -RelativePath ([string]$mf.relativePath) -RootDir $PublishDir -HeadSha $HeadSha -ContextLabel "stage-manifest"
    }

    $liveInventory = Get-DirectoryFileHashInventory -RootDir $PublishDir -ExcludeRelativePaths @(
        "salryn-installer-provenance-manifest.json",
        "salryn-installer-provenance-manifest.json.sha256"
    )
    $liveInventorySha = Get-CanonicalInventorySha256 -InventoryEntries $liveInventory

    if ([int]$manifest.payloadFileCount -ne $liveInventory.Count -or
        $manifest.payloadInventorySha256 -ne $liveInventorySha) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_INVENTORY_MISMATCH" -HeadSha $HeadSha -Detail "Staged files on disk ($($liveInventory.Count) files, $liveInventorySha) differ from manifest ($($manifest.payloadFileCount) files, $($manifest.payloadInventorySha256))."
    }

    $expectedManifestId = "salryn-pos-$ExpectedVersion-$($HeadSha.Substring(0,12))-$($liveInventorySha.Substring(0,12))"
    if ($manifest.manifestId -ne $expectedManifestId) {
        Write-ProvenanceFailure -Code "STAGE_MANIFEST_ID_MISMATCH" -HeadSha $HeadSha -Detail "Manifest ID '$($manifest.manifestId)' does not match recomputed ID '$expectedManifestId'."
    }

    for ($i = 0; $i -lt $liveInventory.Count; $i++) {
        $expectedEntry = $manifestFiles[$i]
        $actualEntry = $liveInventory[$i]
        if ($expectedEntry.relativePath -ne $actualEntry.relativePath -or
            [int64]$expectedEntry.bytes -ne [int64]$actualEntry.bytes -or
            $expectedEntry.sha256 -ne $actualEntry.sha256) {
            Write-ProvenanceFailure -Code "STAGE_MANIFEST_FILE_MISMATCH" -HeadSha $HeadSha -Detail "Staged file mismatch at '$($actualEntry.relativePath)' vs manifest '$($expectedEntry.relativePath)'."
        }
    }

    # Verify tracked branding assets in staging match clean Git worktree bytes
    foreach ($asset in @("salryn-icon.ico", "salryn-icon.png", "salryn-banner-clear.png", "salryn-banner-filled.png")) {
        $repoAssetSha = Get-FileSha256Lower -FilePath (Join-Path $RepoRoot "assets\branding\$asset")
        $stageAssetSha = Get-FileSha256Lower -FilePath (Join-Path $PublishDir "assets\branding\$asset")
        if ($repoAssetSha -ne $stageAssetSha) {
            Write-ProvenanceFailure -Code "STAGE_BRANDING_ASSET_MISMATCH" -HeadSha $HeadSha -Detail "Staged branding asset '$asset' hash ($stageAssetSha) does not match source tree ($repoAssetSha)."
        }
    }

    # Verify web source input digest against live clean source tree
    $liveWebSourceInputSha256 = Get-WebSourceInputDigest -WebRoot $WebRoot -HeadSha $HeadSha
    if ($manifest.buildOptions.webSourceInputSha256 -ne $liveWebSourceInputSha256) {
        Write-ProvenanceFailure -Code "STAGE_WEB_SOURCE_DIGEST_MISMATCH" -HeadSha $HeadSha -Detail "Manifest webSourceInputSha256 ($($manifest.buildOptions.webSourceInputSha256)) does not match clean source tree ($liveWebSourceInputSha256)."
    }

    # Verify staged dist matches manifest webDistInventorySha256
    $stagedDistPath = Join-Path $PublishDir "dist"
    $stagedDistInv = Get-DirectoryFileHashInventory -RootDir $stagedDistPath
    $stagedDistInvSha = Get-CanonicalInventorySha256 -InventoryEntries $stagedDistInv
    if ($stagedDistInvSha -ne [string]$manifest.buildOptions.webDistInventorySha256) {
        Write-ProvenanceFailure -Code "STAGE_WEB_DIST_PROVENANCE_MISMATCH" -HeadSha $HeadSha -Detail "Staged dist inventory ($stagedDistInvSha) does not match manifest webDistInventorySha256 ($($manifest.buildOptions.webDistInventorySha256))."
    }

    if ($RequireSourceTreeAssetParity) {
        $verifiedWorktreeDist = Assert-VerifiedSkipWebDist `
            -WebDistDir $WebDistDir `
            -StampPath $WebProvenanceStampPath `
            -ExpectedHeadSha $HeadSha `
            -ExpectedWebSourceInputSha256 $liveWebSourceInputSha256
        if ($stagedDistInvSha -ne $verifiedWorktreeDist.InventorySha256) {
            Write-ProvenanceFailure -Code "STAGE_WEB_DIST_PROVENANCE_MISMATCH" -HeadSha $HeadSha -Detail "Staged dist inventory ($stagedDistInvSha) does not match verified source-tree web dist ($($verifiedWorktreeDist.InventorySha256))."
        }
    }

    return [ordered]@{
        Manifest               = $manifest
        ManifestSha256         = $actualManifestSha
        LiveInventory          = $liveInventory
        PayloadInventorySha256 = $liveInventorySha
    }
}

# ---------------------------------------------------------------------------
# 1. Fail-closed source custody preflight BEFORE any mutation or downloads
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version cannot be empty."
}

$repoRoot = Find-RepoRoot
Set-Location $repoRoot

$provenance = Resolve-GitSourceProvenance -RepoRoot $repoRoot -ExpectedSha $ExpectedSourceSha
$headSha = $provenance.HeadSha
Write-Info "[PROVENANCE_PREFLIGHT] PASS head=$headSha branch=$($provenance.GitBranch) worktreeClean=true"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $rawArtifactRoot = Join-Path $repoRoot "artifacts\installer"
} else {
    if ($OutputDir.StartsWith("\\", [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-ProvenanceFailure -Code "UNSAFE_OUTPUT_DIR" -HeadSha $headSha -Detail "UNC/device OutputDir path is not allowed: $OutputDir"
    }
    $rawArtifactRoot = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $repoRoot $OutputDir }
}
$resolvedOutputInfo = Assert-OutputDirSafe -ArtifactRoot $rawArtifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots
$artifactRoot = $resolvedOutputInfo.LexicalFull

$stagingRoot          = Join-Path $artifactRoot "staging"
$publishDir           = Join-Path $stagingRoot "Salryn"
$setupDir             = Join-Path $artifactRoot "setup"
$shellTemp            = Join-Path $artifactRoot "shell-publish"
$webRoot              = Join-Path $repoRoot "src\Salryn.App.Web"
$webDistSrc           = Join-Path $webRoot "dist"
$webProvenanceStamp   = Join-Path $webDistSrc ".salryn-web-build-provenance.json"
$stagedDist           = Join-Path $publishDir "dist"
$iconSource           = Join-Path $repoRoot "assets\branding\salryn-icon.ico"
$issScript            = Join-Path $repoRoot "installer\SalrynSetup.iss"
$webview2RedistDir    = Join-Path $repoRoot "installer\redist"
$webview2Bootstrapper = Join-Path $webview2RedistDir "MicrosoftEdgeWebview2Setup.exe"
$webview2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"

# Check critical repo input paths for reparse points before creating outputs
Assert-NoReparsePointsOrTraversal -RootDir (Join-Path $repoRoot "installer") -HeadSha $headSha -ContextLabel "installer-dir"
Assert-NoReparsePointsOrTraversal -RootDir (Join-Path $repoRoot "assets\branding") -HeadSha $headSha -ContextLabel "branding-dir"

if (-not (Test-Path -LiteralPath $iconSource)) {
    Write-ProvenanceFailure -Code "MISSING_BRANDING_ASSET" -HeadSha $headSha -Detail "Missing installer icon at $iconSource."
}
if (-not (Test-Path -LiteralPath $issScript)) {
    Write-ProvenanceFailure -Code "MISSING_ISS_SCRIPT" -HeadSha $headSha -Detail "Missing Inno Setup script at $issScript."
}

function Assert-VerifiedSkipWebDist {
    param(
        [Parameter(Mandatory = $true)][string]$WebDistDir,
        [Parameter(Mandatory = $true)][string]$StampPath,
        [Parameter(Mandatory = $true)][string]$ExpectedHeadSha,
        [Parameter(Mandatory = $true)][string]$ExpectedWebSourceInputSha256
    )
    if (-not (Test-Path -LiteralPath (Join-Path $WebDistDir "index.html")) -or
        -not (Test-Path -LiteralPath $StampPath)) {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "-SkipWebBuild requires both dist\index.html and .salryn-web-build-provenance.json."
    }

    Assert-NoReparsePointsOrTraversal -RootDir $WebDistDir -HeadSha $ExpectedHeadSha -ContextLabel "web-dist"
    try {
        $stamp = Get-Content -LiteralPath $StampPath -Raw | ConvertFrom-Json
    } catch {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Failed to parse .salryn-web-build-provenance.json: $_"
    }

    if ($stamp.schemaVersion -ne "1.0" -or $stamp.sourceCommitSha -ne $ExpectedHeadSha) {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Web dist stamp commit SHA '$($stamp.sourceCommitSha)' does not match HEAD '$ExpectedHeadSha'."
    }
    if ($stamp.webSourceInputSha256 -ne $ExpectedWebSourceInputSha256) {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Web source input digest changed since web dist stamp was generated."
    }
    $stampFiles = @($stamp.files)
    foreach ($wf in $stampFiles) {
        Assert-SafeManifestRelativePath -RelativePath ([string]$wf.relativePath) -RootDir $WebDistDir -HeadSha $ExpectedHeadSha -ContextLabel "web-dist-stamp"
    }

    $currentInventory = Get-DirectoryFileHashInventory -RootDir $WebDistDir -ExcludeRelativePaths @(".salryn-web-build-provenance.json")
    $currentInventorySha = Get-CanonicalInventorySha256 -InventoryEntries $currentInventory
    if ($currentInventory.Count -eq 0 -or
        [int]$stamp.fileCount -ne $currentInventory.Count -or
        $stamp.inventorySha256 -ne $currentInventorySha) {
        Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Web dist files on disk do not match .salryn-web-build-provenance.json."
    }
    for ($i = 0; $i -lt $currentInventory.Count; $i++) {
        $se = $stampFiles[$i]
        $ae = $currentInventory[$i]
        if ($se.relativePath -ne $ae.relativePath -or [int64]$se.bytes -ne [int64]$ae.bytes -or $se.sha256 -ne $ae.sha256) {
            Write-ProvenanceFailure -Code "STALE_OR_UNVERIFIED_WEB_DIST" -HeadSha $ExpectedHeadSha -Detail "Web dist file '$($ae.relativePath)' differs from provenance stamp."
        }
    }

    return [ordered]@{
        Inventory       = $currentInventory
        InventorySha256 = $currentInventorySha
    }
}

# Purge any prior setup artifact inside the verified-safe output dir BEFORE any fallible
# verification or build path (F3), so failed web/stage/publish/ISCC runs never leave a stale EXE.
Remove-StaleSetupArtifacts -SetupDir $setupDir -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots

try {
    if ($CompileExistingStageOnly) {
        if ([string]::IsNullOrWhiteSpace($ExpectedSourceSha) -or
            [string]::IsNullOrWhiteSpace($ExpectedStageManifestSha256) -or
            ($ExpectedStageManifestSha256.Trim() -notmatch '^[0-9a-fA-F]{64}$')) {
            Write-ProvenanceFailure -Code "UNATTESTED_EXISTING_STAGE_FORBIDDEN" -HeadSha $headSha -Detail "-CompileExistingStageOnly requires explicit -ExpectedSourceSha (<40-hex>) and external -ExpectedStageManifestSha256 (<64-hex>); stage self-attestation is forbidden."
        }
        Write-Info "CompileExistingStageOnly set; validating staged output against external digest & clean source tree before mutating setup dir..."
        if (-not (Test-Path -LiteralPath $publishDir)) {
            Write-ProvenanceFailure -Code "MISSING_STAGE_DIRECTORY" -HeadSha $headSha -Detail "Existing stage directory not found: $publishDir"
        }
        $preVerifiedStage = Assert-StageManifestMatchesDisk `
            -PublishDir $publishDir `
            -HeadSha $headSha `
            -ExpectedVersion $Version `
            -ExpectedConfiguration $Configuration `
            -RepoRoot $repoRoot `
            -WebRoot $webRoot `
            -WebDistDir $webDistSrc `
            -WebProvenanceStampPath $webProvenanceStamp `
            -ExpectedManifestSha256 $ExpectedStageManifestSha256 `
            -RequireSourceTreeAssetParity
    } elseif ($SkipWebBuild) {
        Write-Info "SkipWebBuild set; verifying existing dist provenance stamp before mutating output..."
        $preWebSourceInputSha256 = Get-WebSourceInputDigest -WebRoot $webRoot -HeadSha $headSha
        $verifiedSkipWeb = Assert-VerifiedSkipWebDist `
            -WebDistDir $webDistSrc `
            -StampPath $webProvenanceStamp `
            -ExpectedHeadSha $headSha `
            -ExpectedWebSourceInputSha256 $preWebSourceInputSha256
    }

    # Re-validate OutputDir before creating directories (TOCTOU protection)
    [void](Assert-OutputDirSafe -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots)
    New-Item -ItemType Directory -Force -Path $stagingRoot, $setupDir, $webview2RedistDir | Out-Null

function Ensure-WebView2Bootstrapper {
    if (Test-Path -LiteralPath $webview2Bootstrapper) {
        Assert-NotReparsePoint -Path $webview2Bootstrapper -HeadSha $headSha -ContextLabel "webview2-bootstrapper"
        $hash = Get-FileSha256Lower -FilePath $webview2Bootstrapper
        Write-Info "WebView2 bootstrapper present: $webview2Bootstrapper"
        Write-Info "WebView2 bootstrapper SHA-256: $hash"
        return
    }

    Write-Info "WebView2 bootstrapper missing; downloading from Microsoft..."
    Write-Info "URL: $webview2BootstrapperUrl"
    try {
        Invoke-WebRequest -Uri $webview2BootstrapperUrl -OutFile $webview2Bootstrapper -UseBasicParsing
    } catch {
        throw "Failed to download Microsoft Edge WebView2 Evergreen Bootstrapper from $webview2BootstrapperUrl. $_"
    }

    if (-not (Test-Path -LiteralPath $webview2Bootstrapper)) {
        throw "WebView2 bootstrapper download completed but file is missing: $webview2Bootstrapper"
    }

    $hash = Get-FileSha256Lower -FilePath $webview2Bootstrapper
    $size = (Get-Item -LiteralPath $webview2Bootstrapper).Length
    Write-Info "Downloaded WebView2 bootstrapper ($size bytes)"
    Write-Info "WebView2 bootstrapper SHA-256: $hash"
}

Ensure-WebView2Bootstrapper
$redistBytes = [int64](Get-Item -LiteralPath $webview2Bootstrapper).Length
$redistSha256 = Get-FileSha256Lower -FilePath $webview2Bootstrapper

$dotnetVersion = (& dotnet --version 2>$null | Out-String).Trim()
$nodeVersion = (& node --version 2>$null | Out-String).Trim()
$npmVersion = (& npm --version 2>$null | Out-String).Trim()

# ---------------------------------------------------------------------------
# 2. Build / Stage Pipeline (skipped ONLY if -CompileExistingStageOnly)
# ---------------------------------------------------------------------------
if (-not $CompileExistingStageOnly) {
    $webSourceInputSha256 = Get-WebSourceInputDigest -WebRoot $webRoot -HeadSha $headSha

    if (-not $SkipWebBuild) {
        Write-Info "Cleaning previous web dist and running npm run build..."
        if (Test-Path -LiteralPath $webDistSrc) {
            Assert-NotReparsePoint -Path $webDistSrc -HeadSha $headSha -ContextLabel "web-dist"
            Remove-Item -LiteralPath $webDistSrc -Recurse -Force
        }
        & npm --prefix $webRoot run build
        if ($LASTEXITCODE -ne 0) {
            Write-ProvenanceFailure -Code "WEB_BUILD_FAILED" -HeadSha $headSha -Detail "npm run build failed (exit $LASTEXITCODE)."
        }
        if (-not (Test-Path -LiteralPath (Join-Path $webDistSrc "index.html"))) {
            Write-ProvenanceFailure -Code "MISSING_WEB_DIST" -HeadSha $headSha -Detail "Web dist missing index.html after npm run build."
        }
        Assert-NoReparsePointsOrTraversal -RootDir $webDistSrc -HeadSha $headSha -ContextLabel "web-dist"

        $webDistInventory = Get-DirectoryFileHashInventory -RootDir $webDistSrc -ExcludeRelativePaths @(".salryn-web-build-provenance.json")
        if ($webDistInventory.Count -eq 0) {
            Write-ProvenanceFailure -Code "EMPTY_WEB_DIST" -HeadSha $headSha -Detail "Web dist inventory is empty after npm run build."
        }
        $webDistInventorySha256 = Get-CanonicalInventorySha256 -InventoryEntries $webDistInventory

        $stampObject = [ordered]@{
            schemaVersion        = "1.0"
            sourceCommitSha      = $headSha
            webSourceInputSha256 = $webSourceInputSha256
            builtUtc             = (Get-Date).ToUniversalTime().ToString("o")
            fileCount            = $webDistInventory.Count
            inventorySha256      = $webDistInventorySha256
            files                = $webDistInventory
        }
        Write-Utf8NoBom -Path $webProvenanceStamp -Content ($stampObject | ConvertTo-Json -Depth 8)
    } else {
        $webDistInventory = $verifiedSkipWeb.Inventory
        $webDistInventorySha256 = $verifiedSkipWeb.InventorySha256
        Write-Info "Verified cached web dist against commit $headSha ($($webDistInventory.Count) files)."
    }

    # TOCTOU re-check output root immediately before wiping/writing staging directories
    [void](Assert-OutputDirSafe -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots)

    # Wipe staging directories cleanly
    if (Test-Path -LiteralPath $publishDir) {
        Assert-NotReparsePoint -Path $publishDir -HeadSha $headSha -ContextLabel "publish-dir"
        Remove-Item -Recurse -Force -LiteralPath $publishDir
    }
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    if (Test-Path -LiteralPath $shellTemp) {
        Assert-NotReparsePoint -Path $shellTemp -HeadSha $headSha -ContextLabel "shell-temp"
        Remove-Item -Recurse -Force -LiteralPath $shellTemp
    }
    New-Item -ItemType Directory -Force -Path $shellTemp | Out-Null

    $apiProj = Join-Path $repoRoot "src\Salryn.Api.Local\Salryn.Api.Local.csproj"
    $shellProj = Join-Path $repoRoot "src\Salryn.Desktop.WebView\Salryn.Desktop.WebView.csproj"

    # Remove prior configuration bin/obj intermediates (leaving NuGet project.assets.json intact)
    # so stale compiled DLLs cannot leak into fresh SourceRevisionId-stamped assemblies.
    foreach ($projFile in (Get-ChildItem -LiteralPath (Join-Path $repoRoot "src") -Recurse -Filter "*.csproj" -File)) {
        $projDir = $projFile.DirectoryName
        foreach ($sub in @("bin\$Configuration", "obj\$Configuration")) {
            $cleanTarget = Join-Path $projDir $sub
            if (Test-Path -LiteralPath $cleanTarget) {
                Remove-Item -LiteralPath $cleanTarget -Recurse -Force
            }
        }
    }

    Write-Info "Publishing Salryn.Api.Local (SourceRevisionId=$headSha)..."
    & dotnet publish $apiProj `
        -c $Configuration -o $publishDir --self-contained false --disable-build-servers `
        "/p:UseAppHost=true" `
        "/p:Version=$Version" `
        "/p:SourceRevisionId=$headSha" `
        "/p:IncludeSourceRevisionInInformationalVersion=true" `
        "/p:Deterministic=true"
    if ($LASTEXITCODE -ne 0) {
        Write-ProvenanceFailure -Code "DOTNET_PUBLISH_API_FAILED" -HeadSha $headSha -Detail "dotnet publish Salryn.Api.Local failed (exit $LASTEXITCODE)."
    }

    Write-Info "Publishing Salryn.Desktop.WebView (SourceRevisionId=$headSha)..."
    & dotnet publish $shellProj `
        -c $Configuration -o $shellTemp --self-contained false --disable-build-servers `
        "/p:UseAppHost=true" `
        "/p:Version=$Version" `
        "/p:SourceRevisionId=$headSha" `
        "/p:IncludeSourceRevisionInInformationalVersion=true" `
        "/p:Deterministic=true"
    if ($LASTEXITCODE -ne 0) {
        Write-ProvenanceFailure -Code "DOTNET_PUBLISH_SHELL_FAILED" -HeadSha $headSha -Detail "dotnet publish Salryn.Desktop.WebView failed (exit $LASTEXITCODE)."
    }

    Copy-Item -Path (Join-Path $shellTemp '*') -Destination $publishDir -Recurse -Force

    $webview2LoaderSrc = Join-Path $shellTemp "runtimes\win-x64\native\WebView2Loader.dll"
    if (-not (Test-Path -LiteralPath $webview2LoaderSrc)) {
        $webview2LoaderSrc = Get-ChildItem -Path $shellTemp -Recurse -Filter "WebView2Loader.dll" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'win-x64' } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $webview2LoaderSrc -or -not (Test-Path -LiteralPath $webview2LoaderSrc)) {
        Write-ProvenanceFailure -Code "MISSING_WEBVIEW2_LOADER" -HeadSha $headSha -Detail "Shell publish is missing WebView2Loader.dll under $shellTemp."
    }

    $webview2NativeDir = Join-Path $publishDir "runtimes\win-x64\native"
    New-Item -ItemType Directory -Force -Path $webview2NativeDir | Out-Null
    Copy-Item -Force -LiteralPath $webview2LoaderSrc -Destination (Join-Path $webview2NativeDir "WebView2Loader.dll")
    Copy-Item -Force -LiteralPath $webview2LoaderSrc -Destination (Join-Path $publishDir "WebView2Loader.dll")
    Write-Info "WebView2Loader staged: $(Join-Path $publishDir 'WebView2Loader.dll')"

    foreach ($rid in @("win-arm64", "win-x86")) {
        $src = Join-Path $shellTemp "runtimes\$rid\native\WebView2Loader.dll"
        if (Test-Path -LiteralPath $src) {
            $destDir = Join-Path $publishDir "runtimes\$rid\native"
            New-Item -ItemType Directory -Force -Path $destDir | Out-Null
            Copy-Item -Force -LiteralPath $src -Destination (Join-Path $destDir "WebView2Loader.dll")
        }
    }

    # Stage React dist (excluding internal build stamp file) and verify parity
    if (Test-Path -LiteralPath $stagedDist) { Remove-Item -Recurse -Force -LiteralPath $stagedDist }
    Copy-Item -Recurse -Force -LiteralPath $webDistSrc -Destination $stagedDist
    $stagedStampCopy = Join-Path $stagedDist ".salryn-web-build-provenance.json"
    if (Test-Path -LiteralPath $stagedStampCopy) {
        Remove-Item -Force -LiteralPath $stagedStampCopy
    }
    Assert-NoReparsePointsOrTraversal -RootDir $stagedDist -HeadSha $headSha -ContextLabel "staged-dist"
    $stagedDistInventory = Get-DirectoryFileHashInventory -RootDir $stagedDist
    $stagedDistInventorySha256 = Get-CanonicalInventorySha256 -InventoryEntries $stagedDistInventory
    if ($stagedDistInventorySha256 -ne $webDistInventorySha256) {
        Write-ProvenanceFailure -Code "STAGED_DIST_PARITY_MISMATCH" -HeadSha $headSha -Detail "Staged dist inventory does not match verified web dist inventory."
    }

    # Stage branding assets
    $brandingStage = Join-Path $publishDir "assets\branding"
    New-Item -ItemType Directory -Force -Path $brandingStage | Out-Null
    foreach ($asset in @("salryn-icon.ico", "salryn-icon.png", "salryn-banner-clear.png", "salryn-banner-filled.png")) {
        Copy-Item -Force -LiteralPath (Join-Path $repoRoot "assets\branding\$asset") -Destination (Join-Path $brandingStage $asset)
    }

    # Human-readable stage metadata (relative paths only; no hostnames/usernames/secrets)
    $stagedUtc = (Get-Date).ToUniversalTime().ToString("o")
    $stageStampContent = (@(
        "Product=Salryn POS"
        "Publisher=Kirjane Information Technology Services"
        "ProductVersion=$Version"
        "Configuration=$Configuration"
        "SourceCommitSha=$headSha"
        "GitBranch=$($provenance.GitBranch)"
        "WorktreeClean=true"
        "SkipWebBuild=$([bool]$SkipWebBuild)"
        "WebDistInventorySha256=$webDistInventorySha256"
        "StagedUtc=$stagedUtc"
        "Entry=Salryn.Desktop.WebView"
        "HostProcess=Salryn.Api.Local"
        "HostUrl=http://127.0.0.1:5299/"
        "WebDist=dist\"
        "InstallerIcon=assets\branding\salryn-icon.ico"
        "ProductionDataPath=%ProgramData%\Salryn\data\salryn.db"
        "WebView2=EvergreenBootstrapper"
        "WebView2Bootstrapper=installer\redist\MicrosoftEdgeWebview2Setup.exe"
        "ManifestFile=salryn-installer-provenance-manifest.json"
    ) -join "`r`n") + "`r`n"
    Write-Utf8NoBom -Path (Join-Path $publishDir "salryn-beta-rc1-installer-stage.txt") -Content $stageStampContent

    # Verify staged binaries, assets, and reparse-point cleanliness BEFORE emitting manifest
    Assert-NoReparsePointsOrTraversal -RootDir $publishDir -HeadSha $headSha -ContextLabel "staging"
    Assert-StagedBinariesAndAssets -PublishDir $publishDir -HeadSha $headSha -ExpectedVersion $Version

    # Build deterministic SHA-bound manifest covering all staged payload files
    $stageInventory = Get-DirectoryFileHashInventory -RootDir $publishDir -ExcludeRelativePaths @(
        "salryn-installer-provenance-manifest.json",
        "salryn-installer-provenance-manifest.json.sha256"
    ) -IncludeBinaryVersions
    $payloadInventorySha256 = Get-CanonicalInventorySha256 -InventoryEntries $stageInventory
    $manifestId = "salryn-pos-$Version-$($headSha.Substring(0,12))-$($payloadInventorySha256.Substring(0,12))"

    $manifestObject = [ordered]@{
        schemaVersion          = "1.0"
        manifestId             = $manifestId
        product                = "Salryn POS"
        publisher              = "Kirjane Information Technology Services"
        productVersion         = $Version
        configuration          = $Configuration
        builtUtc               = $stagedUtc
        source                 = [ordered]@{
            commitSha     = $headSha
            branch        = $provenance.GitBranch
            detachedHead  = $provenance.DetachedHead
            worktreeClean = $true
        }
        buildOptions           = [ordered]@{
            skipWebBuild            = [bool]$SkipWebBuild
            webDistInventorySha256  = $webDistInventorySha256
            webSourceInputSha256    = $webSourceInputSha256
        }
        toolchain              = [ordered]@{
            dotnetSdk  = $dotnetVersion
            node       = $nodeVersion
            npm        = $npmVersion
            powershell = $PSVersionTable.PSVersion.ToString()
        }
        redistributables       = @(
            [ordered]@{
                relativePath = "installer/redist/MicrosoftEdgeWebview2Setup.exe"
                bytes        = $redistBytes
                sha256       = $redistSha256
            }
        )
        excludedFromSelfHash   = @(
            "salryn-installer-provenance-manifest.json",
            "salryn-installer-provenance-manifest.json.sha256"
        )
        payloadFileCount       = $stageInventory.Count
        payloadInventorySha256 = $payloadInventorySha256
        files                  = $stageInventory
    }

    $manifestPath = Join-Path $publishDir "salryn-installer-provenance-manifest.json"
    $manifestShaPath = Join-Path $publishDir "salryn-installer-provenance-manifest.json.sha256"
    Write-Utf8NoBom -Path $manifestPath -Content ($manifestObject | ConvertTo-Json -Depth 10)
    $manifestSha256 = Get-FileSha256Lower -FilePath $manifestPath
    Write-Utf8NoBom -Path $manifestShaPath -Content "$manifestSha256  salryn-installer-provenance-manifest.json`n"

    Write-Info "Generated stage manifest: $manifestId ($($stageInventory.Count) payload files, inventorySha256=$payloadInventorySha256)"
} else {
    Write-Info "CompileExistingStageOnly set; re-verifying staged output against HEAD $headSha..."
    if (-not (Test-Path -LiteralPath $publishDir)) {
        Write-ProvenanceFailure -Code "MISSING_STAGE_DIRECTORY" -HeadSha $headSha -Detail "Existing stage directory not found: $publishDir"
    }
}

$stageVerifyParams = @{
    PublishDir             = $publishDir
    HeadSha                = $headSha
    ExpectedVersion        = $Version
    ExpectedConfiguration  = $Configuration
    RepoRoot               = $repoRoot
    WebRoot                = $webRoot
    WebDistDir             = $webDistSrc
    WebProvenanceStampPath = $webProvenanceStamp
}
if ($CompileExistingStageOnly) {
    $stageVerifyParams.ExpectedManifestSha256 = $ExpectedStageManifestSha256
    $stageVerifyParams.RequireSourceTreeAssetParity = $true
}

# Always verify on-disk stage matches manifest and HEAD cleanly
$verifiedStage = Assert-StageManifestMatchesDisk @stageVerifyParams
Write-Info "Verified stage custody: manifestId=$($verifiedStage.Manifest.manifestId) manifestSha256=$($verifiedStage.ManifestSha256)"

Write-Host ""
Write-Info "Staging complete: $publishDir"

# ---------------------------------------------------------------------------
# 3. Installer EXE compile (requires Inno Setup 6)
# ---------------------------------------------------------------------------
if (-not $BuildInstaller) {
    Write-Host ""
    Write-Host "BETA RC1 INSTALLER STAGE COMPLETE (staging only)"
    Write-Host "Source SHA: $headSha"
    Write-Host "Manifest  : $(Join-Path $publishDir 'salryn-installer-provenance-manifest.json')"
    Write-Host "App stage : $publishDir"
    Write-Host "Setup dir : $setupDir"
    Write-Host ""
    Write-Host "Installer EXE not compiled because -BuildInstaller was not requested."
    exit 0
}

Write-Info "Resolving Inno Setup 6 compiler (ISCC.exe)..."
$iscc = Resolve-IsccPath -ExplicitPath $InnoSetupCompilerPath

if (-not (Test-Path -LiteralPath $webview2Bootstrapper)) {
    Write-ProvenanceFailure -Code "MISSING_WEBVIEW2_BOOTSTRAPPER" -HeadSha $headSha -Detail "WebView2 bootstrapper required for installer compile is missing: $webview2Bootstrapper"
}
Assert-NotReparsePoint -Path $webview2Bootstrapper -HeadSha $headSha -ContextLabel "webview2-bootstrapper"

$expectedRedistSha = [string]$verifiedStage.Manifest.redistributables[0].sha256
if ($redistSha256 -ne $expectedRedistSha) {
    Write-ProvenanceFailure -Code "REDIST_HASH_MISMATCH" -HeadSha $headSha -Detail "WebView2 bootstrapper SHA256 '$redistSha256' does not match manifest '$expectedRedistSha'."
}

[void](Assert-OutputDirSafe -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots)
$setupExePath = Join-Path $setupDir "Salryn-POS-BetaRC1-Setup.exe"
$shaFile = "$setupExePath.sha256.txt"
$provenanceFile = "$setupExePath.provenance.json"

# Purge pre-existing installer output and custody files BEFORE compiling so stale EXEs can never be picked or left valid
Remove-StaleSetupArtifacts -SetupDir $setupDir -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots

# Re-verify stage immediately before ISCC invocation
$verifiedBeforeCompile = Assert-StageManifestMatchesDisk @stageVerifyParams

Write-Info "Found ISCC: $iscc"
Write-Info "Compiling $issScript into isolated output $setupDir..."
$isccVersion = ""
try {
    $isccVersion = [string]([System.Diagnostics.FileVersionInfo]::GetVersionInfo($iscc).ProductVersion)
} catch {
    $isccVersion = ""
}

$compileStartUtc = (Get-Date).ToUniversalTime()
& $iscc `
    "/DMyAppVersion=$Version" `
    "/DMySourceRevision=$headSha" `
    "/DStagingDir=$publishDir" `
    "/DSetupOutputDir=$setupDir" `
    $issScript
if ($LASTEXITCODE -ne 0) {
    Remove-StaleSetupArtifacts -SetupDir $setupDir -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots
    Write-ProvenanceFailure -Code "ISCC_COMPILE_FAILED" -HeadSha $headSha -Detail "ISCC failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $setupExePath)) {
    Write-ProvenanceFailure -Code "SETUP_EXE_NOT_PRODUCED" -HeadSha $headSha -Detail "ISCC exited 0 but expected setup EXE was not created at $setupExePath."
}
[void](Assert-OutputDirSafe -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots)
Assert-NotReparsePoint -Path $setupExePath -HeadSha $headSha -ContextLabel "setup-exe"

$setupItem = Get-Item -LiteralPath $setupExePath -Force
if ($setupItem.Length -le 0) {
    Remove-Item -Force -LiteralPath $setupExePath
    Write-ProvenanceFailure -Code "SETUP_EXE_EMPTY" -HeadSha $headSha -Detail "Compiled setup EXE is zero bytes: $setupExePath"
}
if ($setupItem.LastWriteTimeUtc -lt $compileStartUtc.AddSeconds(-2)) {
    Remove-Item -Force -LiteralPath $setupExePath
    Write-ProvenanceFailure -Code "STALE_SETUP_EXE_DETECTED" -HeadSha $headSha -Detail "Setup EXE timestamp ($($setupItem.LastWriteTimeUtc.ToString('o'))) predates compile start ($($compileStartUtc.ToString('o')))."
}

# Re-verify stage after ISCC completes to confirm staging did not drift mid-compile
$verifiedAfterCompile = Assert-StageManifestMatchesDisk @stageVerifyParams
if ($verifiedAfterCompile.ManifestSha256 -ne $verifiedBeforeCompile.ManifestSha256) {
    Write-ProvenanceFailure -Code "STAGE_CHANGED_DURING_COMPILE" -HeadSha $headSha -Detail "Stage manifest changed during ISCC execution."
}

$setupSha256 = Get-FileSha256Lower -FilePath $setupExePath
$setupBytes = [int64]$setupItem.Length
$compiledUtc = (Get-Date).ToUniversalTime().ToString("o")
$packedStatus = "PACKED_PAYLOAD_NOT_VERIFIED"

$shaTxtContent = (@(
    "File=$($setupItem.Name)"
    "Bytes=$setupBytes"
    "SHA256=$setupSha256"
    "Version=$Version"
    "Configuration=$Configuration"
    "SourceCommitSha=$headSha"
    "GitBranch=$($provenance.GitBranch)"
    "WorktreeClean=true"
    "ManifestId=$($verifiedAfterCompile.Manifest.manifestId)"
    "StageManifestSHA256=$($verifiedAfterCompile.ManifestSha256)"
    "PayloadFileCount=$($verifiedAfterCompile.Manifest.payloadFileCount)"
    "PayloadInventorySHA256=$($verifiedAfterCompile.PayloadInventorySha256)"
    "WebView2BootstrapperSHA256=$redistSha256"
    "PackedPayloadVerification=$packedStatus"
    "ComputedUtc=$compiledUtc"
) -join "`r`n") + "`r`n"
Write-Utf8NoBom -Path $shaFile -Content $shaTxtContent

$provenanceObject = [ordered]@{
    schemaVersion               = "1.0"
    product                     = "Salryn POS"
    publisher                   = "Kirjane Information Technology Services"
    setupFileName               = $setupItem.Name
    setupExeBytes               = $setupBytes
    setupExeSha256              = $setupSha256
    productVersion              = $Version
    configuration               = $Configuration
    builtUtc                    = $compiledUtc
    sourceCommitSha             = $headSha
    gitBranch                   = $provenance.GitBranch
    detachedHead                = $provenance.DetachedHead
    worktreeClean               = $true
    manifestId                  = $verifiedAfterCompile.Manifest.manifestId
    stageManifestSha256         = $verifiedAfterCompile.ManifestSha256
    payloadFileCount            = $verifiedAfterCompile.Manifest.payloadFileCount
    payloadInventorySha256      = $verifiedAfterCompile.PayloadInventorySha256
    redistributableRelativePath = "installer/redist/MicrosoftEdgeWebview2Setup.exe"
    redistributableSha256       = $redistSha256
    isccVersion                 = $isccVersion
    packedPayloadVerification   = $packedStatus
    trustBoundaryNote           = "Clean Git HEAD, binary ProductVersion (+SHA), full staging inventory hashes, and WebView2 bootstrapper SHA256 were verified immediately before and after ISCC.exe execution; packed bytes inside the compressed Inno Setup EXE were not extracted or independently inspected."
}
Write-Utf8NoBom -Path $provenanceFile -Content ($provenanceObject | ConvertTo-Json -Depth 6)

Write-Host ""
Write-Host "BETA RC1 INSTALLER EXE COMPILED (SOURCE-BOUND)"
Write-Host "Source SHA  : $headSha"
Write-Host "Manifest ID : $($verifiedAfterCompile.Manifest.manifestId)"
Write-Host "Setup EXE   : $($setupItem.FullName)"
Write-Host "SHA-256     : $setupSha256"
Write-Host "SHA file    : $shaFile"
Write-Host "Provenance  : $provenanceFile"
    Write-Host "App stage   : $publishDir"
    Write-Host "Packed check: $packedStatus"

    exit 0
} catch {
    try {
        Remove-StaleSetupArtifacts -SetupDir $setupDir -ArtifactRoot $artifactRoot -RepoRoot $repoRoot -HeadSha $headSha -ExtraProtectedRoots $AdditionalProtectedRoots
    } catch {
        Write-Warning "Cleanup of stale setup artifacts on failure aborted: $_"
    }
    throw
}
