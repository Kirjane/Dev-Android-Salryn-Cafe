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
    # NEG-03: Mismatched -ExpectedSourceSha rejected BEFORE staging/builds
    # -----------------------------------------------------------------------
    $neg03Out = Join-Path $outBase "neg03"
    $wrongSha = "0" * 40
    $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @("-OutputDir", $neg03Out, "-ExpectedSourceSha", $wrongSha, "-SkipWebBuild")
    $stageCreated = Test-Path -LiteralPath (Join-Path $neg03Out "staging\Salryn")
    $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=EXPECTED_SHA_MISMATCH') -and ($r.Combined -match "head=$headSha") -and (-not $stageCreated)
    Record-TestResult -Id "NEG-03_EXPECTED_SHA_MISMATCH" -Passed $ok -ObservedCode "EXPECTED_SHA_MISMATCH" -Notes "exit=$($r.ExitCode), stageCreated=$stageCreated"

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
    # POS-01: Full clean build + QA-only ISCC compile in isolated OutputDir
    #         Also seeds a stale 16-byte Salryn-POS-BetaRC1-Setup.exe beforehand
    #         to verify purge + fresh compilation + manifest/provenance binding.
    # -----------------------------------------------------------------------
    $pos01Out = Join-Path $outBase "pos01"
    $pos01SetupDir = Join-Path $pos01Out "setup"
    New-Item -ItemType Directory -Force -Path $pos01SetupDir | Out-Null
    $pos01SetupExe = Join-Path $pos01SetupDir "Salryn-POS-BetaRC1-Setup.exe"
    Write-Utf8NoBom -Path $pos01SetupExe -Content "STALE_DUMMY_EXE!"

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

    Write-SuiteInfo "Running POS-01 full build + QA-only ISCC compile in isolated dir..."
    $posCompileStart = (Get-Date).ToUniversalTime()
    $rPos01 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $posArgs

    $manifestFile = Join-Path $pos01Out "staging\Salryn\salryn-installer-provenance-manifest.json"
    $manifestShaFile = Join-Path $pos01Out "staging\Salryn\salryn-installer-provenance-manifest.json.sha256"
    $provJsonFile = "$pos01SetupExe.provenance.json"
    $shaTxtFile = "$pos01SetupExe.sha256.txt"
    $desktopExe = Join-Path $pos01Out "staging\Salryn\Salryn.Desktop.WebView.exe"
    $apiExe = Join-Path $pos01Out "staging\Salryn\Salryn.Api.Local.exe"

    $posOk = ($rPos01.ExitCode -eq 0) -and
             (Test-Path -LiteralPath $manifestFile) -and
             (Test-Path -LiteralPath $manifestShaFile) -and
             (Test-Path -LiteralPath $pos01SetupExe) -and
             (Test-Path -LiteralPath $provJsonFile) -and
             (Test-Path -LiteralPath $shaTxtFile)

    $posDetails = ""
    if ($posOk) {
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
        $hasLeaks = ($manifestRaw -match $leakPattern) -or ($provRaw -match $leakPattern) -or ($shaTxtRaw -match $leakPattern)

        $posOk = ($setupItem.Length -gt 100000) -and
                 ($setupItem.LastWriteTimeUtc -ge $posCompileStart.AddSeconds(-2)) -and
                 ($provJson.setupExeSha256 -eq $setupHash) -and
                 ($provJson.sourceCommitSha -eq $headSha) -and
                 ($provJson.worktreeClean -eq $true) -and
                 ($provJson.packedPayloadVerification -eq "PACKED_PAYLOAD_NOT_VERIFIED") -and
                 ($manifestObj.source.commitSha -eq $headSha) -and
                 ($manifestObj.payloadFileCount -gt 0) -and
                 ($desktopVer -eq "$Version+$headSha") -and
                 ($apiVer -eq "$Version+$headSha") -and
                 (-not $hasLeaks)

        $posDetails = "setupBytes=$($setupItem.Length), setupSha256=$setupHash, payloadFiles=$($manifestObj.payloadFileCount), manifestId=$($manifestObj.manifestId), leaks=$hasLeaks"
    } else {
        $posDetails = "exit=$($rPos01.ExitCode) out=$($rPos01.Combined)"
    }
    Record-TestResult -Id "POS-01_CLEAN_FULL_STAGE_AND_ISCC_INSTALLER" -Passed $posOk -ObservedCode $(if ($posOk) { "COMPILED_SOURCE_BOUND" } else { "FAILED" }) -Notes $posDetails

    # -----------------------------------------------------------------------
    # POS-02: Valid -SkipWebBuild reuse against stamped dist on clean HEAD
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
        $pos02Ok = ($m2.buildOptions.skipWebBuild -eq $true) -and
                   ($m2.buildOptions.webDistInventorySha256 -eq $m1.buildOptions.webDistInventorySha256) -and
                   ($m2.source.commitSha -eq $headSha)
    }
    Record-TestResult -Id "POS-02_VALID_SKIP_WEB_BUILD_REUSE" -Passed $pos02Ok -ObservedCode $(if ($pos02Ok) { "VERIFIED_WEB_DIST_REUSE" } else { "FAILED" }) -Notes "exit=$($rPos02.ExitCode)"

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
    # NEG-11: Tampered staged payload file rejected before ISCC compile
    # -----------------------------------------------------------------------
    $neg11Out = Join-Path $outBase "neg11"
    & robocopy (Join-Path $pos02Out "staging") (Join-Path $neg11Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos02 staging for NEG-11" }
    New-Item -ItemType Directory -Force -Path (Join-Path $neg11Out "setup") | Out-Null
    Add-Content -LiteralPath (Join-Path $neg11Out "staging\Salryn\dist\index.html") -Value "<!-- tampered-stage -->"
    $stageArgs11 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg11Out, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos02ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs11 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs11
    $neg11Exe = Test-Path -LiteralPath (Join-Path $neg11Out "setup\Salryn-POS-BetaRC1-Setup.exe")
    $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=STAGE_MANIFEST_INVENTORY_MISMATCH|STAGE_MANIFEST_FILE_MISMATCH') -and (-not $neg11Exe)
    Record-TestResult -Id "NEG-11_TAMPERED_STAGE_REJECTED_BEFORE_ISCC" -Passed $ok -ObservedCode "STAGE_MANIFEST_MISMATCH" -Notes "exit=$($r.ExitCode), setupExists=$neg11Exe"

    # -----------------------------------------------------------------------
    # NEG-12: Missing required staged binary rejected before ISCC compile
    # -----------------------------------------------------------------------
    $neg12Out = Join-Path $outBase "neg12"
    & robocopy (Join-Path $pos02Out "staging") (Join-Path $neg12Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos02 staging for NEG-12" }
    New-Item -ItemType Directory -Force -Path (Join-Path $neg12Out "setup") | Out-Null
    Remove-Item -Force -LiteralPath (Join-Path $neg12Out "staging\Salryn\WebView2Loader.dll")
    $stageArgs12 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg12Out, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos02ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs12 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs12
    $neg12Exe = Test-Path -LiteralPath (Join-Path $neg12Out "setup\Salryn-POS-BetaRC1-Setup.exe")
    $ok = ($r.ExitCode -ne 0) -and ($r.Combined -match 'code=MISSING_STAGED_FILE') -and (-not $neg12Exe)
    Record-TestResult -Id "NEG-12_MISSING_STAGE_BINARY_REJECTED" -Passed $ok -ObservedCode "MISSING_STAGED_FILE" -Notes "exit=$($r.ExitCode), setupExists=$neg12Exe"

    # -----------------------------------------------------------------------
    # NEG-13: Failing ISCC + no-op ISCC must purge stale Salryn-POS-BetaRC1-Setup.exe
    # -----------------------------------------------------------------------
    $neg13Out = Join-Path $outBase "neg13"
    & robocopy (Join-Path $pos02Out "staging") (Join-Path $neg13Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos02 staging for NEG-13" }
    $neg13SetupDir = Join-Path $neg13Out "setup"
    New-Item -ItemType Directory -Force -Path $neg13SetupDir | Out-Null
    $neg13StaleExe = Join-Path $neg13SetupDir "Salryn-POS-BetaRC1-Setup.exe"
    Write-Utf8NoBom -Path $neg13StaleExe -Content "STALE_PREEXISTING_EXE"

    $fakeFailIscc = Join-Path $testRoot "fail-iscc.cmd"
    Write-Utf8NoBom -Path $fakeFailIscc -Content "@echo off`r`necho Simulated ISCC failure`r`nexit /b 2`r`n"
    $rFail = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg13Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos02ManifestSha,
        "-CompileExistingStageOnly", "-BuildInstaller", "-InnoSetupCompilerPath", $fakeFailIscc
    )
    $staleSurvivedFail = Test-Path -LiteralPath $neg13StaleExe

    Write-Utf8NoBom -Path $neg13StaleExe -Content "STALE_PREEXISTING_EXE"
    $fakeNoopIscc = Join-Path $testRoot "noop-iscc.cmd"
    Write-Utf8NoBom -Path $fakeNoopIscc -Content "@echo off`r`necho Simulated ISCC wrote nothing`r`nexit /b 0`r`n"
    $rNoop = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg13Out,
        "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos02ManifestSha,
        "-CompileExistingStageOnly", "-BuildInstaller", "-InnoSetupCompilerPath", $fakeNoopIscc
    )
    $staleSurvivedNoop = Test-Path -LiteralPath $neg13StaleExe

    $ok = ($rFail.ExitCode -ne 0) -and ($rFail.Combined -match 'code=ISCC_COMPILE_FAILED') -and (-not $staleSurvivedFail) -and
          ($rNoop.ExitCode -ne 0) -and ($rNoop.Combined -match 'code=SETUP_EXE_NOT_PRODUCED') -and (-not $staleSurvivedNoop)
    Record-TestResult -Id "NEG-13_FAILED_OR_NOOP_ISCC_PURGES_STALE_EXE" -Passed $ok -ObservedCode "ISCC_COMPILE_FAILED+SETUP_EXE_NOT_PRODUCED" -Notes "staleAfterFail=$staleSurvivedFail, staleAfterNoop=$staleSurvivedNoop"

    # -----------------------------------------------------------------------
    # NEG-15: Path traversal in stage manifest (even with updated .sha256)
    #         must fail closed before ISCC
    # -----------------------------------------------------------------------
    $neg15Out = Join-Path $outBase "neg15"
    & robocopy (Join-Path $pos02Out "staging") (Join-Path $neg15Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos02 staging for NEG-15" }
    New-Item -ItemType Directory -Force -Path (Join-Path $neg15Out "setup") | Out-Null
    $neg15Manifest = Join-Path $neg15Out "staging\Salryn\salryn-installer-provenance-manifest.json"
    $neg15ManifestSha = "$neg15Manifest.sha256"
    $mObj = Get-Content -LiteralPath $neg15Manifest -Raw | ConvertFrom-Json
    $mObj.files[0].relativePath = "../../escape.dll"
    Write-Utf8NoBom -Path $neg15Manifest -Content ($mObj | ConvertTo-Json -Depth 10)
    $newMSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $neg15Manifest).Hash.ToLowerInvariant()
    Write-Utf8NoBom -Path $neg15ManifestSha -Content "$newMSha  salryn-installer-provenance-manifest.json`n"
    $stageArgs15 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg15Out, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $newMSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs15 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r15 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs15
    $neg15Exe = Test-Path -LiteralPath (Join-Path $neg15Out "setup\Salryn-POS-BetaRC1-Setup.exe")
    $ok15 = ($r15.ExitCode -ne 0) -and ($r15.Combined -match 'code=PATH_TRAVERSAL_FORBIDDEN') -and (-not $neg15Exe)
    Record-TestResult -Id "NEG-15_PATH_TRAVERSAL_IN_STAGE_MANIFEST_REJECTED" -Passed $ok15 -ObservedCode "PATH_TRAVERSAL_FORBIDDEN" -Notes "exit=$($r15.ExitCode), setupExists=$neg15Exe"

    # -----------------------------------------------------------------------
    # NEG-16 (F1): -CompileExistingStageOnly without external pinned manifest
    #              SHA-256 fails closed (eliminates stage self-certification)
    # -----------------------------------------------------------------------
    $neg16Out = Join-Path $outBase "neg16"
    & robocopy (Join-Path $pos02Out "staging") (Join-Path $neg16Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos02 staging for NEG-16" }
    $neg16SetupDir = Join-Path $neg16Out "setup"
    New-Item -ItemType Directory -Force -Path $neg16SetupDir | Out-Null
    $neg16StaleExe = Join-Path $neg16SetupDir "Salryn-POS-BetaRC1-Setup.exe"
    Write-Utf8NoBom -Path $neg16StaleExe -Content "STALE_PRE_F1_EXE"
    $stageArgs16 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg16Out, "-ExpectedSourceSha", $headSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs16 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r16 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs16
    $neg16StaleLeft = Test-Path -LiteralPath $neg16StaleExe
    $ok16 = ($r16.ExitCode -ne 0) -and ($r16.Combined -match 'code=UNATTESTED_EXISTING_STAGE_FORBIDDEN') -and (-not $neg16StaleLeft)
    Record-TestResult -Id "NEG-16_F1_UNATTESTED_COMPILE_EXISTING_STAGE_REJECTED" -Passed $ok16 -ObservedCode "UNATTESTED_EXISTING_STAGE_FORBIDDEN" -Notes "exit=$($r16.ExitCode), staleLeft=$neg16StaleLeft"

    # -----------------------------------------------------------------------
    # NEG-17 (F1): Modified staged asset + consistently recomputed manifest ID,
    #              dist digest, payload inventory, and .sha256 rejected against
    #              external pinned authentic manifest digest
    # -----------------------------------------------------------------------
    $neg17Out = Join-Path $outBase "neg17"
    $forgedSha17 = New-ForgedConsistentlyHashedStage -SourceStagingDir (Join-Path $pos02Out "staging") -DestStagingDir (Join-Path $neg17Out "staging") -HeadSha $headSha -Version $Version
    New-Item -ItemType Directory -Force -Path (Join-Path $neg17Out "setup") | Out-Null
    $stageArgs17 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg17Out, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos02ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs17 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r17 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs17
    $neg17Exe = Test-Path -LiteralPath (Join-Path $neg17Out "setup\Salryn-POS-BetaRC1-Setup.exe")
    $ok17 = ($r17.ExitCode -ne 0) -and ($forgedSha17 -ne $pos02ManifestSha) -and ($r17.Combined -match 'code=EXPECTED_STAGE_MANIFEST_SHA_MISMATCH') -and (-not $neg17Exe)
    Record-TestResult -Id "NEG-17_F1_REHASHED_STAGE_REJECTED_BY_PINNED_MANIFEST_SHA" -Passed $ok17 -ObservedCode "EXPECTED_STAGE_MANIFEST_SHA_MISMATCH" -Notes "exit=$($r17.ExitCode), forgedShaDiffers=$($forgedSha17 -ne $pos02ManifestSha), setupExists=$neg17Exe"

    # -----------------------------------------------------------------------
    # NEG-18 (F1): Even if forged manifest SHA is supplied externally, staged dist
    #              parity check and source branding-asset check catch modified payloads
    # -----------------------------------------------------------------------
    $neg18OutA = Join-Path $outBase "neg18a"
    $forgedSha18A = New-ForgedConsistentlyHashedStage -SourceStagingDir (Join-Path $pos02Out "staging") -DestStagingDir (Join-Path $neg18OutA "staging") -HeadSha $headSha -Version $Version
    New-Item -ItemType Directory -Force -Path (Join-Path $neg18OutA "setup") | Out-Null
    $stageArgs18A = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg18OutA, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $forgedSha18A, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs18A += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r18A = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs18A
    $neg18AExe = Test-Path -LiteralPath (Join-Path $neg18OutA "setup\Salryn-POS-BetaRC1-Setup.exe")

    $neg18OutB = Join-Path $outBase "neg18b"
    $forgedBytes = [System.Text.Encoding]::UTF8.GetBytes("FORGED_BRANDING_PNG")
    $forgedSha18B = New-ForgedConsistentlyHashedStage -SourceStagingDir (Join-Path $pos02Out "staging") -DestStagingDir (Join-Path $neg18OutB "staging") -HeadSha $headSha -Version $Version -AssetRelativePath "assets\branding\salryn-icon.png" -ReplacementBytes $forgedBytes
    New-Item -ItemType Directory -Force -Path (Join-Path $neg18OutB "setup") | Out-Null
    $stageArgs18B = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg18OutB, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $forgedSha18B, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs18B += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r18B = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs18B
    $neg18BExe = Test-Path -LiteralPath (Join-Path $neg18OutB "setup\Salryn-POS-BetaRC1-Setup.exe")

    $ok18 = ($r18A.ExitCode -ne 0) -and ($r18A.Combined -match 'code=STAGE_WEB_DIST_PROVENANCE_MISMATCH') -and (-not $neg18AExe) -and
            ($r18B.ExitCode -ne 0) -and ($r18B.Combined -match 'code=STAGE_BRANDING_ASSET_MISMATCH') -and (-not $neg18BExe)
    Record-TestResult -Id "NEG-18_F1_REHASHED_STAGE_REJECTED_BY_SOURCE_PARITY" -Passed $ok18 -ObservedCode "STAGE_WEB_DIST+BRANDING_PARITY" -Notes "distExit=$($r18A.ExitCode), brandingExit=$($r18B.ExitCode)"

    # -----------------------------------------------------------------------
    # POS-03 (F1 positive): Authentic -CompileExistingStageOnly WITH pinned
    #                       -ExpectedSourceSha & -ExpectedStageManifestSha256 passes
    # -----------------------------------------------------------------------
    $pos03Out = Join-Path $outBase "pos03"
    & robocopy (Join-Path $pos02Out "staging") (Join-Path $pos03Out "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos02 staging for POS-03" }
    $stageArgsPos03 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $pos03Out, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos02ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgsPos03 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $rPos03 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgsPos03
    $pos03Exe = Join-Path $pos03Out "setup\Salryn-POS-BetaRC1-Setup.exe"
    $pos03Prov = "$pos03Exe.provenance.json"
    $okPos03 = ($rPos03.ExitCode -eq 0) -and (Test-Path -LiteralPath $pos03Exe) -and (Test-Path -LiteralPath $pos03Prov)
    if ($okPos03) {
        $p03Json = Get-Content -LiteralPath $pos03Prov -Raw | ConvertFrom-Json
        $okPos03 = ($p03Json.stageManifestSha256 -eq $pos02ManifestSha) -and ($p03Json.sourceCommitSha -eq $headSha)
    }
    Record-TestResult -Id "POS-03_F1_PINNED_DIGEST_COMPILE_EXISTING_STAGE_PASSES" -Passed $okPos03 -ObservedCode $(if ($okPos03) { "PINNED_STAGE_COMPILED" } else { "FAILED" }) -Notes "exit=$($rPos03.ExitCode)"

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
            "-OutputDir", $junction19, "-AdditionalProtectedRoots", $standin19, "-SkipWebBuild", "-BuildInstaller"
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
            "-OutputDir", $missingLeafInAnc, "-AdditionalProtectedRoots", $standin20, "-SkipWebBuild", "-BuildInstaller"
        )
        $r20B = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-OutputDir", (Join-Path $repoJunction20 "nested-out"), "-SkipWebBuild"
        )
        $directRepoUnsafe = Join-Path $dispWorktree "src\forbidden-out"
        $r20C = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-OutputDir", $directRepoUnsafe, "-SkipWebBuild"
        )
        $r20D = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-OutputDir", $childSubRoot20, "-AdditionalProtectedRoots", $standin20, "-SkipWebBuild", "-BuildInstaller"
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
    # NEG-22 (F3): Existing-stage validation and -SkipWebBuild validation
    #              failures purge pre-seeded setup artifacts
    # -----------------------------------------------------------------------
    $neg22AOut = Join-Path $outBase "neg22a"
    & robocopy (Join-Path $pos02Out "staging") (Join-Path $neg22AOut "staging") /E /NFL /NDL /NJH /NJS /NC /NS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed copying pos02 staging for NEG-22A" }
    Add-Content -LiteralPath (Join-Path $neg22AOut "staging\Salryn\dist\index.html") -Value "<!-- tampered-for-f3 -->"
    New-Item -ItemType Directory -Force -Path (Join-Path $neg22AOut "setup") | Out-Null
    $neg22AExe = Join-Path $neg22AOut "setup\Salryn-POS-BetaRC1-Setup.exe"
    $neg22ASha = "$neg22AExe.sha256.txt"
    $neg22AProv = "$neg22AExe.provenance.json"
    Write-Utf8NoBom -Path $neg22AExe -Content "STALE_STAGE_VALIDATION_EXE"
    Write-Utf8NoBom -Path $neg22ASha -Content "SHA256=deadbeef`r`n"
    Write-Utf8NoBom -Path $neg22AProv -Content '{"stale":true}'
    $stageArgs22A = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg22AOut, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos02ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs22A += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r22A = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs22A
    $stale22A = (Test-Path -LiteralPath $neg22AExe) -or (Test-Path -LiteralPath $neg22ASha) -or (Test-Path -LiteralPath $neg22AProv)

    $neg22BOut = Join-Path $outBase "neg22b"
    New-Item -ItemType Directory -Force -Path (Join-Path $neg22BOut "setup") | Out-Null
    $neg22BExe = Join-Path $neg22BOut "setup\Salryn-POS-BetaRC1-Setup.exe"
    Write-Utf8NoBom -Path $neg22BExe -Content "STALE_MISSING_STAGE_EXE"
    $stageArgs22B = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg22BOut, "-ExpectedSourceSha", $headSha, "-ExpectedStageManifestSha256", $pos02ManifestSha, "-CompileExistingStageOnly", "-BuildInstaller")
    $r22B = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs22B
    $stale22B = Test-Path -LiteralPath $neg22BExe

    $neg22COut = Join-Path $outBase "neg22c"
    New-Item -ItemType Directory -Force -Path (Join-Path $neg22COut "setup") | Out-Null
    $neg22CExe = Join-Path $neg22COut "setup\Salryn-POS-BetaRC1-Setup.exe"
    Write-Utf8NoBom -Path $neg22CExe -Content "STALE_SKIP_WEB_FAIL_EXE"
    $stamp22CPath = Join-Path $dispWebDist ".salryn-web-build-provenance.json"
    $webStampBak = if (Test-Path -LiteralPath $stamp22CPath) { Get-Content -LiteralPath $stamp22CPath -Raw } else { $null }
    try {
        if (Test-Path -LiteralPath $stamp22CPath) { Remove-Item -Force -LiteralPath $stamp22CPath }
        $r22C = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
            "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg22COut,
            "-ExpectedSourceSha", $headSha, "-SkipWebBuild", "-BuildInstaller"
        )
        $stale22C = Test-Path -LiteralPath $neg22CExe
    } finally {
        if ($null -ne $webStampBak) {
            Write-Utf8NoBom -Path $stamp22CPath -Content $webStampBak
        }
    }

    $ok22 = ($r22A.ExitCode -ne 0) -and ($r22A.Combined -match 'code=STAGE_MANIFEST_INVENTORY_MISMATCH') -and (-not $stale22A) -and
            ($r22B.ExitCode -ne 0) -and ($r22B.Combined -match 'code=MISSING_STAGE_DIRECTORY') -and (-not $stale22B) -and
            ($r22C.ExitCode -ne 0) -and ($r22C.Combined -match 'code=STALE_OR_UNVERIFIED_WEB_DIST') -and (-not $stale22C)
    Record-TestResult -Id "NEG-22_F3_VALIDATION_FAILURES_PURGE_PRESEEDED_SETUP_EXE" -Passed $ok22 -ObservedCode "STAGE+SKIPWEB_PURGE" -Notes "staleA=$stale22A, staleB=$stale22B, staleC=$stale22C"

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
