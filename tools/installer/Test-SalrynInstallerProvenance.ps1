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
    if (Test-Path -LiteralPath $srcNodeModules) {
        Write-SuiteInfo "Seeding gitignored node_modules into disposable worktree..."
        & robocopy $srcNodeModules $dispNodeModules /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
        if ($LASTEXITCODE -ge 8) {
            throw "robocopy of node_modules failed with exit code $LASTEXITCODE"
        }
    } else {
        Write-SuiteInfo "Installing frontend packages via npm ci in disposable worktree..."
        & npm --prefix (Join-Path $dispWorktree "src\Salryn.App.Web") ci --prefer-offline
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
    }

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
    # POS-01: Full fresh clean build + QA-only ISCC compile in isolated nested OutputDir
    #         Pre-seeds forged ignored worktree dist + stamp AND stale setup EXE +
    #         sidecars to verify Option A wipes ignored dist and builds fresh from source.
    # -----------------------------------------------------------------------
    $pos01Out = Join-Path $outBase "pos01\nested\clean-run"
    $pos01SetupDir = Join-Path $pos01Out "setup"
    New-Item -ItemType Directory -Force -Path $pos01SetupDir | Out-Null
    $pos01SetupExe = Join-Path $pos01SetupDir "Salryn-POS-BetaRC1-Setup.exe"
    $pos01StaleSha = "$pos01SetupExe.sha256.txt"
    $pos01StaleProv = "$pos01SetupExe.provenance.json"
    Write-Utf8NoBom -Path $pos01SetupExe -Content "STALE_DUMMY_EXE!"
    Write-Utf8NoBom -Path $pos01StaleSha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $pos01StaleProv -Content '{"stale":true}'

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

    Write-SuiteInfo "Running POS-01 full fresh build + QA-only ISCC compile in isolated nested dir..."
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
    # NEG-11: Option A blocks -CompileExistingStageOnly -BuildInstaller AND
    #         -VerifyStageOnly detects tampered staged payload before ISCC
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
    $neg11ExeAfter = Test-Path -LiteralPath $neg11ExePath

    $r11Verify = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg11Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-VerifyStageOnly"
    )
    $ok = ($r11Compile.ExitCode -ne 0) -and ($r11Compile.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and (-not $neg11ExeAfter) -and
          ($r11Verify.ExitCode -ne 0) -and ($r11Verify.Combined -match 'code=STAGE_MANIFEST_INVENTORY_MISMATCH|STAGE_MANIFEST_FILE_MISMATCH')
    Record-TestResult -Id "NEG-11_TAMPERED_STAGE_REJECTED_BEFORE_ISCC" -Passed $ok -ObservedCode "OPTION_A+STAGE_MISMATCH" -Notes "compileExit=$($r11Compile.ExitCode), verifyExit=$($r11Verify.ExitCode), setupExists=$neg11ExeAfter"

    # -----------------------------------------------------------------------
    # NEG-12: Option A blocks -CompileExistingStageOnly AND -VerifyStageOnly
    #         detects missing required staged binary
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
    $neg12ExeAfter = Test-Path -LiteralPath $neg12ExePath

    $r12Verify = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg12Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-VerifyStageOnly"
    )
    $ok = ($r12Compile.ExitCode -ne 0) -and ($r12Compile.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and (-not $neg12ExeAfter) -and
          ($r12Verify.ExitCode -ne 0) -and ($r12Verify.Combined -match 'code=MISSING_STAGED_FILE')
    Record-TestResult -Id "NEG-12_MISSING_STAGE_BINARY_REJECTED" -Passed $ok -ObservedCode "OPTION_A+MISSING_STAGED_FILE" -Notes "compileExit=$($r12Compile.ExitCode), verifyExit=$($r12Verify.ExitCode), setupExists=$neg12ExeAfter"

    # -----------------------------------------------------------------------
    # NEG-13: Fresh -BuildInstaller with failing ISCC, no-op ISCC, and
    #         mid-ISCC stage tampering must fail closed and purge stale/new EXE + sidecars
    # -----------------------------------------------------------------------
    $neg13AOut = Join-Path $outBase "neg13a"
    $neg13ASetupDir = Join-Path $neg13AOut "setup"
    New-Item -ItemType Directory -Force -Path $neg13ASetupDir | Out-Null
    $neg13AExe = Join-Path $neg13ASetupDir "Salryn-POS-BetaRC1-Setup.exe"
    $neg13ASha = "$neg13AExe.sha256.txt"
    $neg13AProv = "$neg13AExe.provenance.json"
    Write-Utf8NoBom -Path $neg13AExe -Content "STALE_PREEXISTING_13A"
    Write-Utf8NoBom -Path $neg13ASha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg13AProv -Content '{"stale":true}'

    $fakeFailIscc = Join-Path $testRoot "fail-iscc.cmd"
    Write-Utf8NoBom -Path $fakeFailIscc -Content "@echo off`r`necho Simulated ISCC failure`r`nexit /b 2`r`n"
    $rFail = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg13AOut,
        "-ExpectedSourceSha", $headSha, "-BuildInstaller", "-InnoSetupCompilerPath", $fakeFailIscc
    )
    $staleSurvivedFail = (Test-Path -LiteralPath $neg13AExe) -or (Test-Path -LiteralPath $neg13ASha) -or (Test-Path -LiteralPath $neg13AProv)

    $neg13BOut = Join-Path $outBase "neg13b"
    $neg13BSetupDir = Join-Path $neg13BOut "setup"
    New-Item -ItemType Directory -Force -Path $neg13BSetupDir | Out-Null
    $neg13BExe = Join-Path $neg13BSetupDir "Salryn-POS-BetaRC1-Setup.exe"
    $neg13BSha = "$neg13BExe.sha256.txt"
    $neg13BProv = "$neg13BExe.provenance.json"
    Write-Utf8NoBom -Path $neg13BExe -Content "STALE_PREEXISTING_13B"
    Write-Utf8NoBom -Path $neg13BSha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg13BProv -Content '{"stale":true}'

    $fakeNoopIscc = Join-Path $testRoot "noop-iscc.cmd"
    Write-Utf8NoBom -Path $fakeNoopIscc -Content "@echo off`r`necho Simulated ISCC wrote nothing`r`nexit /b 0`r`n"
    $rNoop = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg13BOut,
        "-ExpectedSourceSha", $headSha, "-BuildInstaller", "-InnoSetupCompilerPath", $fakeNoopIscc
    )
    $staleSurvivedNoop = (Test-Path -LiteralPath $neg13BExe) -or (Test-Path -LiteralPath $neg13BSha) -or (Test-Path -LiteralPath $neg13BProv)

    $neg13COut = Join-Path $outBase "neg13c"
    $neg13CSetupDir = Join-Path $neg13COut "setup"
    New-Item -ItemType Directory -Force -Path $neg13CSetupDir | Out-Null
    $neg13CExe = Join-Path $neg13CSetupDir "Salryn-POS-BetaRC1-Setup.exe"
    $neg13CSha = "$neg13CExe.sha256.txt"
    $neg13CProv = "$neg13CExe.provenance.json"
    Write-Utf8NoBom -Path $neg13CExe -Content "STALE_PREEXISTING_13C"
    Write-Utf8NoBom -Path $neg13CSha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg13CProv -Content '{"stale":true}'

    $neg13CDistIdx = Join-Path $neg13COut "staging\Salryn\dist\index.html"
    $fakeTamperIscc = Join-Path $testRoot "tamper-iscc.cmd"
    $tamperCmd = "@echo off`r`n(echo FAKE_MID_ISCC_SETUP)>`"$neg13CExe`"`r`n(echo mid-iscc-tamper)>>`"$neg13CDistIdx`"`r`nexit /b 0`r`n"
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
    # NEG-15: Option A blocks -CompileExistingStageOnly AND -VerifyStageOnly
    #         rejects path traversal in stage manifest (even with updated .sha256)
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
    $neg15ExeAfter = Test-Path -LiteralPath $neg15ExePath

    $r15Verify = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg15Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $newMSha, "-VerifyStageOnly"
    )
    $ok15 = ($r15Compile.ExitCode -ne 0) -and ($r15Compile.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and (-not $neg15ExeAfter) -and
            ($r15Verify.ExitCode -ne 0) -and ($r15Verify.Combined -match 'code=PATH_TRAVERSAL_FORBIDDEN')
    Record-TestResult -Id "NEG-15_PATH_TRAVERSAL_IN_STAGE_MANIFEST_REJECTED" -Passed $ok15 -ObservedCode "OPTION_A+PATH_TRAVERSAL_FORBIDDEN" -Notes "compileExit=$($r15Compile.ExitCode), verifyExit=$($r15Verify.ExitCode), setupExists=$neg15ExeAfter"

    # -----------------------------------------------------------------------
    # NEG-16 (F1): -CompileExistingStageOnly without external pinned manifest
    #              SHA-256 fails closed under Option A and purges setup artifacts
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
    $neg16StaleLeft = (Test-Path -LiteralPath $neg16StaleExe) -or (Test-Path -LiteralPath $neg16StaleSha) -or (Test-Path -LiteralPath $neg16StaleProv)
    $ok16 = ($r16.ExitCode -ne 0) -and ($r16.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and (-not $neg16StaleLeft)
    Record-TestResult -Id "NEG-16_F1_COMPILE_EXISTING_STAGE_FORBIDDEN" -Passed $ok16 -ObservedCode "COMPILE_EXISTING_STAGE_FORBIDDEN" -Notes "exit=$($r16.ExitCode), staleLeft=$neg16StaleLeft"

    # -----------------------------------------------------------------------
    # NEG-17 (F1): Coordinated forgery of ignored worktree dist+stamp AND staged
    #              payload + consistently recomputed manifest ID/dist/inventory/.sha256
    #              is rejected unconditionally by Option A (-CompileExistingStageOnly forbidden)
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
        $neg17StaleLeft = (Test-Path -LiteralPath $neg17Exe) -or (Test-Path -LiteralPath $neg17Sha) -or (Test-Path -LiteralPath $neg17Prov)
        $ok17 = ($r17.ExitCode -ne 0) -and ($forgedSha17 -ne $pos01ManifestSha) -and ($r17.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and (-not $neg17StaleLeft)
        Record-TestResult -Id "NEG-17_F1_COORDINATED_STAGE_AND_DIST_FORGERY_REJECTED" -Passed $ok17 -ObservedCode "COMPILE_EXISTING_STAGE_FORBIDDEN" -Notes "exit=$($r17.ExitCode), forgedDiffers=$($forgedSha17 -ne $pos01ManifestSha), staleLeft=$neg17StaleLeft"
    } finally {
        if (Test-Path -LiteralPath $dispWebDist) { Remove-Item -Recurse -Force -LiteralPath $dispWebDist }
        & robocopy $pos01DistBackup $dispWebDist /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Failed restoring clean web dist after NEG-17" }
    }

    # -----------------------------------------------------------------------
    # NEG-18 (F1): Coordinated forged ignored worktree dist+stamp blocked on
    #              -SkipWebBuild -BuildInstaller, AND -VerifyStageOnly still
    #              catches staged dist & source branding asset parity tampering
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
        $neg18AStaleLeft = (Test-Path -LiteralPath $neg18AExe) -or (Test-Path -LiteralPath $neg18ASha) -or (Test-Path -LiteralPath $neg18AProv)
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

    $ok18 = ($r18A.ExitCode -ne 0) -and ($r18A.Combined -match 'code=SKIP_WEB_BUILD_FORBIDDEN_FOR_INSTALLER') -and (-not $neg18AStaleLeft) -and
            ($r18B.ExitCode -ne 0) -and ($r18B.Combined -match 'code=STAGE_WEB_DIST_PROVENANCE_MISMATCH') -and
            ($r18C.ExitCode -ne 0) -and ($r18C.Combined -match 'code=STAGE_BRANDING_ASSET_MISMATCH')
    Record-TestResult -Id "NEG-18_F1_SKIPWEB_FORGERY_AND_STAGE_PARITY" -Passed $ok18 -ObservedCode "SKIPWEB_FORBIDDEN+STAGE_PARITY" -Notes "skipExit=$($r18A.ExitCode), distVerify=$($r18B.ExitCode), brandVerify=$($r18C.ExitCode)"

    # -----------------------------------------------------------------------
    # NEG-23 (F1 Option A): Authentic existing stage (with exact pinned SHA)
    #                       and authentic stamped dist are BOTH blocked from
    #                       reused installer packaging (fresh-build-only lane)
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
    $stale23A = (Test-Path -LiteralPath $neg23AExe) -or (Test-Path -LiteralPath $neg23ASha) -or (Test-Path -LiteralPath $neg23AProv)

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
    $stale23B = (Test-Path -LiteralPath $neg23BExe) -or (Test-Path -LiteralPath $neg23BSha) -or (Test-Path -LiteralPath $neg23BProv)

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
    $stale23C = (Test-Path -LiteralPath $neg23CExe) -or (Test-Path -LiteralPath $neg23CSha) -or (Test-Path -LiteralPath $neg23CProv)

    $ok23 = ($r23A.ExitCode -ne 0) -and ($r23A.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and (-not $stale23A) -and
            ($r23B.ExitCode -ne 0) -and ($r23B.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and (-not $stale23B) -and
            ($r23C.ExitCode -ne 0) -and ($r23C.Combined -match 'code=SKIP_WEB_BUILD_FORBIDDEN_FOR_INSTALLER') -and (-not $stale23C)
    Record-TestResult -Id "NEG-23_F1_OPTION_A_BLOCKS_AUTHENTIC_REUSE" -Passed $ok23 -ObservedCode "OPTION_A_BLOCKS_STAGE_AND_SKIPWEB" -Notes "A=$($r23A.ExitCode),B=$($r23B.ExitCode),C=$($r23C.ExitCode)"

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
    # NEG-22 (F3): Option A early gate failures and locked web dependency
    #              mismatch purge pre-seeded .exe, .sha256.txt, and .provenance.json
    # -----------------------------------------------------------------------
    $neg22AOut = Join-Path $outBase "neg22a"
    & robocopy (Join-Path $pos01Out "staging") (Join-Path $neg22AOut "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos01 staging for NEG-22A" }
    New-Item -ItemType Directory -Force -Path (Join-Path $neg22AOut "setup") | Out-Null
    $neg22AExe = Join-Path $neg22AOut "setup\Salryn-POS-BetaRC1-Setup.exe"
    $neg22ASha = "$neg22AExe.sha256.txt"
    $neg22AProv = "$neg22AExe.provenance.json"
    Write-Utf8NoBom -Path $neg22AExe -Content "STALE_STAGE_VALIDATION_EXE"
    Write-Utf8NoBom -Path $neg22ASha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg22AProv -Content '{"stale":true}'
    $stageArgs22A = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg22AOut, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos01ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs22A += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r22A = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs22A
    $stale22A = (Test-Path -LiteralPath $neg22AExe) -or (Test-Path -LiteralPath $neg22ASha) -or (Test-Path -LiteralPath $neg22AProv)

    $neg22BOut = Join-Path $outBase "neg22b"
    New-Item -ItemType Directory -Force -Path (Join-Path $neg22BOut "setup") | Out-Null
    $neg22BExe = Join-Path $neg22BOut "setup\Salryn-POS-BetaRC1-Setup.exe"
    $neg22BSha = "$neg22BExe.sha256.txt"
    $neg22BProv = "$neg22BExe.provenance.json"
    Write-Utf8NoBom -Path $neg22BExe -Content "STALE_SKIPWEB_FAIL_EXE"
    Write-Utf8NoBom -Path $neg22BSha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg22BProv -Content '{"stale":true}'
    $r22B = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg22BOut,
        "-ExpectedSourceSha", $headSha, "-SkipWebBuild", "-BuildInstaller"
    )
    $stale22B = (Test-Path -LiteralPath $neg22BExe) -or (Test-Path -LiteralPath $neg22BSha) -or (Test-Path -LiteralPath $neg22BProv)

    $neg22COut = Join-Path $outBase "neg22c"
    New-Item -ItemType Directory -Force -Path (Join-Path $neg22COut "setup") | Out-Null
    $neg22CExe = Join-Path $neg22COut "setup\Salryn-POS-BetaRC1-Setup.exe"
    $neg22CSha = "$neg22CExe.sha256.txt"
    $neg22CProv = "$neg22CExe.provenance.json"
    Write-Utf8NoBom -Path $neg22CExe -Content "STALE_LOCKFILE_FAIL_EXE"
    Write-Utf8NoBom -Path $neg22CSha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg22CProv -Content '{"stale":true}'
    $nmLockPath22C = Join-Path $dispNodeModules ".package-lock.json"
    if (-not (Test-Path -LiteralPath $nmLockPath22C)) {
        throw "Expected node_modules\.package-lock.json missing for NEG-22C lockfile mismatch test: $nmLockPath22C"
    }
    $nmLockBak22C = Get-Content -LiteralPath $nmLockPath22C -Raw
    try {
        Write-Utf8NoBom -Path $nmLockPath22C -Content '{"name":"salryn-web","lockfileVersion":3,"packages":{"":{}}}'
        $r22C = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg22COut,
            "-ExpectedSourceSha", $headSha, "-BuildInstaller"
        )
        $stale22C = (Test-Path -LiteralPath $neg22CExe) -or (Test-Path -LiteralPath $neg22CSha) -or (Test-Path -LiteralPath $neg22CProv)
    } finally {
        Write-Utf8NoBom -Path $nmLockPath22C -Content $nmLockBak22C
    }

    $ok22 = ($r22A.ExitCode -ne 0) -and ($r22A.Combined -match 'code=COMPILE_EXISTING_STAGE_FORBIDDEN') -and (-not $stale22A) -and
            ($r22B.ExitCode -ne 0) -and ($r22B.Combined -match 'code=SKIP_WEB_BUILD_FORBIDDEN_FOR_INSTALLER') -and (-not $stale22B) -and
            ($r22C.ExitCode -ne 0) -and ($r22C.Combined -match 'code=WEB_LOCKFILE_MISMATCH') -and (-not $stale22C)
    Record-TestResult -Id "NEG-22_F3_OPTIONA_AND_LOCKFILE_FAILURES_PURGE_SETUP" -Passed $ok22 -ObservedCode "OPTIONA+LOCKFILE_PURGE" -Notes "staleA=$stale22A, staleB=$stale22B, staleC=$stale22C"

    # -----------------------------------------------------------------------
    # NEG-14: Failed dotnet publish (broken C# on temp detached commit) must
    #         fail closed and purge pre-existing stale setup EXE
    #         (Runs before NEG-21's broken-commit checkout; dist already verified)
    # -----------------------------------------------------------------------
    $neg14Out = Join-Path $outBase "neg14"
    $neg14SetupDir = Join-Path $neg14Out "setup"
    New-Item -ItemType Directory -Force -Path $neg14SetupDir | Out-Null
    $neg14StaleExe = Join-Path $neg14SetupDir "Salryn-POS-BetaRC1-Setup.exe"
    Write-Utf8NoBom -Path $neg14StaleExe -Content "STALE_PRE_PUBLISH_EXE"
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
        $staleAfterPubFail = Test-Path -LiteralPath $neg14StaleExe
        $ok = ($rPubFail.ExitCode -ne 0) -and ($rPubFail.Combined -match 'code=DOTNET_PUBLISH_API_FAILED') -and (-not $staleAfterPubFail)
        Record-TestResult -Id "NEG-14_FAILED_DOTNET_PUBLISH_PURGES_STALE_EXE" -Passed $ok -ObservedCode "DOTNET_PUBLISH_API_FAILED" -Notes "exit=$($rPubFail.ExitCode), staleAfterPubFail=$staleAfterPubFail"
    } finally {
        & git -C $dispWorktree checkout --detach $headSha | Out-Null
        if (Test-Path -LiteralPath $brokenCs) { Remove-Item -Force -LiteralPath $brokenCs }
    }

    # -----------------------------------------------------------------------
    # NEG-21 (F3): Failed frontend build (npm run build) purges pre-seeded
    #              Salryn-POS-BetaRC1-Setup.exe + .sha256.txt + .provenance.json
    # -----------------------------------------------------------------------
    $neg21Out = Join-Path $outBase "neg21"
    $neg21SetupDir = Join-Path $neg21Out "setup"
    New-Item -ItemType Directory -Force -Path $neg21SetupDir | Out-Null
    $neg21Exe = Join-Path $neg21SetupDir "Salryn-POS-BetaRC1-Setup.exe"
    $neg21Sha = "$neg21Exe.sha256.txt"
    $neg21Prov = "$neg21Exe.provenance.json"
    Write-Utf8NoBom -Path $neg21Exe -Content "STALE_PRE_WEB_BUILD_EXE"
    Write-Utf8NoBom -Path $neg21Sha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg21Prov -Content '{"stale":true}'

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
        $anyStale21 = (Test-Path -LiteralPath $neg21Exe) -or (Test-Path -LiteralPath $neg21Sha) -or (Test-Path -LiteralPath $neg21Prov)
        $ok21 = ($r21.ExitCode -ne 0) -and ($r21.Combined -match 'code=WEB_BUILD_FAILED') -and (-not $anyStale21)
        Record-TestResult -Id "NEG-21_F3_WEB_BUILD_FAILURE_PURGES_PRESEEDED_SETUP_EXE" -Passed $ok21 -ObservedCode "WEB_BUILD_FAILED" -Notes "exit=$($r21.ExitCode), anyStale=$anyStale21"
    } finally {
        & git -C $dispWorktree checkout --detach $headSha | Out-Null
        if (Test-Path -LiteralPath $brokenTs) { Remove-Item -Force -LiteralPath $brokenTs }
    }

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
