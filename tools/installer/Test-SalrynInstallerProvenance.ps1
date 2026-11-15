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
    if ($pos02Ok) {
        $m1 = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
        $m2 = Get-Content -LiteralPath $m2Path -Raw | ConvertFrom-Json
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
    $stageArgs11 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg11Out, "-CompileExistingStageOnly", "-BuildInstaller")
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
    $stageArgs12 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg12Out, "-CompileExistingStageOnly", "-BuildInstaller")
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
        "-CompileExistingStageOnly", "-BuildInstaller", "-InnoSetupCompilerPath", $fakeFailIscc
    )
    $staleSurvivedFail = Test-Path -LiteralPath $neg13StaleExe

    Write-Utf8NoBom -Path $neg13StaleExe -Content "STALE_PREEXISTING_EXE"
    $fakeNoopIscc = Join-Path $testRoot "noop-iscc.cmd"
    Write-Utf8NoBom -Path $fakeNoopIscc -Content "@echo off`r`necho Simulated ISCC wrote nothing`r`nexit /b 0`r`n"
    $rNoop = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments @(
        "-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg13Out,
        "-CompileExistingStageOnly", "-BuildInstaller", "-InnoSetupCompilerPath", $fakeNoopIscc
    )
    $staleSurvivedNoop = Test-Path -LiteralPath $neg13StaleExe

    $ok = ($rFail.ExitCode -ne 0) -and ($rFail.Combined -match 'code=ISCC_COMPILE_FAILED') -and (-not $staleSurvivedFail) -and
          ($rNoop.ExitCode -ne 0) -and ($rNoop.Combined -match 'code=SETUP_EXE_NOT_PRODUCED') -and (-not $staleSurvivedNoop)
    Record-TestResult -Id "NEG-13_FAILED_OR_NOOP_ISCC_PURGES_STALE_EXE" -Passed $ok -ObservedCode "ISCC_COMPILE_FAILED+SETUP_EXE_NOT_PRODUCED" -Notes "staleAfterFail=$staleSurvivedFail, staleAfterNoop=$staleSurvivedNoop"

    # -----------------------------------------------------------------------
    # NEG-14: Failed dotnet publish (broken C# on temp detached commit) must
    #         fail closed and purge pre-existing stale setup EXE
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
    $stageArgs15 = @("-Version", $Version, "-Configuration", $Configuration, "-OutputDir", $neg15Out, "-CompileExistingStageOnly", "-BuildInstaller")
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompilerPath)) { $stageArgs15 += @("-InnoSetupCompilerPath", $InnoSetupCompilerPath) }
    $r15 = Invoke-BuildInstallerChild -ScriptPath $dispBuildScript -WorkingDir $dispWorktree -Arguments $stageArgs15
    $neg15Exe = Test-Path -LiteralPath (Join-Path $neg15Out "setup\Salryn-POS-BetaRC1-Setup.exe")
    $ok15 = ($r15.ExitCode -ne 0) -and ($r15.Combined -match 'code=PATH_TRAVERSAL_FORBIDDEN') -and (-not $neg15Exe)
    Record-TestResult -Id "NEG-15_PATH_TRAVERSAL_IN_STAGE_MANIFEST_REJECTED" -Passed $ok15 -ObservedCode "PATH_TRAVERSAL_FORBIDDEN" -Notes "exit=$($r15.ExitCode), setupExists=$neg15Exe"

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
