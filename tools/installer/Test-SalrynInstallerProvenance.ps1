# Automated positive and negative provenance regression suite for
# tools\installer\Build-SalrynBetaRc1Installer.ps1 and installer\SalrynSetup.iss.
#
# Creates an isolated temporary detached git worktree from HEAD and isolated
# temporary output directories under $env:TEMP. Leaves the repository worktree,
# %ProgramData%\Salryn, %LocalAppData%\Salryn, and %ProgramFiles%\...\Salryn POS untouched.

[CmdletBinding()]
param(
    [string]$Version = "0.1.0-rc3.qa.provenance",
    [string]$Configuration = "Release",
    [string]$InnoSetupCompilerPath = "",
    [switch]$KeepTempOnFailure
)

$ErrorActionPreference = "Stop"

function Write-SuiteInfo([string]$Msg) { Write-Host "[provenance-test] $Msg" }

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

function Invoke-BuildInstallerChild {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$WorkingDir,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $psExe = Join-Path $PSHOME "powershell.exe"
    if (-not (Test-Path -LiteralPath $psExe)) {
        $psExe = (Get-Command "powershell.exe" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
    }
    if (-not $psExe -or -not (Test-Path -LiteralPath $psExe)) {
        $psExe = (Get-Process -Id $PID).Path
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $psExe
    $psi.WorkingDirectory = $WorkingDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $quotedArgs = New-Object System.Collections.Generic.List[string]
    $quotedArgs.Add("-NoProfile")
    $quotedArgs.Add("-ExecutionPolicy")
    $quotedArgs.Add("Bypass")
    $quotedArgs.Add("-File")
    $quotedArgs.Add('"' + $ScriptPath + '"')
    foreach ($a in $Arguments) {
        if ($a -match '[\s"]') {
            $escaped = $a -replace '"', '\"'
            $quotedArgs.Add('"' + $escaped + '"')
        } else {
            $quotedArgs.Add($a)
        }
    }
    $psi.Arguments = ($quotedArgs -join " ")

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $proc.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()

    return [pscustomobject]@{
        ExitCode = $proc.ExitCode
        StdOut   = $stdout
        StdErr   = $stderr
        Combined = ($stdout + "`n" + $stderr).Trim()
    }
}

$sourceRepoRoot = Find-RepoRoot
$headSha = (& git -C $sourceRepoRoot rev-parse --verify HEAD 2>$null | Out-String).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $headSha -notmatch '^[0-9a-f]{40}$') {
    throw "Failed to resolve clean 40-char HEAD SHA from $sourceRepoRoot"
}

$dirtySource = @(& git -C $sourceRepoRoot status --porcelain=v1 --untracked-files=all | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($dirtySource.Count -gt 0) {
    throw "Source repo at $sourceRepoRoot has uncommitted changes ($($dirtySource.Count) items). Commit or clean before running provenance test suite so HEAD matches tested code."
}

$runId = [System.Guid]::NewGuid().ToString("N").Substring(0, 10)
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "salryn-prov-qa-$runId"
$dispWorktree = Join-Path $testRoot "worktree"
$outBase = Join-Path $testRoot "outputs"
$junctionTarget = Join-Path $testRoot "junction-target"
New-Item -ItemType Directory -Force -Path $testRoot, $outBase, $junctionTarget | Out-Null
Write-Utf8NoBom -Path (Join-Path $junctionTarget "evil.txt") -Content "outside-payload"

$results = New-Object System.Collections.Generic.List[object]
$suiteFailed = $false

function Record-TestResult {
    param(
        [string]$Id,
        [bool]$Passed,
        [string]$ObservedCode,
        [string]$Notes
    )
    $status = if ($Passed) { "PASS" } else { "FAIL" }
    $script:results.Add([pscustomobject]@{
        TestId       = $Id
        Status       = $status
        ObservedCode = $ObservedCode
        Notes        = $Notes
    })
    Write-SuiteInfo "$Id => $status ($ObservedCode) : $Notes"
    if (-not $Passed) { $script:suiteFailed = $true }
}

function Get-TestFileSha256Lower {
    param([Parameter(Mandatory = $true)][string]$FilePath)
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $FilePath).Hash.ToLowerInvariant()
}

function Get-TestCanonicalInventorySha256 {
    param([AllowEmptyCollection()][array]$Entries)
    $sb = New-Object System.Text.StringBuilder
    foreach ($entry in ($Entries | Sort-Object -Property { $_.relativePath } -Culture ([System.Globalization.CultureInfo]::InvariantCulture))) {
        [void]$sb.Append($entry.relativePath).Append("`n").Append([string]$entry.bytes).Append("`n").Append($entry.sha256).Append("`n")
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($utf8NoBom.GetBytes($sb.ToString()))
        return ([System.BitConverter]::ToString($hashBytes) -replace '-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function New-ForgedConsistentlyHashedStage {
    param(
        [Parameter(Mandatory = $true)][string]$SourceStagingDir,
        [Parameter(Mandatory = $true)][string]$DestStagingDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$AssetRelativePath = "dist\index.html",
        [byte[]]$ReplacementBytes = $null
    )
    & robocopy $SourceStagingDir $DestStagingDir /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying staging to $DestStagingDir for forgery test" }

    $publishDir = Join-Path $DestStagingDir "Salryn"
    $targetFile = Join-Path $publishDir $AssetRelativePath
    if ($null -ne $ReplacementBytes) {
        [System.IO.File]::WriteAllBytes($targetFile, $ReplacementBytes)
    } else {
        Add-Content -LiteralPath $targetFile -Value "<!-- forged-stage-asset -->"
    }

    $manifestPath = Join-Path $publishDir "salryn-installer-provenance-manifest.json"
    $manifestShaPath = "$manifestPath.sha256"
    $mObj = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    $relNorm = $AssetRelativePath.Replace('\', '/')
    $newFileItem = Get-Item -LiteralPath $targetFile -Force
    $newFileSha = Get-TestFileSha256Lower -FilePath $targetFile
    foreach ($f in @($mObj.files)) {
        if ($f.relativePath -eq $relNorm) {
            $f.bytes = [int64]$newFileItem.Length
            $f.sha256 = $newFileSha
        }
    }

    # Recompute dist inventory digest if dist was modified
    $stagedDistRoot = Join-Path $publishDir "dist"
    $distPrefix = ([System.IO.Path]::GetFullPath($stagedDistRoot)).TrimEnd('\') + '\'
    $distEntries = foreach ($df in (Get-ChildItem -LiteralPath $stagedDistRoot -Recurse -File -Force)) {
        $dfFull = [System.IO.Path]::GetFullPath($df.FullName)
        [ordered]@{
            relativePath = $dfFull.Substring($distPrefix.Length).Replace('\', '/')
            bytes        = [int64]$df.Length
            sha256       = (Get-TestFileSha256Lower -FilePath $dfFull)
        }
    }
    $mObj.buildOptions.webDistInventorySha256 = Get-TestCanonicalInventorySha256 -Entries @($distEntries)

    $newPayloadInventorySha = Get-TestCanonicalInventorySha256 -Entries @($mObj.files)
    $mObj.payloadFileCount = @($mObj.files).Count
    $mObj.payloadInventorySha256 = $newPayloadInventorySha
    $mObj.manifestId = "salryn-pos-$Version-$($HeadSha.Substring(0,12))-$($newPayloadInventorySha.Substring(0,12))"

    Write-Utf8NoBom -Path $manifestPath -Content ($mObj | ConvertTo-Json -Depth 10)
    $forgedManifestSha = Get-TestFileSha256Lower -FilePath $manifestPath
    Write-Utf8NoBom -Path $manifestShaPath -Content "$forgedManifestSha  salryn-installer-provenance-manifest.json`n"
    return $forgedManifestSha
}

function Set-ForgedWorktreeDistAndStamp {
    param(
        [Parameter(Mandatory = $true)][string]$WebDistDir,
        [Parameter(Mandatory = $true)][string]$HeadSha,
        [string]$AssetRelativePath = "index.html",
        [string]$AppendText = "<!-- forged-stage-asset -->"
    )
    $targetFile = Join-Path $WebDistDir $AssetRelativePath
    if (-not (Test-Path -LiteralPath $targetFile)) {
        throw "Set-ForgedWorktreeDistAndStamp target missing: $targetFile"
    }
    Add-Content -LiteralPath $targetFile -Value $AppendText

    $stampPath = Join-Path $WebDistDir ".salryn-web-build-provenance.json"
    if (-not (Test-Path -LiteralPath $stampPath)) {
        throw "Set-ForgedWorktreeDistAndStamp stamp missing: $stampPath"
    }
    $stampObj = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json
    $distPrefix = ([System.IO.Path]::GetFullPath($WebDistDir)).TrimEnd('\') + '\'
    $filesList = foreach ($df in (Get-ChildItem -LiteralPath $WebDistDir -Recurse -File -Force)) {
        if ($df.Name -eq ".salryn-web-build-provenance.json") { continue }
        $dfFull = [System.IO.Path]::GetFullPath($df.FullName)
        [ordered]@{
            relativePath = $dfFull.Substring($distPrefix.Length).Replace('\', '/')
            bytes        = [int64]$df.Length
            sha256       = (Get-TestFileSha256Lower -FilePath $dfFull)
        }
    }
    $sortedFiles = @($filesList | Sort-Object -Property { $_.relativePath } -Culture ([System.Globalization.CultureInfo]::InvariantCulture))
    $newInvSha = Get-TestCanonicalInventorySha256 -Entries $sortedFiles
    $forgedStamp = [ordered]@{
        schemaVersion        = "1.0"
        sourceCommitSha      = $HeadSha
        webSourceInputSha256 = [string]$stampObj.webSourceInputSha256
        builtUtc             = (Get-Date).ToUniversalTime().ToString("o")
        fileCount            = $sortedFiles.Count
        inventorySha256      = $newInvSha
        files                = $sortedFiles
    }
    Write-Utf8NoBom -Path $stampPath -Content ($forgedStamp | ConvertTo-Json -Depth 8)
    return $newInvSha
}

try {
    Write-SuiteInfo "Creating disposable detached git worktree at $dispWorktree (SHA $headSha)..."
    & git -C $sourceRepoRoot worktree add --detach $dispWorktree $headSha | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create disposable git worktree."
    }

    $srcNodeModules = Join-Path $sourceRepoRoot "src\Salryn.App.Web\node_modules"
    $dispNodeModules = Join-Path $dispWorktree "src\Salryn.App.Web\node_modules"
    $dispViteBin = Join-Path $dispNodeModules "vite\bin\vite.js"
    # Note: POS-01 now unconditionally wipes node_modules and runs clean npm ci --prefer-offline,
    # so we seed only a poisoned vite.js dummy before POS-01 rather than copying 8000 files twice.

    $dispBuildScript = Join-Path $dispWorktree "tools\installer\Build-SalrynBetaRc1Installer.ps1"
    $dispWebDist = Join-Path $dispWorktree "src\Salryn.App.Web\dist"

    # -----------------------------------------------------------------------
    # NEG-01: Dirty tracked file rejected BEFORE any staging output created
    # -----------------------------------------------------------------------
    $neg01Out = Join-Path $outBase "neg01"
    $trackedReadme = Join-Path $dispWorktree "installer\README.md"
    Add-Content -LiteralPath $trackedReadme -Value "`n<!-- dirty-probe -->"
    try {
        $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg01Out, "-SkipWebBuild")
        $stageCreated = Test-Path -LiteralPath (Join-Path $neg01Out "staging\Salryn")
        $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=DIRTY_WORKTREE') -and ($r.Combined -match "head=$headSha") -and (-not $stageCreated)
        Record-TestResult -Id "NEG-01_DIRTY_TRACKED_FILE" -Passed $ok -ObservedCode "DIRTY_WORKTREE" -Notes "exit=$($r.ExitCode), stageCreated=$stageCreated"
    } finally {
        & git -C $dispWorktree checkout -- "installer/README.md" | Out-Null
    }

    # -----------------------------------------------------------------------
    # NEG-02: Dirty untracked source file rejected BEFORE any staging output created
    # -----------------------------------------------------------------------
    $neg02Out = Join-Path $outBase "neg02"
    $untrackedFile = Join-Path $dispWorktree "src\untracked-provenance-probe.txt"
    Write-Utf8NoBom -Path $untrackedFile -Content "untracked-probe"
    try {
        $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg02Out, "-SkipWebBuild")
        $stageCreated = Test-Path -LiteralPath (Join-Path $neg02Out "staging\Salryn")
        $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=DIRTY_WORKTREE') -and ($r.Combined -match "head=$headSha") -and (-not $stageCreated)
        Record-TestResult -Id "NEG-02_DIRTY_UNTRACKED_FILE" -Passed $ok -ObservedCode "DIRTY_WORKTREE" -Notes "exit=$($r.ExitCode), stageCreated=$stageCreated"
    } finally {
        if (Test-Path -LiteralPath $untrackedFile) { Remove-Item -Force -LiteralPath $untrackedFile }
    }

    # -----------------------------------------------------------------------
    # NEG-03: Omitted or mismatched -ExpectedSourceSha rejected BEFORE builds
    # -----------------------------------------------------------------------
    $neg03OutA = Join-Path $outBase "neg03a"
    $rA = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg03OutA, "-BuildInstaller")
    $stageCreatedA = Test-Path -LiteralPath (Join-Path $neg03OutA "staging\Salryn")

    $neg03OutB = Join-Path $outBase "neg03b"
    $wrongSha = "0" * 40
    $rB = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg03OutB, "-ExpectedSourceSha", $wrongSha, "-BuildInstaller")
    $stageCreatedB = Test-Path -LiteralPath (Join-Path $neg03OutB "staging\Salryn")

    $ok = ($rA.ExitCode -ne 0) -and ($rA.Combined -match 'code=EXPECTED_SHA_REQUIRED_FOR_INSTALLER') -and (-not $stageCreatedA) -and
          ($rB.ExitCode -ne 0) -and ($rB.Combined -match 'code=EXPECTED_SHA_MISMATCH') -and ($rB.Combined -match "head=$headSha") -and (-not $stageCreatedB)
    Record-TestResult -Id "NEG-03_MISSING_OR_MISMATCHED_EXPECTED_SHA" -Passed $ok -ObservedCode "EXPECTED_SHA_REQUIRED+MISMATCH" -Notes "exitA=$($rA.ExitCode), exitB=$($rB.ExitCode), stageA=$stageCreatedA, stageB=$stageCreatedB"

    # -----------------------------------------------------------------------
    # NEG-04: -SkipWebBuild without provenance stamp fails closed
    # -----------------------------------------------------------------------
    $neg04Out = Join-Path $outBase "neg04"
    if (Test-Path -LiteralPath $dispWebDist) { Remove-Item -Recurse -Force -LiteralPath $dispWebDist }
    Write-Utf8NoBom -Path (Join-Path $dispWebDist "index.html") -Content "<html><body>unverified</body></html>"
    $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg04Out, "-SkipWebBuild")
    $stageCreated = Test-Path -LiteralPath (Join-Path $neg04Out "staging\Salryn")
    $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=STALE_OR_UNVERIFIED_WEB_DIST') -and (-not $stageCreated)
    Record-TestResult -Id "NEG-04_SKIP_WEB_BUILD_MISSING_STAMP" -Passed $ok -ObservedCode "STALE_OR_UNVERIFIED_WEB_DIST" -Notes "exit=$($r.ExitCode), stageCreated=$stageCreated"

    # -----------------------------------------------------------------------
    # NEG-05: -SkipWebBuild with wrong commit SHA stamp fails closed
    # -----------------------------------------------------------------------
    $neg05Out = Join-Path $outBase "neg05"
    $fakeStamp = [ordered]@{
        schemaVersion        = "1.0"
        sourceCommitSha      = ("1" * 40)
        webSourceInputSha256 = ("a" * 64)
        builtUtc             = (Get-Date).ToUniversalTime().ToString("o")
        fileCount            = 1
        inventorySha256      = ("b" * 64)
        files                = @(@{ relativePath = "index.html"; bytes = 35; sha256 = ("c" * 64) })
    }
    Write-Utf8NoBom -Path (Join-Path $dispWebDist ".salryn-web-build-provenance.json") -Content ($fakeStamp | ConvertTo-Json -Depth 6)
    $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg05Out, "-SkipWebBuild")
    $stageCreated = Test-Path -LiteralPath (Join-Path $neg05Out "staging\Salryn")
    $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=STALE_OR_UNVERIFIED_WEB_DIST') -and (-not $stageCreated)
    Record-TestResult -Id "NEG-05_SKIP_WEB_BUILD_STALE_SHA_STAMP" -Passed $ok -ObservedCode "STALE_OR_UNVERIFIED_WEB_DIST" -Notes "exit=$($r.ExitCode), stageCreated=$stageCreated"

    # -----------------------------------------------------------------------
    # NEG-06 (before POS-01): Protected output path rejected before build
    # -----------------------------------------------------------------------
    $protOut = Join-Path $env:ProgramData "Salryn\forbidden-qa-probe-$runId"
    $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $protOut, "-SkipWebBuild")
    $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=PROTECTED_OUTPUT_DIR_FORBIDDEN') -and (-not (Test-Path -LiteralPath $protOut))
    Record-TestResult -Id "NEG-06_PROTECTED_OUTPUT_DIR_REJECTED" -Passed $ok -ObservedCode "PROTECTED_OUTPUT_DIR_FORBIDDEN" -Notes "exit=$($r.ExitCode)"
    if (Test-Path -LiteralPath $dispWebDist) { Remove-Item -Recurse -Force -LiteralPath $dispWebDist }

    # -----------------------------------------------------------------------
    # POS-01: Full fresh clean build + QA-only ISCC compile in isolated nested
    #         non-existent OutputDir. Pre-seeds forged ignored worktree dist +
    #         poisoned vite.js canary to prove Option A / F1 wipes node_modules
    #         and runs clean lockfile-verified npm ci + fresh build from source.
    # -----------------------------------------------------------------------
    $pos01Out = Join-Path $outBase "pos01\nested\clean-run"
    $pos01SetupDir = Join-Path $pos01Out "setup"
    $pos01SetupExe = Join-Path $pos01SetupDir "Salryn-POS-BetaRC1-Setup.exe"

    $pos01ViteCanary = Join-Path $testRoot "pos01-vite-canary.txt"
    $pos01ViteCanaryJs = $pos01ViteCanary.Replace('\', '\\')
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dispViteBin) | Out-Null
    Write-Utf8NoBom -Path $dispViteBin -Content "require('fs').writeFileSync('$pos01ViteCanaryJs', 'POISONED_VITE_RAN');`nprocess.exit(99);`n"

    # Pre-seed forged ignored dist + stamp on clean HEAD to prove Option A ignores & wipes it
    New-Item -ItemType Directory -Force -Path $dispWebDist | Out-Null
    $preSeedDistIdx = Join-Path $dispWebDist "index.html"
    Write-Utf8NoBom -Path $preSeedDistIdx -Content "<!doctype html><html><body>FORGED_PRE_BUILD_DIST_MUST_BE_WIPED</body></html>"
    $preSeedHash = Get-TestFileSha256Lower -FilePath $preSeedDistIdx
    $preSeedBytes = [int64](Get-Item -LiteralPath $preSeedDistIdx).Length
    $preSeedEntry = @([ordered]@{ relativePath = "index.html"; bytes = $preSeedBytes; sha256 = $preSeedHash })
    $preSeedStamp = [ordered]@{
        schemaVersion        = "1.0"
        sourceCommitSha      = $headSha
        webSourceInputSha256 = ("d" * 64)
        builtUtc             = (Get-Date).ToUniversalTime().ToString("o")
        fileCount            = 1
        inventorySha256      = (Get-TestCanonicalInventorySha256 -Entries $preSeedEntry)
        files                = $preSeedEntry
    }
    Write-Utf8NoBom -Path (Join-Path $dispWebDist ".salryn-web-build-provenance.json") -Content ($preSeedStamp | ConvertTo-Json -Depth 6)

    $posArgs = @(
        "-Version", $Version,
        "-Configuration", $Configuration,
        "-OutputDir", $pos01Out,
        "-ExpectedSourceSha", $headSha,
        "-BuildInstaller"
    )
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) {
        $posArgs += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath)
    }

    Write-SuiteInfo "Running POS-01 full fresh build + QA-only ISCC compile in isolated nested private dir..."
    $posCompileStart = (Get-Date).ToUniversalTime()
    $rPos01 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $posArgs

    $manifestFile = Join-Path $pos01Out "staging\Salryn\salryn-installer-provenance-manifest.json"
    $manifestShaFile = Join-Path $pos01Out "staging\Salryn\salryn-installer-provenance-manifest.json.sha256"
    $provJsonFile = "$pos01SetupExe.provenance.json"
    $shaTxtFile = "$pos01SetupExe.sha256.txt"
    $desktopExe = Join-Path $pos01Out "staging\Salryn\Salryn.Desktop.WebView.exe"
    $apiExe = Join-Path $pos01Out "staging\Salryn\Salryn.Api.Local.exe"
    $stagedDistIdx = Join-Path $pos01Out "staging\Salryn\dist\index.html"

    $posOk = ($rPos01.ExitCode -eq 0) -and
             (-not (Test-Path -LiteralPath $pos01ViteCanary)) -and
             (Test-Path -LiteralPath $manifestFile) -and
             (Test-Path -LiteralPath $manifestShaFile) -and
             (Test-Path -LiteralPath $pos01SetupExe) -and
             (Test-Path -LiteralPath $provJsonFile) -and
             (Test-Path -LiteralPath $shaTxtFile) -and
             (Test-Path -LiteralPath $stagedDistIdx)

    $posDetails = ""
    $pos01ManifestSha = ""
    if ($posOk) {
        $pos01ManifestSha = Get-TestFileSha256Lower -FilePath $manifestFile
        $setupItem = Get-Item -LiteralPath $pos01SetupExe
        $setupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $pos01SetupExe).Hash.ToLowerInvariant()
        $provJson = Get-Content -LiteralPath $provJsonFile -Raw | ConvertFrom-Json
        $manifestObj = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
        $desktopVer = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($desktopExe).ProductVersion
        $apiVer = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($apiExe).ProductVersion

        $leakPattern = [regex]::Escape($env:USERNAME) + "|" + [regex]::Escape($env:COMPUTERNAME) + '|C:\\Users\\'
        $manifestRaw = Get-Content -LiteralPath $manifestFile -Raw
        $provRaw = Get-Content -LiteralPath $provJsonFile -Raw
        $shaTxtRaw = Get-Content -LiteralPath $shaTxtFile -Raw
        $stagedIdxRaw = Get-Content -LiteralPath $stagedDistIdx -Raw
        $hasLeaks = ($manifestRaw -match $leakPattern) -or ($provRaw -match $leakPattern) -or ($shaTxtRaw -match $leakPattern)

        $posOk = ($setupItem.Length -gt 100000) -and
                 ($setupItem.LastWriteTimeUtc -ge $posCompileStart.AddSeconds(-2)) -and
                 ($stagedIdxRaw -notmatch 'FORGED_PRE_BUILD_DIST_MUST_BE_WIPED') -and
                 ($provJson.setupExeSha256 -eq $setupHash) -and
                 ($provJson.sourceCommitSha -eq $headSha) -and
                 ($provJson.worktreeClean -eq $true) -and
                 ($provJson.privatePerRunOutput -eq $true) -and
                 (-not [string]::IsNullOrWhiteSpace($provJson.packageLockSha256)) -and
                 ($manifestObj.buildOptions.packageLockSha256 -eq $provJson.packageLockSha256) -and
                 ($manifestObj.buildOptions.npmInstallCommand -match '^npm ci') -and
                 ($provJson.packedPayloadVerification -eq "PACKED_PAYLOAD_NOT_VERIFIED") -and
                 ($provJson.releaseAuthority -eq "SOURCE_BOUND_FRESH_BUILD") -and
                 ($provJson.authoritativeInstallerAllowed -eq $true) -and
                 ($shaTxtRaw -match 'ReleaseAuthority=SOURCE_BOUND_FRESH_BUILD') -and
                 ($manifestObj.source.commitSha -eq $headSha) -and
                 ($manifestObj.releaseAuthority -eq "SOURCE_BOUND_FRESH_BUILD") -and
                 ($manifestObj.authoritativeInstallerAllowed -eq $true) -and
                 ($manifestObj.payloadFileCount -gt 0) -and
                 ($desktopVer -eq "$Version+$headSha") -and
                 ($apiVer -eq "$Version+$headSha") -and
                 (-not $hasLeaks)

        $posDetails = "setupBytes=$($setupItem.Length), setupSha256=$setupHash, payloadFiles=$($manifestObj.payloadFileCount), manifestId=$($manifestObj.manifestId), leaks=$hasLeaks"
    } else {
        $posDetails = "exit=$($rPos01.ExitCode) out=$($rPos01.Combined)"
    }
    Record-TestResult -Id "POS-01_CLEAN_FULL_STAGE_AND_ISCC_INSTALLER" -Passed $posOk -ObservedCode $(if ($posOk) { "COMPILED_SOURCE_BOUND" } else { "FAILED" }) -Notes $posDetails

    $pos01DistBackup = Join-Path $testRoot "pos01-dist-backup"
    & robocopy $dispWebDist $pos01DistBackup /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed backing up clean freshly built web dist" }

    # -----------------------------------------------------------------------
    # POS-02: Non-release dev staging (-SkipWebBuild without -BuildInstaller)
    #         is marked NONRELEASE_DEV_STAGE_NOT_ATTESTED & rejected by -BuildInstaller
    # -----------------------------------------------------------------------
    $pos02Out = Join-Path $outBase "pos02"
    $rPos02 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version,
        "-Configuration", $Configuration,
        "-OutputDir", $pos02Out,
        "-ExpectedSourceSha", $headSha,
        "-SkipWebBuild"
    )
    $m2Path = Join-Path $pos02Out "staging\Salryn\salryn-installer-provenance-manifest.json"
    $pos02Ok = ($rPos02.ExitCode -eq 0) -and (Test-Path -LiteralPath $m2Path)
    $pos02ManifestSha = ""
    if ($pos02Ok) {
        $m1 = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
        $m2 = Get-Content -LiteralPath $m2Path -Raw | ConvertFrom-Json
        $pos02ManifestSha = Get-TestFileSha256Lower -FilePath $m2Path
        $rPos02Misuse = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $pos02Out,
            "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos02ManifestSha,
            "-CompileExistingStageOnly", "-BuildInstaller"
        )
        $pos02Ok = ($m2.buildOptions.skipWebBuild -eq $true) -and
                   ($m2.buildOptions.webDistInventorySha256 -eq $m1.buildOptions.webDistInventorySha256) -and
                   ($m2.source.commitSha -eq $headSha) -and
                   ($m2.releaseAuthority -eq "NONRELEASE_DEV_STAGE_NOT_ATTESTED") -and
                   ($m2.authoritativeInstallerAllowed -eq $false) -and
                   ($rPos02Misuse.ExitCode -ne 0) -and
                   ($rPos02Misuse.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and
                   (-not (Test-Path -LiteralPath (Join-Path $pos02Out "setup\Salryn-POS-BetaRC1-Setup.exe")))
    }
    Record-TestResult -Id "POS-02_DEV_SKIP_WEB_STAGE_MARKED_NONRELEASE" -Passed $pos02Ok -ObservedCode $(if ($pos02Ok) { "NONRELEASE_DEV_STAGE_NOT_ATTESTED" } else { "FAILED" }) -Notes "exit=$($rPos02.ExitCode)"

    # -----------------------------------------------------------------------
    # NEG-08: Path traversal injected into valid web stamp rejected
    # -----------------------------------------------------------------------
    $neg08Out = Join-Path $outBase "neg08"
    $webStampPath = Join-Path $dispWebDist ".salryn-web-build-provenance.json"
    $validWebStampRaw = Get-Content -LiteralPath $webStampPath -Raw
    try {
        $obj = $validWebStampRaw | ConvertFrom-Json
        $obj.files[0].relativePath = "../outside-traversal.js"
        Write-Utf8NoBom -Path $webStampPath -Content ($obj | ConvertTo-Json -Depth 8)
        $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg08Out, "-SkipWebBuild")
        $stageCreated = Test-Path -LiteralPath (Join-Path $neg08Out "staging\Salryn")
        $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=PATH_TRAVERSAL_FORBIDDEN') -and (-not $stageCreated)
        Record-TestResult -Id "NEG-08_PATH_TRAVERSAL_IN_STAMP_REJECTED" -Passed $ok -ObservedCode "PATH_TRAVERSAL_FORBIDDEN" -Notes "exit=$($r.ExitCode), stageCreated=$stageCreated"
    } finally {
        Write-Utf8NoBom -Path $webStampPath -Content $validWebStampRaw
    }

    # -----------------------------------------------------------------------
    # NEG-09: Post-build tampering of web dist rejected by -SkipWebBuild
    # -----------------------------------------------------------------------
    $neg09Out = Join-Path $outBase "neg09"
    $distIndex = Join-Path $dispWebDist "index.html"
    $origDistIndex = Get-Content -LiteralPath $distIndex -Raw
    try {
        Add-Content -LiteralPath $distIndex -Value "<!-- tampered-dist -->"
        $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg09Out, "-SkipWebBuild")
        $stageCreated = Test-Path -LiteralPath (Join-Path $neg09Out "staging\Salryn")
        $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=STALE_OR_UNVERIFIED_WEB_DIST') -and (-not $stageCreated)
        Record-TestResult -Id "NEG-09_TAMPERED_DIST_WITH_SKIP_WEB_BUILD" -Passed $ok -ObservedCode "STALE_OR_UNVERIFIED_WEB_DIST" -Notes "exit=$($r.ExitCode), stageCreated=$stageCreated"
    } finally {
        Write-Utf8NoBom -Path $distIndex -Content $origDistIndex
    }

    # -----------------------------------------------------------------------
    # NEG-10: Directory junction (reparse point) inside web dist fails closed
    # -----------------------------------------------------------------------
    $neg10Out = Join-Path $outBase "neg10"
    $distJunction = Join-Path $dispWebDist "junction-probe"
    & cmd /c "mklink /J `"$distJunction`" `"$junctionTarget`"" | Out-Null
    if (-not (Test-Path -LiteralPath $distJunction)) {
        throw "Test setup failed to create directory junction at $distJunction"
    }
    try {
        $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg10Out, "-SkipWebBuild")
        $stageCreated = Test-Path -LiteralPath (Join-Path $neg10Out "staging\Salryn")
        $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=REPARSE_POINT_FORBIDDEN') -and (-not $stageCreated)
        Record-TestResult -Id "NEG-10_REPARSE_POINT_IN_DIST_REJECTED" -Passed $ok -ObservedCode "REPARSE_POINT_FORBIDDEN" -Notes "exit=$($r.ExitCode), stageCreated=$stageCreated"
    } finally {
        if (Test-Path -LiteralPath $distJunction) {
            [System.IO.Directory]::Delete($distJunction, $false)
        }
    }

    # -----------------------------------------------------------------------
    # NEG-11: Option A blocks -CompileExistingStageOnly -BuildInstaller
    #         BEFORE mutating setup (F3 preserves existing setup files), AND
    #         -VerifyStageOnly detects tampered staged payload read-only
    # -----------------------------------------------------------------------
    $neg11Out = Join-Path $outBase "neg11"
    & robocopy (Join-Path $pos01Out "staging") (Join-Path $neg11Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01 staging for NEG-11" }
    New-Item -ItemType Directory -Force -Path (Join-Path $neg11Out "setup") | Out-Null
    $neg11ExePath = Join-Path $neg11Out "setup\Salryn-POS-BetaRC1-Setup.exe"
    Write-Utf8NoBom -Path $neg11ExePath -Content "STALE_NEG11_EXE"
    Add-Content -LiteralPath (Join-Path $neg11Out "staging\Salryn\dist\index.html") -Value "<!-- tampered-stage -->"

    $stageArgs11Compile = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg11Out, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs11Compile += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r11Compile = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs11Compile

    $r11Verify = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg11Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-VerifyStageOnly"
    )
    $neg11ExeIntact = (Test-Path -LiteralPath $neg11ExePath) -and ((Get-Content -LiteralPath $neg11ExePath -Raw) -eq "STALE_NEG11_EXE")
    $ok = ($r11Compile.ExitCode -ne 0) -and ($r11Compile.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and $neg11ExeIntact -and
          ($r11Verify.ExitCode -ne 0) -and ($r11Verify.Combined -match 'code=STAGE_MANIFEST_INVENTORY_MISMATCH|STAGE_MANIFEST_FILE_MISMATCH')
    Record-TestResult -Id "NEG-11_TAMPERED_STAGE_REJECTED_BEFORE_ISCC" -Passed $ok -ObservedCode "OPTION_A+STAGE_MISMATCH_READONLY" -Notes "compileExit=$($r11Compile.ExitCode), verifyExit=$($r11Verify.ExitCode), exeIntact=$neg11ExeIntact"

    # -----------------------------------------------------------------------
    # NEG-12: Option A blocks -CompileExistingStageOnly BEFORE mutation (F3),
    #         AND -VerifyStageOnly detects missing staged binary read-only
    # -----------------------------------------------------------------------
    $neg12Out = Join-Path $outBase "neg12"
    & robocopy (Join-Path $pos01Out "staging") (Join-Path $neg12Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01 staging for NEG-12" }
    New-Item -ItemType Directory -Force -Path (Join-Path $neg12Out "setup") | Out-Null
    $neg12ExePath = Join-Path $neg12Out "setup\Salryn-POS-BetaRC1-Setup.exe"
    Write-Utf8NoBom -Path $neg12ExePath -Content "STALE_NEG12_EXE"
    Remove-Item -Force -LiteralPath (Join-Path $neg12Out "staging\Salryn\WebView2Loader.dll")

    $stageArgs12Compile = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg12Out, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs12Compile += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r12Compile = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs12Compile

    $r12Verify = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg12Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-VerifyStageOnly"
    )
    $neg12ExeIntact = (Test-Path -LiteralPath $neg12ExePath) -and ((Get-Content -LiteralPath $neg12ExePath -Raw) -eq "STALE_NEG12_EXE")
    $ok = ($r12Compile.ExitCode -ne 0) -and ($r12Compile.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and $neg12ExeIntact -and
          ($r12Verify.ExitCode -ne 0) -and ($r12Verify.Combined -match 'code=MISSING_STAGED_FILE')
    Record-TestResult -Id "NEG-12_MISSING_STAGE_BINARY_REJECTED" -Passed $ok -ObservedCode "OPTION_A+MISSING_STAGED_FILE" -Notes "compileExit=$($r12Compile.ExitCode), verifyExit=$($r12Verify.ExitCode), exeIntact=$neg12ExeIntact"

    # -----------------------------------------------------------------------
    # NEG-13: Fresh -BuildInstaller with failing ISCC, no-op ISCC, and
    #         mid-ISCC stage tampering in non-existent private OutputDirs must
    #         fail closed and purge partial/forged setup EXE + sidecars
    # -----------------------------------------------------------------------
    $neg13AOut = Join-Path $outBase "neg13a"
    $neg13ASetupDir = Join-Path $neg13AOut "setup"
    $neg13AExe = Join-Path $neg13ASetupDir "Salryn-POS-BetaRC1-Setup.exe"
    $neg13ASha = "$neg13AExe.sha256.txt"
    $neg13AProv = "$neg13AExe.provenance.json"

    $fakeFailIscc = Join-Path $testRoot "fail-iscc.cmd"
    $failCmd = "@echo off`r`n(echo PARTIAL_13A_SETUP)>`"$neg13AExe`"`r`n(echo SHA256=deadbeef)>`"$neg13ASha`"`r`n(echo {`"stale`":true})>`"$neg13AProv`"`r`necho Simulated ISCC failure`r`nexit /b 2`r`n"
    Write-Utf8NoBom -Path $fakeFailIscc -Content $failCmd
    $rFail = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg13AOut,
        "-ExpectedSourceSha", $headSha, "-BuildInstaller", "-InnoSetupCompilerPath", $fakeFailIscc
    )
    $staleSurvivedFail = (Test-Path -LiteralPath $neg13AExe) -or (Test-Path -LiteralPath $neg13ASha) -or (Test-Path -LiteralPath $neg13AProv)

    $neg13BOut = Join-Path $outBase "neg13b"
    $neg13BSetupDir = Join-Path $neg13BOut "setup"
    $neg13BExe = Join-Path $neg13BSetupDir "Salryn-POS-BetaRC1-Setup.exe"
    $neg13BSha = "$neg13BExe.sha256.txt"
    $neg13BProv = "$neg13BExe.provenance.json"

    $fakeNoopIscc = Join-Path $testRoot "noop-iscc.cmd"
    Write-Utf8NoBom -Path $fakeNoopIscc -Content "@echo off`r`necho Simulated ISCC wrote nothing`r`nexit /b 0`r`n"
    $rNoop = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg13BOut,
        "-ExpectedSourceSha", $headSha, "-BuildInstaller", "-InnoSetupCompilerPath", $fakeNoopIscc
    )
    $staleSurvivedNoop = (Test-Path -LiteralPath $neg13BExe) -or (Test-Path -LiteralPath $neg13BSha) -or (Test-Path -LiteralPath $neg13BProv)

    $neg13COut = Join-Path $outBase "neg13c"
    $neg13CSetupDir = Join-Path $neg13COut "setup"
    $neg13CExe = Join-Path $neg13CSetupDir "Salryn-POS-BetaRC1-Setup.exe"
    $neg13CSha = "$neg13CExe.sha256.txt"
    $neg13CProv = "$neg13CExe.provenance.json"

    $neg13CDistIdx = Join-Path $neg13COut "staging\Salryn\dist\index.html"
    $fakeTamperIscc = Join-Path $testRoot "tamper-iscc.cmd"
    $tamperCmd = "@echo off`r`n(echo FAKE_MID_ISCC_SETUP)>`"$neg13CExe`"`r`n(echo SHA256=deadbeef)>`"$neg13CSha`"`r`n(echo {`"stale`":true})>`"$neg13CProv`"`r`n(echo mid-iscc-tamper)>>`"$neg13CDistIdx`"`r`nexit /b 0`r`n"
    Write-Utf8NoBom -Path $fakeTamperIscc -Content $tamperCmd
    $rTamper = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg13COut,
        "-ExpectedSourceSha", $headSha, "-BuildInstaller", "-InnoSetupCompilerPath", $fakeTamperIscc
    )
    $staleSurvivedTamper = (Test-Path -LiteralPath $neg13CExe) -or (Test-Path -LiteralPath $neg13CSha) -or (Test-Path -LiteralPath $neg13CProv)

    $ok = ($rFail.ExitCode -ne 0) -and ($rFail.Combined -match 'code=ISCC_COMPILE_FAILED') -and (-not $staleSurvivedFail) -and
          ($rNoop.ExitCode -ne 0) -and ($rNoop.Combined -match 'code=SETUP_EXE_NOT_PRODUCED') -and (-not $staleSurvivedNoop) -and
          ($rTamper.ExitCode -ne 0) -and ($rTamper.Combined -match 'code=STAGE_MANIFEST_INVENTORY_MISMATCH|STAGE_MANIFEST_FILE_MISMATCH|STAGE_CHANGED_DURING_COMPILE') -and (-not $staleSurvivedTamper)
    Record-TestResult -Id "NEG-13_FAILED_NOOP_OR_TAMPER_ISCC_PURGES_EXE" -Passed $ok -ObservedCode "ISCC_FAIL+NOOP+POST_CHECK" -Notes "failStale=$staleSurvivedFail, noopStale=$staleSurvivedNoop, tamperStale=$staleSurvivedTamper"

    # -----------------------------------------------------------------------
    # NEG-15: Option A blocks -CompileExistingStageOnly BEFORE mutating setup
    #         (F3 preserves setup files), AND -VerifyStageOnly rejects path
    #         traversal in stage manifest read-only (even with updated .sha256)
    # -----------------------------------------------------------------------
    $neg15Out = Join-Path $outBase "neg15"
    & robocopy (Join-Path $pos01Out "staging") (Join-Path $neg15Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01 staging for NEG-15" }
    New-Item -ItemType Directory -Force -Path (Join-Path $neg15Out "setup") | Out-Null
    $neg15ExePath = Join-Path $neg15Out "setup\Salryn-POS-BetaRC1-Setup.exe"
    Write-Utf8NoBom -Path $neg15ExePath -Content "STALE_NEG15_EXE"
    $neg15Manifest = Join-Path $neg15Out "staging\Salryn\salryn-installer-provenance-manifest.json"
    $neg15ManifestSha = "$neg15Manifest.sha256"
    $mObj = Get-Content -LiteralPath $neg15Manifest -Raw | ConvertFrom-Json
    $mObj.files[0].relativePath = "../../escape.dll"
    Write-Utf8NoBom -Path $neg15Manifest -Content ($mObj | ConvertTo-Json -Depth 10)
    $newMSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $neg15Manifest).Hash.ToLowerInvariant()
    Write-Utf8NoBom -Path $neg15ManifestSha -Content "$newMSha  salryn-installer-provenance-manifest.json`n"

    $stageArgs15Compile = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg15Out, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $newMSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs15Compile += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r15Compile = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs15Compile

    $r15Verify = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg15Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $newMSha, "-VerifyStageOnly"
    )
    $neg15ExeIntact = (Test-Path -LiteralPath $neg15ExePath) -and ((Get-Content -LiteralPath $neg15ExePath -Raw) -eq "STALE_NEG15_EXE")
    $ok15 = ($r15Compile.ExitCode -ne 0) -and ($r15Compile.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and $neg15ExeIntact -and
            ($r15Verify.ExitCode -ne 0) -and ($r15Verify.Combined -match 'code=PATH_TRAVERSAL_FORBIDDEN')
    Record-TestResult -Id "NEG-15_PATH_TRAVERSAL_IN_STAGE_MANIFEST_REJECTED" -Passed $ok15 -ObservedCode "OPTION_A+PATH_TRAVERSAL_FORBIDDEN" -Notes "compileExit=$($r15Compile.ExitCode), verifyExit=$($r15Verify.ExitCode), exeIntact=$neg15ExeIntact"

    # -----------------------------------------------------------------------
    # NEG-16 (F1+F3): -CompileExistingStageOnly fails BEFORE any disk
    #                 mutation under Option A and preserves setup artifacts
    # -----------------------------------------------------------------------
    $neg16Out = Join-Path $outBase "neg16"
    & robocopy (Join-Path $pos01Out "staging") (Join-Path $neg16Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01 staging for NEG-16" }
    $neg16SetupDir = Join-Path $neg16Out "setup"
    New-Item -ItemType Directory -Force -Path $neg16SetupDir | Out-Null
    $neg16StaleExe = Join-Path $neg16SetupDir "Salryn-POS-BetaRC1-Setup.exe"
    $neg16StaleSha = "$neg16StaleExe.sha256.txt"
    $neg16StaleProv = "$neg16StaleExe.provenance.json"
    Write-Utf8NoBom -Path $neg16StaleExe -Content "STALE_PRE_F1_EXE"
    Write-Utf8NoBom -Path $neg16StaleSha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg16StaleProv -Content '{"stale":true}'
    $stageArgs16 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg16Out, "-ExpectedSourceSha", $headSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs16 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r16 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs16
    $setupPreserved16 = ((Get-Content -LiteralPath $neg16StaleExe -Raw) -eq "STALE_PRE_F1_EXE") -and
                        ((Get-Content -LiteralPath $neg16StaleSha -Raw) -eq "SHA256=deadbeef`r`n") -and
                        ((Get-Content -LiteralPath $neg16StaleProv -Raw) -eq '{"stale":true}')
    $ok16 = ($r16.ExitCode -ne 0) -and ($r16.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and $setupPreserved16
    Record-TestResult -Id "NEG-16_F1_COMPILE_EXISTING_STAGE_FORBIDDEN" -Passed $ok16 -ObservedCode "COMPILE_EXISTING_STAGE_FORBIDDEN" -Notes "exit=$($r16.ExitCode), preserved=$setupPreserved16"

    # -----------------------------------------------------------------------
    # NEG-17 (F1+F3): Coordinated forgery rejected BEFORE any disk mutation;
    #                 pre-seeded setup artifacts remain preserved
    # -----------------------------------------------------------------------
    $neg17Out = Join-Path $outBase "neg17"
    New-Item -ItemType Directory -Force -Path (Join-Path $neg17Out "setup") | Out-Null
    $neg17Exe = Join-Path $neg17Out "setup\Salryn-POS-BetaRC1-Setup.exe"
    $neg17Sha = "$neg17Exe.sha256.txt"
    $neg17Prov = "$neg17Exe.provenance.json"
    Write-Utf8NoBom -Path $neg17Exe -Content "STALE_PRE_17_EXE"
    Write-Utf8NoBom -Path $neg17Sha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg17Prov -Content '{"stale":true}'
    try {
        [void](Set-ForgedWorktreeDistAndStamp -WebDistDir $dispWebDist -HeadSha $headSha)
        $forgedSha17 = New-ForgedConsistentlyHashedStage -SourceStagingDir (Join-Path $pos01Out "staging") -DestStagingDir (Join-Path $neg17Out "staging") -HeadSha $headSha -Version $Version
        $stageArgs17 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg17Out, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $forgedSha17, "-CompileExistingStageOnly", "-BuildInstaller")
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs17 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
        $r17 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs17
        $setupPreserved17 = ((Get-Content -LiteralPath $neg17Exe -Raw) -eq "STALE_PRE_17_EXE") -and
                            ((Get-Content -LiteralPath $neg17Sha -Raw) -eq "SHA256=deadbeef`r`n") -and
                            ((Get-Content -LiteralPath $neg17Prov -Raw) -eq '{"stale":true}')
        $ok17 = ($r17.ExitCode -ne 0) -and ($forgedSha17 -ne $pos01ManifestSha) -and ($r17.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and $setupPreserved17
        Record-TestResult -Id "NEG-17_F1_COORDINATED_STAGE_AND_DIST_FORGERY_REJECTED" -Passed $ok17 -ObservedCode "COMPILE_EXISTING_STAGE_FORBIDDEN" -Notes "exit=$($r17.ExitCode), forgedDiffers=$($forgedSha17 -ne $pos01ManifestSha), preserved=$setupPreserved17"
    } finally {
        if (Test-Path -LiteralPath $dispWebDist) { Remove-Item -Recurse -Force -LiteralPath $dispWebDist }
        & robocopy $pos01DistBackup $dispWebDist /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Failed restoring clean web dist after NEG-17" }
    }

    # -----------------------------------------------------------------------
    # NEG-18 (F1+F3): -SkipWebBuild -BuildInstaller rejected BEFORE mutation
    #                 (preserving setup), AND -VerifyStageOnly catches parity
    #                 tampering read-only
    # -----------------------------------------------------------------------
    $neg18OutA = Join-Path $outBase "neg18a"
    New-Item -ItemType Directory -Force -Path (Join-Path $neg18OutA "setup") | Out-Null
    $neg18AExe = Join-Path $neg18OutA "setup\Salryn-POS-BetaRC1-Setup.exe"
    $neg18ASha = "$neg18AExe.sha256.txt"
    $neg18AProv = "$neg18AExe.provenance.json"
    Write-Utf8NoBom -Path $neg18AExe -Content "STALE_PRE_18A_EXE"
    Write-Utf8NoBom -Path $neg18ASha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg18AProv -Content '{"stale":true}'
    try {
        [void](Set-ForgedWorktreeDistAndStamp -WebDistDir $dispWebDist -HeadSha $headSha)
        $r18A = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg18OutA,
            "-ExpectedSourceSha", $headSha, "-SkipWebBuild", "-BuildInstaller"
        )
        $setupPreserved18A = ((Get-Content -LiteralPath $neg18AExe -Raw) -eq "STALE_PRE_18A_EXE") -and
                             ((Get-Content -LiteralPath $neg18ASha -Raw) -eq "SHA256=deadbeef`r`n") -and
                             ((Get-Content -LiteralPath $neg18AProv -Raw) -eq '{"stale":true}')
    } finally {
        if (Test-Path -LiteralPath $dispWebDist) { Remove-Item -Recurse -Force -LiteralPath $dispWebDist }
        & robocopy $pos01DistBackup $dispWebDist /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Failed restoring clean web dist after NEG-18A" }
    }

    $neg18OutB = Join-Path $outBase "neg18b"
    $forgedSha18B = New-ForgedConsistentlyHashedStage -SourceStagingDir (Join-Path $pos01Out "staging") -DestStagingDir (Join-Path $neg18OutB "staging") -HeadSha $headSha -Version $Version
    $r18B = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg18OutB,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $forgedSha18B, "-VerifyStageOnly"
    )

    $neg18OutC = Join-Path $outBase "neg18c"
    $forgedBytes = [System.Text.Encoding]::UTF8.GetBytes("FORGED_BRANDING_PNG")
    $forgedSha18C = New-ForgedConsistentlyHashedStage -SourceStagingDir (Join-Path $pos01Out "staging") -DestStagingDir (Join-Path $neg18OutC "staging") -HeadSha $headSha -Version $Version -AssetRelativePath "assets\branding\salryn-icon.png" -ReplacementBytes $forgedBytes
    $r18C = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg18OutC,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $forgedSha18C, "-VerifyStageOnly"
    )

    $ok18 = ($r18A.ExitCode -ne 0) -and ($r18A.Combined -match 'code=SKIP_WEB_BUILD_FORBIDDEN_FOR_INSTALLER') -and $setupPreserved18A -and
            ($r18B.ExitCode -ne 0) -and ($r18B.Combined -match 'code=STAGE_WEB_DIST_PROVENANCE_MISMATCH') -and
            ($r18C.ExitCode -ne 0) -and ($r18C.Combined -match 'code=STAGE_BRANDING_ASSET_MISMATCH')
    Record-TestResult -Id "NEG-18_F1_SKIPWEB_FORGERY_AND_STAGE_PARITY" -Passed $ok18 -ObservedCode "SKIPWEB_FORBIDDEN+STAGE_PARITY" -Notes "skipExit=$($r18A.ExitCode), preserved=$setupPreserved18A, distVerify=$($r18B.ExitCode), brandVerify=$($r18C.ExitCode)"

    # -----------------------------------------------------------------------
    # NEG-23 (F1 Option A + F3): Authentic existing stage & stamped dist are
    #                            blocked from reused installer packaging BEFORE
    #                            mutating existing setup artifacts
    # -----------------------------------------------------------------------
    $neg23OutA = Join-Path $outBase "neg23a"
    & robocopy (Join-Path $pos01Out "staging") (Join-Path $neg23OutA "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01 staging for NEG-23A" }
    New-Item -ItemType Directory -Force -Path (Join-Path $neg23OutA "setup") | Out-Null
    $neg23AExe = Join-Path $neg23OutA "setup\Salryn-POS-BetaRC1-Setup.exe"
    $neg23ASha = "$neg23AExe.sha256.txt"
    $neg23AProv = "$neg23AExe.provenance.json"
    Write-Utf8NoBom -Path $neg23AExe -Content "STALE_23A"
    Write-Utf8NoBom -Path $neg23ASha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg23AProv -Content '{"stale":true}'
    $args23A = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg23OutA, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $args23A += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r23A = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $args23A
    $preserved23A = ((Get-Content -LiteralPath $neg23AExe -Raw) -eq "STALE_23A") -and
                    ((Get-Content -LiteralPath $neg23ASha -Raw) -eq "SHA256=deadbeef`r`n") -and
                    ((Get-Content -LiteralPath $neg23AProv -Raw) -eq '{"stale":true}')

    $neg23OutB = Join-Path $outBase "neg23b"
    & robocopy (Join-Path $pos01Out "staging") (Join-Path $neg23OutB "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01 staging for NEG-23B" }
    New-Item -ItemType Directory -Force -Path (Join-Path $neg23OutB "setup") | Out-Null
    $neg23BExe = Join-Path $neg23OutB "setup\Salryn-POS-BetaRC1-Setup.exe"
    $neg23BSha = "$neg23BExe.sha256.txt"
    $neg23BProv = "$neg23BExe.provenance.json"
    Write-Utf8NoBom -Path $neg23BExe -Content "STALE_23B"
    Write-Utf8NoBom -Path $neg23BSha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg23BProv -Content '{"stale":true}'
    $r23B = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg23OutB,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-CompileExistingStageOnly"
    )
    $preserved23B = ((Get-Content -LiteralPath $neg23BExe -Raw) -eq "STALE_23B") -and
                    ((Get-Content -LiteralPath $neg23BSha -Raw) -eq "SHA256=deadbeef`r`n") -and
                    ((Get-Content -LiteralPath $neg23BProv -Raw) -eq '{"stale":true}')

    $neg23OutC = Join-Path $outBase "neg23c"
    New-Item -ItemType Directory -Force -Path (Join-Path $neg23OutC "setup") | Out-Null
    $neg23CExe = Join-Path $neg23OutC "setup\Salryn-POS-BetaRC1-Setup.exe"
    $neg23CSha = "$neg23CExe.sha256.txt"
    $neg23CProv = "$neg23CExe.provenance.json"
    Write-Utf8NoBom -Path $neg23CExe -Content "STALE_23C"
    Write-Utf8NoBom -Path $neg23CSha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg23CProv -Content '{"stale":true}'
    $r23C = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg23OutC,
        "-ExpectedSourceSha", $headSha, "-SkipWebBuild", "-BuildInstaller"
    )
    $preserved23C = ((Get-Content -LiteralPath $neg23CExe -Raw) -eq "STALE_23C") -and
                    ((Get-Content -LiteralPath $neg23CSha -Raw) -eq "SHA256=deadbeef`r`n") -and
                    ((Get-Content -LiteralPath $neg23CProv -Raw) -eq '{"stale":true}')

    $ok23 = ($r23A.ExitCode -ne 0) -and ($r23A.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and $preserved23A -and
            ($r23B.ExitCode -ne 0) -and ($r23B.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and $preserved23B -and
            ($r23C.ExitCode -ne 0) -and ($r23C.Combined -match 'code=SKIP_WEB_BUILD_FORBIDDEN_FOR_INSTALLER') -and $preserved23C
    Record-TestResult -Id "NEG-23_F1_OPTION_A_BLOCKS_AUTHENTIC_REUSE" -Passed $ok23 -ObservedCode "OPTION_A_BLOCKS_STAGE_AND_SKIPWEB" -Notes "A=$($r23A.ExitCode)/$preserved23A,B=$($r23B.ExitCode)/$preserved23B,C=$($r23C.ExitCode)/$preserved23C"

    # -----------------------------------------------------------------------
    # POS-03 (F3): -VerifyStageOnly (valid & failing) and rejected mode
    #              combinations leave existing output & setup artifacts 100%
    #              byte-identical (read-only / zero writes or deletes)
    # -----------------------------------------------------------------------
    $pos03Out = Join-Path $outBase "pos03-readonly"
    & robocopy $pos01Out $pos03Out /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01Out for POS-03" }
    $pos03PreFiles = @{}
    foreach ($f in @(Get-ChildItem -LiteralPath $pos03Out -Recurse -File -Force)) {
        $pos03PreFiles[$f.FullName] = [ordered]@{
            Sha256    = (Get-TestFileSha256Lower -FilePath $f.FullName)
            WriteUtc  = $f.LastWriteTimeUtc.ToString("o")
        }
    }
    $r03Valid = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $pos03Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-VerifyStageOnly"
    )
    $wrongStageSha03 = "f" * 64
    $r03BadPin = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $pos03Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $wrongStageSha03, "-VerifyStageOnly"
    )
    $r03VerifyBuild = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $pos03Out,
        "-ExpectedSourceSha", $headSha, "-VerifyStageOnly", "-BuildInstaller"
    )
    $r03CompileStage = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $pos03Out,
        "-ExpectedSourceSha", $headSha, "-CompileExistingStageOnly", "-BuildInstaller"
    )
    $r03SkipWebBuild = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $pos03Out,
        "-ExpectedSourceSha", $headSha, "-SkipWebBuild", "-BuildInstaller"
    )
    $pos03PostFiles = @(Get-ChildItem -LiteralPath $pos03Out -Recurse -File -Force)
    $pos03Unchanged = ($pos03PostFiles.Count -eq $pos03PreFiles.Count)
    if ($pos03Unchanged) {
        foreach ($pf in $pos03PostFiles) {
            $preMeta = $pos03PreFiles[$pf.FullName]
            if ($null -eq $preMeta -or
                (Get-TestFileSha256Lower -FilePath $pf.FullName) -ne $preMeta.Sha256 -or
                $pf.LastWriteTimeUtc.ToString("o") -ne $preMeta.WriteUtc) {
                $pos03Unchanged = $false
                break
            }
        }
    }
    $ok03 = ($r03Valid.ExitCode -eq 0) -and ($r03Valid.Combined -match '\[STAGE_VERIFIED_READONLY\]') -and
            ($r03BadPin.ExitCode -ne 0) -and ($r03BadPin.Combined -match 'code=EXPECTED_STAGE_MANIFEST_SHA_MISMATCH') -and
            ($r03VerifyBuild.ExitCode -ne 0) -and ($r03VerifyBuild.Combined -match 'code=VERIFY_ONLY_CANNOT_BUILD_INSTALLER') -and
            ($r03CompileStage.ExitCode -ne 0) -and ($r03CompileStage.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and
            ($r03SkipWebBuild.ExitCode -ne 0) -and ($r03SkipWebBuild.Combined -match 'code=SKIP_WEB_BUILD_FORBIDDEN_FOR_INSTALLER') -and
            $pos03Unchanged
    Record-TestResult -Id "POS-03_F3_VERIFY_STAGE_ONLY_AND_REJECTED_MODES_BYTE_PRESERVING" -Passed $ok03 -ObservedCode "READONLY_BYTE_IDENTICAL" -Notes "files=$($pos03PostFiles.Count), unchanged=$pos03Unchanged"

    # -----------------------------------------------------------------------
    # NEG-26 (F1): Modifying node_modules\vite\bin\vite.js while leaving
    #              package-lock.json & node_modules metadata unchanged is
    #              overwritten by mandatory npm ci before vite runs
    # -----------------------------------------------------------------------
    $neg26Out = Join-Path $outBase "neg26"
    $neg26Canary = Join-Path $testRoot "neg26-vite-canary.txt"
    $neg26CanaryJs = $neg26Canary.Replace('\', '\\')
    $dispPkgLock = Join-Path $dispWorktree "src\Salryn.App.Web\package-lock.json"
    $dispNmPkgLock = Join-Path $dispNodeModules ".package-lock.json"
    $dispVitePkg = Join-Path $dispNodeModules "vite\package.json"
    $preViteSha26 = Get-TestFileSha256Lower -FilePath $dispViteBin
    $preLockSha26 = Get-TestFileSha256Lower -FilePath $dispPkgLock
    $preNmLockSha26 = Get-TestFileSha256Lower -FilePath $dispNmPkgLock
    $preVitePkgSha26 = Get-TestFileSha256Lower -FilePath $dispVitePkg
    $origVite26 = [System.IO.File]::ReadAllText($dispViteBin)
    try {
        [System.IO.File]::WriteAllText($dispViteBin, "require('fs').writeFileSync('$neg26CanaryJs', 'TAMPERED_VITE_RAN');`n" + $origVite26, (New-Object System.Text.UTF8Encoding($false)))
        $args26 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg26Out, "-ExpectedSourceSha", $headSha, "-BuildInstaller")
        if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $args26 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
        $r26 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $args26
        $postViteSha26 = Get-TestFileSha256Lower -FilePath $dispViteBin
        $canaryRan26 = Test-Path -LiteralPath $neg26Canary
        $metaUnchanged26 = ($preLockSha26 -eq (Get-TestFileSha256Lower -FilePath $dispPkgLock)) -and
                           ($preNmLockSha26 -eq (Get-TestFileSha256Lower -FilePath $dispNmPkgLock)) -and
                           ($preVitePkgSha26 -eq (Get-TestFileSha256Lower -FilePath $dispVitePkg))
        $ok26 = ($r26.ExitCode -eq 0) -and (-not $canaryRan26) -and ($postViteSha26 -eq $preViteSha26) -and $metaUnchanged26
        Record-TestResult -Id "NEG-26_F1_TAMPERED_VITE_EXECUTABLE_REPLACED_BY_MANDATORY_NPM_CI" -Passed $ok26 -ObservedCode "NPM_CI_REPLACED_TAMPERED_BIN" -Notes "exit=$($r26.ExitCode), canaryRan=$canaryRan26, viteRestored=$($postViteSha26 -eq $preViteSha26)"
    } finally {
        if ($null -ne $origVite26 -and (Test-Path -LiteralPath $dispViteBin)) {
            $curSha26 = Get-TestFileSha256Lower -FilePath $dispViteBin
            if ($curSha26 -ne $preViteSha26) {
                [System.IO.File]::WriteAllText($dispViteBin, $origVite26, (New-Object System.Text.UTF8Encoding($false)))
            }
        }
    }

    # -----------------------------------------------------------------------
    # NEG-19 (F2): Direct OutputDir junction redirecting to disposable stand-in
    #              protected directory is rejected BEFORE any mutation/delete
    # -----------------------------------------------------------------------
    $standin19 = Join-Path $testRoot "standin-protected-19"
    $standin19Setup = Join-Path $standin19 "setup"
    $standin19Staging = Join-Path $standin19 "staging\Salryn"
    New-Item -ItemType Directory -Force -Path $standin19Setup, $standin19Staging | Out-Null
    $canary19Root = Join-Path $standin19 "canary.txt"
    $canary19Exe = Join-Path $standin19Setup "Salryn-POS-BetaRC1-Setup.exe"
    $canary19Payload = Join-Path $standin19Staging "canary.dll"
    Write-Utf8NoBom -Path $canary19Root -Content "CANARY_ROOT_19"
    Write-Utf8NoBom -Path $canary19Exe -Content "CANARY_EXE_19"
    Write-Utf8NoBom -Path $canary19Payload -Content "CANARY_DLL_19"

    $junction19 = Join-Path $outBase "neg19-direct-junction"
    & cmd /c "mklink /J `"$junction19`" `"$standin19`"" | Out-Null
    if (-not (Test-Path -LiteralPath $junction19)) { throw "Failed creating junction at $junction19" }
    try {
        $r19 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-OutputDir", $junction19, "-AdditionalProtectedRoots", $standin19, "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $canariesIntact19 = ((Get-Content -LiteralPath $canary19Root -Raw) -eq "CANARY_ROOT_19") -and
                            ((Get-Content -LiteralPath $canary19Exe -Raw) -eq "CANARY_EXE_19") -and
                            ((Get-Content -LiteralPath $canary19Payload -Raw) -eq "CANARY_DLL_19")
        $ok19 = ($r19.ExitCode -ne 0) -and ($r19.Combined -match 'code=OUTPUT_DIR_REPARSE_POINT_FORBIDDEN') -and $canariesIntact19
        Record-TestResult -Id "NEG-19_F2_DIRECT_OUTPUTDIR_JUNCTION_REJECTED" -Passed $ok19 -ObservedCode "OUTPUT_DIR_REPARSE_POINT_FORBIDDEN" -Notes "exit=$($r19.ExitCode), canariesIntact=$canariesIntact19"
    } finally {
        if (Test-Path -LiteralPath $junction19) { [System.IO.Directory]::Delete($junction19, $false) }
    }

    # -----------------------------------------------------------------------
    # NEG-20 (F2): Ancestor junction with non-existent child leaf, repo src alias
    #              junction, direct non-artifacts repo path, and child setup junction
    # -----------------------------------------------------------------------
    $standin20 = Join-Path $testRoot "standin-protected-20"
    New-Item -ItemType Directory -Force -Path $standin20 | Out-Null
    $canary20 = Join-Path $standin20 "canary-anc.txt"
    Write-Utf8NoBom -Path $canary20 -Content "CANARY_ANCESTOR_20"

    $ancJunction20 = Join-Path $outBase "neg20-anc-junction"
    $repoSrcTarget = Join-Path $dispWorktree "src\Salryn.App.Web"
    $repoJunction20 = Join-Path $outBase "neg20-repo-junction"
    $childSubRoot20 = Join-Path $outBase "neg20-child-subroot"
    New-Item -ItemType Directory -Force -Path $childSubRoot20 | Out-Null
    $childSetupJunction20 = Join-Path $childSubRoot20 "setup"

    & cmd /c "mklink /J `"$ancJunction20`" `"$standin20`"" | Out-Null
    & cmd /c "mklink /J `"$repoJunction20`" `"$repoSrcTarget`"" | Out-Null
    & cmd /c "mklink /J `"$childSetupJunction20`" `"$standin20`"" | Out-Null
    try {
        $missingLeafInAnc = Join-Path $ancJunction20 "nested\nonexistent-leaf"
        $r20A = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-OutputDir", $missingLeafInAnc, "-AdditionalProtectedRoots", $standin20, "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $r20B = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-OutputDir", (Join-Path $repoJunction20 "nested-out"), "-SkipWebBuild"
        )
        $directRepoUnsafe = Join-Path $dispWorktree "src\forbidden-out"
        $r20C = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-OutputDir", $directRepoUnsafe, "-SkipWebBuild"
        )
        $r20D = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-OutputDir", $childSubRoot20, "-AdditionalProtectedRoots", $standin20, "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $leafCreated = Test-Path -LiteralPath (Join-Path $standin20 "nested")
        $repoUnsafeCreated = (Test-Path -LiteralPath $directRepoUnsafe) -or (Test-Path -LiteralPath (Join-Path $repoSrcTarget "nested-out"))
        $canaryIntact20 = ((Get-Content -LiteralPath $canary20 -Raw) -eq "CANARY_ANCESTOR_20")
        $ok20 = ($r20A.ExitCode -ne 0) -and ($r20A.Combined -match 'code=OUTPUT_DIR_REPARSE_POINT_FORBIDDEN') -and
                ($r20B.ExitCode -ne 0) -and ($r20B.Combined -match 'code=OUTPUT_DIR_REPARSE_POINT_FORBIDDEN') -and
                ($r20C.ExitCode -ne 0) -and ($r20C.Combined -match 'code=UNSAFE_OUTPUT_DIR') -and
                ($r20D.ExitCode -ne 0) -and ($r20D.Combined -match 'code=OUTPUT_DIR_REPARSE_POINT_FORBIDDEN') -and
                (-not $leafCreated) -and (-not $repoUnsafeCreated) -and $canaryIntact20
        Record-TestResult -Id "NEG-20_F2_ANCESTOR_AND_REPO_ALIAS_JUNCTIONS_REJECTED" -Passed $ok20 -ObservedCode "OUTPUT_DIR_REPARSE+UNSAFE_OUTPUT_DIR" -Notes "A=$($r20A.ExitCode),B=$($r20B.ExitCode),C=$($r20C.ExitCode),D=$($r20D.ExitCode),canary=$canaryIntact20"
    } finally {
        foreach ($j in @($ancJunction20, $repoJunction20, $childSetupJunction20)) {
            if (Test-Path -LiteralPath $j) { [System.IO.Directory]::Delete($j, $false) }
        }
    }

    # -----------------------------------------------------------------------
    # NEG-24 (F2): Nested directory junctions inside staging\Salryn\dist,
    #              staging\Salryn\assets\branding, shell-publish, and
    #              src\*\obj are detected & rejected BEFORE any deletion
    # -----------------------------------------------------------------------
    $canaryDir24 = Join-Path $testRoot "canary-target-24"
    New-Item -ItemType Directory -Force -Path $canaryDir24 | Out-Null
    $canary24Txt = Join-Path $canaryDir24 "canary.txt"
    $canary24Dll = Join-Path $canaryDir24 "canary.dll"
    Write-Utf8NoBom -Path $canary24Txt -Content "CANARY_24_TXT"
    Write-Utf8NoBom -Path $canary24Dll -Content "CANARY_24_DLL"

    $neg24AOut = Join-Path $outBase "neg24a"
    & robocopy (Join-Path $pos01Out "staging") (Join-Path $neg24AOut "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01 staging for NEG-24A" }
    $j24Dist = Join-Path $neg24AOut "staging\Salryn\dist\nested-j"

    $neg24BOut = Join-Path $outBase "neg24b"
    & robocopy (Join-Path $pos01Out "staging") (Join-Path $neg24BOut "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01 staging for NEG-24B" }
    $j24Brand = Join-Path $neg24BOut "staging\Salryn\assets\branding\nested-j"

    $neg24COut = Join-Path $outBase "neg24c"
    New-Item -ItemType Directory -Force -Path (Join-Path $neg24COut "shell-publish") | Out-Null
    $j24Shell = Join-Path $neg24COut "shell-publish\nested-j"

    $neg24DOut = Join-Path $outBase "neg24d"
    $objConfigDir24 = Join-Path $dispWorktree "src\Salryn.Api.Local\obj\$Configuration"
    if (-not (Test-Path -LiteralPath $objConfigDir24)) {
        New-Item -ItemType Directory -Force -Path $objConfigDir24 | Out-Null
    }
    $j24Obj = Join-Path $objConfigDir24 "nested-obj-j"

    & cmd /c "mklink /J `"$j24Dist`" `"$canaryDir24`"" | Out-Null
    & cmd /c "mklink /J `"$j24Brand`" `"$canaryDir24`"" | Out-Null
    & cmd /c "mklink /J `"$j24Shell`" `"$canaryDir24`"" | Out-Null
    & cmd /c "mklink /J `"$j24Obj`" `"$canaryDir24`"" | Out-Null
    try {
        $r24A = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg24AOut,
            "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $r24B = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg24BOut,
            "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $r24C = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg24COut,
            "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $r24D = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg24DOut,
            "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $canariesIntact24 = ((Get-Content -LiteralPath $canary24Txt -Raw) -eq "CANARY_24_TXT") -and
                            ((Get-Content -LiteralPath $canary24Dll -Raw) -eq "CANARY_24_DLL")
        $stage24DCreated = Test-Path -LiteralPath (Join-Path $neg24DOut "staging\Salryn")
        $ok24 = ($r24A.ExitCode -ne 0) -and ($r24A.Combined -match 'code=OUTPUT_DIR_REPARSE_POINT_FORBIDDEN') -and
                ($r24B.ExitCode -ne 0) -and ($r24B.Combined -match 'code=OUTPUT_DIR_REPARSE_POINT_FORBIDDEN') -and
                ($r24C.ExitCode -ne 0) -and ($r24C.Combined -match 'code=OUTPUT_DIR_REPARSE_POINT_FORBIDDEN') -and
                ($r24D.ExitCode -ne 0) -and ($r24D.Combined -match 'code=REPARSE_POINT_FORBIDDEN') -and
                $canariesIntact24 -and (-not $stage24DCreated)
        Record-TestResult -Id "NEG-24_F2_NESTED_REPARSE_POINTS_IN_STAGE_SHELL_OBJ" -Passed $ok24 -ObservedCode "NESTED_REPARSE_ABORT_NO_DELETE" -Notes "A=$($r24A.ExitCode),B=$($r24B.ExitCode),C=$($r24C.ExitCode),D=$($r24D.ExitCode),canaries=$canariesIntact24"
    } finally {
        foreach ($j in @($j24Dist, $j24Brand, $j24Shell, $j24Obj)) {
            $jItem = Get-Item -LiteralPath $j -Force -ErrorAction SilentlyContinue
            if ($null -ne $jItem) { [System.IO.Directory]::Delete($j, $false) }
        }
    }

    # -----------------------------------------------------------------------
    # NEG-25 (F2): Unresolvable / dangling protected-root canonical resolution
    #              fails closed before any output directory creation or mutation
    # -----------------------------------------------------------------------
    $neg25Out = Join-Path $outBase "neg25"
    $danglingTarget25 = Join-Path $testRoot "dangling-target-25"
    $danglingProt25 = Join-Path $testRoot "dangling-protected-25"
    New-Item -ItemType Directory -Force -Path $danglingTarget25 | Out-Null
    & cmd /c "mklink /J `"$danglingProt25`" `"$danglingTarget25`"" | Out-Null
    [System.IO.Directory]::Delete($danglingTarget25, $false)
    try {
        $r25 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-OutputDir", $neg25Out, "-AdditionalProtectedRoots", $danglingProt25,
            "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $neg25Created = Test-Path -LiteralPath $neg25Out
        $ok25 = ($r25.ExitCode -ne 0) -and ($r25.Combined -match 'code=PROTECTED_ROOT_RESOLUTION_FAILED') -and (-not $neg25Created)
        Record-TestResult -Id "NEG-25_F2_UNRESOLVABLE_PROTECTED_ROOT_FAILS_CLOSED" -Passed $ok25 -ObservedCode "PROTECTED_ROOT_RESOLUTION_FAILED" -Notes "exit=$($r25.ExitCode), outCreated=$neg25Created"
    } finally {
        $dpItem = Get-Item -LiteralPath $danglingProt25 -Force -ErrorAction SilentlyContinue
        if ($null -ne $dpItem) { [System.IO.Directory]::Delete($danglingProt25, $false) }
    }

    # -----------------------------------------------------------------------
    # NEG-22 (F1+F3): Locked web dependency failures (missing SRI integrity,
    #                 corrupted lockfile SRI rejected by npm ci before Vite runs,
    #                 and nested junction in node_modules) fail closed cleanly
    #                 on fresh private OutputDirs with zero setup artifacts
    # -----------------------------------------------------------------------
    $neg22AOut = Join-Path $outBase "neg22a"
    $dispPkgLock22 = Join-Path $dispWorktree "src\Salryn.App.Web\package-lock.json"
    $origPkgLock22Raw = [System.IO.File]::ReadAllText($dispPkgLock22)
    try {
        $prevCheck22 = $env:SALRYN_CHECK_WEB_ROOT
        try {
            $env:SALRYN_CHECK_WEB_ROOT = Join-Path $dispWorktree "src\Salryn.App.Web"
            & node -e "const fs=require('fs'),p=require('path').join(process.env.SALRYN_CHECK_WEB_ROOT,'package-lock.json');const l=JSON.parse(fs.readFileSync(p,'utf8'));const k='node_modules/vite';if(!l.packages||!l.packages[k])throw new Error('vite missing');delete l.packages[k].integrity;fs.writeFileSync(p,JSON.stringify(l,null,2)+'\n');"
            if ($LASTEXITCODE -ne 0) { throw "Failed creating missing-integrity lockfile for NEG-22A" }
        } finally {
            $env:SALRYN_CHECK_WEB_ROOT = $prevCheck22
        }
        & git -C $dispWorktree add "src/Salryn.App.Web/package-lock.json" | Out-Null
        & git -C $dispWorktree -c user.name="QA Probe" -c user.email="qa@example.invalid" commit -m "temp missing SRI integrity probe" | Out-Null
        $missingSriHead = (& git -C $dispWorktree rev-parse --verify HEAD 2>$null | Out-String).Trim().ToLowerInvariant()
        $r22A = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg22AOut,
            "-ExpectedSourceSha", $missingSriHead, "-BuildInstaller"
        )
        $setupFiles22A = @(Get-ChildItem -LiteralPath (Join-Path $neg22AOut "setup") -Force -ErrorAction SilentlyContinue).Count
    } finally {
        & git -C $dispWorktree checkout --detach $headSha | Out-Null
        if (([System.IO.File]::ReadAllText($dispPkgLock22)) -ne $origPkgLock22Raw) {
            [System.IO.File]::WriteAllText($dispPkgLock22, $origPkgLock22Raw, (New-Object System.Text.UTF8Encoding($false)))
        }
    }

    $neg22BOut = Join-Path $outBase "neg22b"
    $neg22BCanary = Join-Path $testRoot "neg22b-vite-canary.txt"
    $neg22BCanaryJs = $neg22BCanary.Replace('\', '\\')
    try {
        $prevCheck22B = $env:SALRYN_CHECK_WEB_ROOT
        try {
            $env:SALRYN_CHECK_WEB_ROOT = Join-Path $dispWorktree "src\Salryn.App.Web"
            & node -e "const fs=require('fs'),p=require('path').join(process.env.SALRYN_CHECK_WEB_ROOT,'package-lock.json');const l=JSON.parse(fs.readFileSync(p,'utf8'));const k='node_modules/vite';l.packages[k].integrity='sha512-'+'A'.repeat(86)+'==';fs.writeFileSync(p,JSON.stringify(l,null,2)+'\n');"
            if ($LASTEXITCODE -ne 0) { throw "Failed creating corrupted-integrity lockfile for NEG-22B" }
        } finally {
            $env:SALRYN_CHECK_WEB_ROOT = $prevCheck22B
        }
        & git -C $dispWorktree add "src/Salryn.App.Web/package-lock.json" | Out-Null
        & git -C $dispWorktree -c user.name="QA Probe" -c user.email="qa@example.invalid" commit -m "temp corrupted SRI integrity probe" | Out-Null
        $badSriHead = (& git -C $dispWorktree rev-parse --verify HEAD 2>$null | Out-String).Trim().ToLowerInvariant()
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dispViteBin) | Out-Null
        Write-Utf8NoBom -Path $dispViteBin -Content "require('fs').writeFileSync('$neg22BCanaryJs', 'CORRUPT_LOCK_VITE_RAN');`nprocess.exit(99);`n"
        $r22B = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg22BOut,
            "-ExpectedSourceSha", $badSriHead, "-BuildInstaller"
        )
        $setupFiles22B = @(Get-ChildItem -LiteralPath (Join-Path $neg22BOut "setup") -Force -ErrorAction SilentlyContinue).Count
        $canaryRan22B = Test-Path -LiteralPath $neg22BCanary
    } finally {
        & git -C $dispWorktree checkout --detach $headSha | Out-Null
    }

    $neg22COut = Join-Path $outBase "neg22c"
    $canaryDir22C = Join-Path $testRoot "canary-target-22c"
    New-Item -ItemType Directory -Force -Path $canaryDir22C | Out-Null
    $canary22CTxt = Join-Path $canaryDir22C "canary.txt"
    Write-Utf8NoBom -Path $canary22CTxt -Content "CANARY_22C_INTACT"
    New-Item -ItemType Directory -Force -Path $dispNodeModules | Out-Null
    $j22CNm = Join-Path $dispNodeModules "nested-nm-j"
    & cmd /c "mklink /J `"$j22CNm`" `"$canaryDir22C`"" | Out-Null
    if (-not (Test-Path -LiteralPath $j22CNm)) { throw "Failed creating junction at $j22CNm" }
    try {
        $r22C = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg22COut,
            "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $out22CCreated = Test-Path -LiteralPath $neg22COut
        $canaryIntact22C = ((Get-Content -LiteralPath $canary22CTxt -Raw) -eq "CANARY_22C_INTACT")
    } finally {
        if (Test-Path -LiteralPath $j22CNm) { [System.IO.Directory]::Delete($j22CNm, $false) }
    }

    $ok22 = ($r22A.ExitCode -ne 0) -and ($r22A.Combined -match 'code=WEB_LOCKFILE_INTEGRITY_MISSING') -and ($setupFiles22A -eq 0) -and
            ($r22B.ExitCode -ne 0) -and ($r22B.Combined -match 'code=WEB_NPM_CI_FAILED') -and (-not $canaryRan22B) -and ($setupFiles22B -eq 0) -and
            ($r22C.ExitCode -ne 0) -and ($r22C.Combined -match 'code=REPARSE_POINT_FORBIDDEN') -and (-not $out22CCreated) -and $canaryIntact22C
    Record-TestResult -Id "NEG-22_F1_F3_LOCKFILE_INTEGRITY_AND_NPM_CI_FAILURES" -Passed $ok22 -ObservedCode "LOCKFILE_SRI+NPM_CI+NM_JUNCTION" -Notes "A=$($r22A.ExitCode)/$setupFiles22A,B=$($r22B.ExitCode)/$canaryRan22B/$setupFiles22B,C=$($r22C.ExitCode)/$canaryIntact22C"

    # -----------------------------------------------------------------------
    # NEG-14: Failed dotnet publish (broken C# on temp detached commit) in
    #         private OutputDir must fail closed and leave zero setup files
    # -----------------------------------------------------------------------
    $neg14Out = Join-Path $outBase "neg14"
    $brokenCs = Join-Path $dispWorktree "src\Salryn.Api.Local\BrokenProvenanceProbe.cs"
    try {
        Write-Utf8NoBom -Path $brokenCs -Content "this is intentional syntax error for publish failure test;"
        & git -C $dispWorktree add "src/Salryn.Api.Local/BrokenProvenanceProbe.cs" | Out-Null
        & git -C $dispWorktree -c user.name="QA Probe" -c user.email="qa@example.invalid" commit -m "temp broken publish probe" | Out-Null
        $brokenHead = (& git -C $dispWorktree rev-parse --verify HEAD 2>$null | Out-String).Trim().ToLowerInvariant()
        $rPubFail = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg14Out,
            "-ExpectedSourceSha", $brokenHead, "-BuildInstaller"
        )
        $setupFiles14 = @(Get-ChildItem -LiteralPath (Join-Path $neg14Out "setup") -Force -ErrorAction SilentlyContinue).Count
        $ok = ($rPubFail.ExitCode -ne 0) -and ($rPubFail.Combined -match 'code=DOTNET_PUBLISH_API_FAILED') -and ($setupFiles14 -eq 0)
        Record-TestResult -Id "NEG-14_FAILED_DOTNET_PUBLISH_PURGES_STALE_EXE" -Passed $ok -ObservedCode "DOTNET_PUBLISH_API_FAILED" -Notes "exit=$($rPubFail.ExitCode), setupFiles=$setupFiles14"
    } finally {
        & git -C $dispWorktree checkout --detach $headSha | Out-Null
        if (Test-Path -LiteralPath $brokenCs) { Remove-Item -Force -LiteralPath $brokenCs }
    }

    # -----------------------------------------------------------------------
    # NEG-21 (F3): Failed frontend build (npm run build) in private OutputDir
    #              fails closed and leaves zero setup artifacts
    # -----------------------------------------------------------------------
    $neg21Out = Join-Path $outBase "neg21"
    $brokenTs = Join-Path $dispWorktree "src\Salryn.App.Web\src\BrokenUiProbe.ts"
    try {
        Write-Utf8NoBom -Path $brokenTs -Content "export const broken: number = 'intentional_type_error';"
        & git -C $dispWorktree add "src/Salryn.App.Web/src/BrokenUiProbe.ts" | Out-Null
        & git -C $dispWorktree -c user.name="QA Probe" -c user.email="qa@example.invalid" commit -m "temp broken frontend probe" | Out-Null
        $brokenWebHead = (& git -C $dispWorktree rev-parse --verify HEAD 2>$null | Out-String).Trim().ToLowerInvariant()
        $r21 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg21Out,
            "-ExpectedSourceSha", $brokenWebHead, "-BuildInstaller"
        )
        $setupFiles21 = @(Get-ChildItem -LiteralPath (Join-Path $neg21Out "setup") -Force -ErrorAction SilentlyContinue).Count
        $ok21 = ($r21.ExitCode -ne 0) -and ($r21.Combined -match 'code=WEB_BUILD_FAILED') -and ($setupFiles21 -eq 0)
        Record-TestResult -Id "NEG-21_F3_WEB_BUILD_FAILURE_PURGES_PRESEEDED_SETUP_EXE" -Passed $ok21 -ObservedCode "WEB_BUILD_FAILED" -Notes "exit=$($r21.ExitCode), setupFiles=$setupFiles21"
    } finally {
        & git -C $dispWorktree checkout --detach $headSha | Out-Null
        if (Test-Path -LiteralPath $brokenTs) { Remove-Item -Force -LiteralPath $brokenTs }
    }

    # -----------------------------------------------------------------------
    # NEG-27 (F2): Private per-run OutputDir exclusivity, restricted ACLs,
    #              and pinned scan-to-delete directory handles block concurrent
    #              junction swaps and fail closed on post-scan injection
    # -----------------------------------------------------------------------
    $neg27AOut = Join-Path $outBase "neg27a-existing"
    New-Item -ItemType Directory -Force -Path $neg27AOut | Out-Null
    $keep27A = Join-Path $neg27AOut "keep.txt"
    Write-Utf8NoBom -Path $keep27A -Content "PRE_EXISTING_PRIVATE_GUARD"
    $r27ABuild = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg27AOut,
        "-ExpectedSourceSha", $headSha, "-BuildInstaller"
    )
    $r27ASkip = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg27AOut,
        "-ExpectedSourceSha", $headSha, "-SkipWebBuild"
    )
    $keepIntact27A = ((Get-Content -LiteralPath $keep27A -Raw) -eq "PRE_EXISTING_PRIVATE_GUARD")

    $pos01Acl = [System.IO.Directory]::GetAccessControl($pos01Out)
    $curUserSidVal = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $allowedSids = @($curUserSidVal, "S-1-5-18", "S-1-5-32-544")
    $aclOk27B = $pos01Acl.AreAccessRulesProtected
    if ($aclOk27B) {
        $accessRules = @($pos01Acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]))
        if ($accessRules.Count -eq 0) { $aclOk27B = $false }
        foreach ($ar in $accessRules) {
            if ($allowedSids -notcontains $ar.IdentityReference.Value) {
                $aclOk27B = $false
                break
            }
        }
    }

    $neg27COut = Join-Path $outBase "neg27c-race"
    $canary27C = Join-Path $testRoot "canary-27c"
    New-Item -ItemType Directory -Force -Path $canary27C | Out-Null
    $canary27CTxt = Join-Path $canary27C "canary.txt"
    Write-Utf8NoBom -Path $canary27CTxt -Content "CANARY_27C_PROTECTED"
    $nmRaceSub27 = Join-Path $dispNodeModules "sub27-race"
    New-Item -ItemType Directory -Force -Path $nmRaceSub27 | Out-Null
    Write-Utf8NoBom -Path (Join-Path $nmRaceSub27 "race.txt") -Content "race-seed"
    $hookLog27C = Join-Path $testRoot "post-scan-hook-27c.log"
    $hookScript27C = Join-Path $testRoot "post-scan-hook-27c.ps1"
    $hookContent27C = @'
param([string]$ContextLabel, [string]$TargetRoot)
if ($ContextLabel -ne "web-node-modules") { return }
$sub = Join-Path $TargetRoot "sub27-race"
$logPath = "__LOG_PATH__"
$canaryTarget = "__CANARY_TARGET__"
$moved = $sub + "-moved"
$moveBlocked = $false
try {
    [System.IO.Directory]::Move($sub, $moved)
    & cmd /c "mklink /J `"$sub`" `"$canaryTarget`"" | Out-Null
} catch {
    $moveBlocked = $true
}
[System.IO.File]::WriteAllText((Join-Path $sub "injected-after-scan.txt"), "RACE_INJECTED", (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($logPath, "MOVE_BLOCKED=$moveBlocked", (New-Object System.Text.UTF8Encoding($false)))
'@
    $hookContent27C = $hookContent27C.Replace("__LOG_PATH__", $hookLog27C).Replace("__CANARY_TARGET__", $canary27C)
    Write-Utf8NoBom -Path $hookScript27C -Content $hookContent27C

    $prevHook27C = $env:SALRYN_TEST_POST_SCAN_HOOK
    try {
        $env:SALRYN_TEST_POST_SCAN_HOOK = $hookScript27C
        $r27C = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg27COut,
            "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
    } finally {
        $env:SALRYN_TEST_POST_SCAN_HOOK = $prevHook27C
        if (Test-Path -LiteralPath $dispWebDist) { Remove-Item -Recurse -Force -LiteralPath $dispWebDist }
        & robocopy $pos01DistBackup $dispWebDist /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    }
    $hookLogRaw27C = if (Test-Path -LiteralPath $hookLog27C) { (Get-Content -LiteralPath $hookLog27C -Raw).Trim() } else { "MISSING" }
    $canaryIntact27C = ((Get-Content -LiteralPath $canary27CTxt -Raw) -eq "CANARY_27C_PROTECTED")
    $ok27C = ($r27C.ExitCode -ne 0) -and ($r27C.Combined -match 'code=REPARSE_POINT_FORBIDDEN') -and ($hookLogRaw27C -eq "MOVE_BLOCKED=True") -and $canaryIntact27C

    $ok27 = ($r27ABuild.ExitCode -ne 0) -and ($r27ABuild.Combined -match 'code=NONPRIVATE_OUTPUT_DIR_FORBIDDEN') -and
            ($r27ASkip.ExitCode -ne 0) -and ($r27ASkip.Combined -match 'code=NONPRIVATE_OUTPUT_DIR_FORBIDDEN') -and
            $keepIntact27A -and $aclOk27B -and $ok27C
    Record-TestResult -Id "NEG-27_F2_PRIVATE_OUTPUT_OWNERSHIP_AND_SCAN_TO_DELETE_RACE_SAFE" -Passed $ok27 -ObservedCode "PRIVATE_OUT+ACL+PINNED_LOCK" -Notes "27A=$keepIntact27A,27B_ACL=$aclOk27B,27C_Hook=$hookLogRaw27C,canary=$canaryIntact27C"

    Write-Host ""
    Write-Host "=== INSTALLER PROVENANCE REGRESSION SUMMARY ==="
    $results | Format-Table -AutoSize | Out-String | Write-Host
}
finally {
    if ($suiteFailed -and $KeepTempOnFailure) {
        Write-SuiteInfo "Preserving temp test directory due to failure (-KeepTempOnFailure): $testRoot"
    } else {
        if (Test-Path -LiteralPath $dispWorktree) {
            Write-SuiteInfo "Removing disposable git worktree..."
            if (Test-Path -LiteralPath $dispNodeModules) {
                Remove-Item -Recurse -Force -LiteralPath $dispNodeModules -ErrorAction SilentlyContinue
            }
            & git -C $sourceRepoRoot worktree remove --force $dispWorktree 2>$null | Out-Null
        }
        if (Test-Path -LiteralPath $testRoot) {
            Remove-Item -Recurse -Force -LiteralPath $testRoot -ErrorAction SilentlyContinue
        }
    }
}

if ($suiteFailed) {
    throw "One or more installer provenance regression tests FAILED."
}

Write-Host "ALL_PROVENANCE_REGRESSION_TESTS_PASSED ($($results.Count)/$($results.Count))"
exit 0
