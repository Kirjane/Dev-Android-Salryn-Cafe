<#
.SYNOPSIS
    Fail-closed, scoped Salryn Cafe verification. A mode PASS is not RC acceptance.
.DESCRIPTION
    Windows runs desktop build/smoke plus host suites; Portable runs only portable
    suites and web; Android requires mobile tests and a newly produced test-signed APK.
    Required suite inventories are deliberate: absent components fail partial branches.
    Raw command logs, TRX/JSON reports, source identity and output hashes are retained.
#>
param(
    [ValidateSet('Auto', 'Windows', 'Portable', 'Android')][string]$Mode = 'Auto',
    [string]$Configuration = 'Release',
    [ValidateSet('Auto', 'ReleaseUnlicensed', 'DebugSynthetic', 'ReleaseEntitled')][string]$CoreSmokeProfile = 'Auto',
    [string]$OutputDir = '',
    # Compatibility flags never turn missing required gates into successful checks.
    [switch]$EnableWindowsTargeting,
    [switch]$RequireXunitSuites,
    [switch]$RequireAndroid,
    [switch]$SkipWeb,
    [switch]$SkipAndroid
)
$ErrorActionPreference = 'Stop'

function Resolve-CoreSmokeVerificationProfile {
    param(
        [string]$Configuration,
        [string]$RequestedProfile = 'Auto',
        [string]$LicenseFixturePath = ''
    )
    $isDebug = $Configuration -ieq 'Debug'
    $resolved = if ($RequestedProfile -eq 'Auto') {
        if ($isDebug) { 'DebugSynthetic' }
        elseif (-not [string]::IsNullOrWhiteSpace($LicenseFixturePath)) { 'ReleaseEntitled' }
        else { 'ReleaseUnlicensed' }
    } else { $RequestedProfile }

    if ($resolved -eq 'DebugSynthetic' -and -not $isDebug) {
        throw 'DebugSynthetic CoreSmoke profile is forbidden under Release configuration.'
    }
    if ($resolved -in @('ReleaseUnlicensed', 'ReleaseEntitled') -and $isDebug) {
        throw "$resolved CoreSmoke profile requires Release configuration."
    }
    if ($resolved -eq 'ReleaseEntitled') {
        if ([string]::IsNullOrWhiteSpace($LicenseFixturePath) -or -not (Test-Path -LiteralPath $LicenseFixturePath -PathType Leaf)) {
            throw 'BLOCKED_EXTERNAL (NOT RUN): ReleaseEntitled requires an authorized signed QA license fixture in SALRYN_QA_RELEASE_LICENSE_PATH.'
        }
    }
    return $resolved
}

function Get-PrHeadSha {
    param([string]$FallbackSha, [string]$EventPath = $env:GITHUB_EVENT_PATH)
    if (-not [string]::IsNullOrWhiteSpace($EventPath) -and (Test-Path -LiteralPath $EventPath -PathType Leaf)) {
        try {
            $evt = Get-Content -LiteralPath $EventPath -Raw | ConvertFrom-Json
            if ($evt.pull_request -and $evt.pull_request.head -and $evt.pull_request.head.sha -match '^[0-9a-f]{40}$') {
                return $evt.pull_request.head.sha
            }
        } catch { }
    }
    return $FallbackSha
}

function Get-SourceIdentity {
    param([string]$Root)
    $head = (& git -C $Root rev-parse HEAD 2>&1) | Out-String
    if ($LASTEXITCODE -ne 0 -or $head.Trim() -notmatch '^[a-f0-9]{40}$') { throw 'Cannot identify exact Git HEAD.' }
    $tree = (& git -C $Root rev-parse 'HEAD^{tree}' 2>&1) | Out-String
    if ($LASTEXITCODE -ne 0 -or $tree.Trim() -notmatch '^[a-f0-9]{40}$') { throw 'Cannot identify exact Git tree.' }
    $status = (& git -C $Root status --porcelain=v1 --untracked-files=all 2>&1) | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Git status inspection failed.' }
    if (-not [string]::IsNullOrWhiteSpace($status)) { throw 'Verification requires a clean tracked/index/untracked source tree.' }
    return [pscustomobject]@{ head = $head.Trim(); tree = $tree.Trim(); dirty = $false }
}

function Assert-SourceUnchanged {
    param($Before, $After)
    if ($Before.head -cne $After.head -or $Before.tree -cne $After.tree -or $After.dirty) {
        throw 'Source identity changed during verification.'
    }
}

function Get-RequiredSuitePaths {
    param([string]$Mode)
    $portable = @('Salryn.Cafe.I2Tests', 'Salryn.Cafe.I3Tests', 'Salryn.Cafe.I4Tests', 'Salryn.Cafe.I6ATests')
    $names = switch ($Mode) {
        'Windows' { @('Salryn.Cafe.Tests') + $portable }
        'Portable' { $portable }
        'Android' { @('Salryn.Cafe.I6BTests') }
        default { throw 'Unknown explicit verification mode.' }
    }
    return @($names | ForEach-Object { "tests/$_/$_.csproj" })
}

function Read-TrxCounts {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing TRX: $Path" }
    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($xml.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $c = $xml.SelectSingleNode('//t:ResultSummary/t:Counters', $ns)
    if ($null -eq $c) { throw 'TRX counters missing.' }
    $counts = @{}
    foreach ($key in @('total','executed','passed','failed','notExecuted')) {
        $value = 0
        if (-not [int]::TryParse($c.GetAttribute($key), [ref]$value) -or $value -lt 0) { throw "Invalid TRX $key count." }
        $counts[$key] = $value
    }
    return $counts
}

function Assert-NonzeroTests {
    param([int]$ExitCode, [int]$Executed, [int]$Passed, [int]$Failed)
    if ($ExitCode -ne 0 -or $Executed -le 0 -or $Passed -le 0 -or $Failed -ne 0 -or $Passed -gt $Executed) {
        throw "Failed/zero-test gate: exit=$ExitCode executed=$Executed passed=$Passed failed=$Failed"
    }
}

function Get-ProducedApks {
    param([string]$OutputPath)
    if (-not (Test-Path -LiteralPath $OutputPath -PathType Container)) { throw 'Android publish output missing.' }
    $apks = @(Get-ChildItem -LiteralPath $OutputPath -Recurse -File -Filter '*.apk')
    if ($apks.Count -eq 0) { throw 'Android publish produced no APK in this run output.' }
    return $apks
}

function Get-OverallStatus {
    param([object[]]$Results)
    if ($Results.Count -eq 0) { return 'FAIL' }
    if (@($Results | Where-Object { $_.status -notin @('PASS','BUILD_OK','RUNTIME_OBSERVED','SOURCE_CHECKED') }).Count -gt 0) { return 'FAIL' }
    if (@($Results | Where-Object { $_.kind -eq 'TEST' -and $_.status -eq 'PASS' -and $_.executed -gt 0 }).Count -eq 0) { return 'FAIL' }
    return 'PASS'
}

function Assert-PortableProject {
    param([string]$Path, [System.Collections.Generic.HashSet[string]]$Seen)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $Seen.Add($full)) { return }
    [xml]$project = Get-Content -LiteralPath $full -Raw
    $raw = $project.OuterXml
    if ($raw -match 'net\d+\.\d+-windows|<UseWindowsForms>\s*true|<UseWPF>\s*true|Microsoft.WindowsDesktop.App') {
        throw "Windows-only project in portable execution graph: $full"
    }
    foreach ($ref in $project.SelectNodes('//ProjectReference')) {
        # MSBuild uses backslashes even when evaluated on Linux.
        $relative = $ref.GetAttribute('Include').Replace('\', [IO.Path]::DirectorySeparatorChar)
        Assert-PortableProject -Path (Join-Path (Split-Path -Parent $full) $relative) -Seen $Seen
    }
}

function Invoke-RecordedCommand {
    param([string]$Gate, [string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory = $script:repoRoot)
    $start = (Get-Date).ToUniversalTime()
    $log = Join-Path $script:runDir (([regex]::Replace($Gate, '[^a-zA-Z0-9._-]', '_')) + '.log')
    $exit = 1
    $output = ''
    Push-Location $WorkingDirectory
    try {
        $output = (& $Executable @Arguments 2>&1 | Out-String)
        $exit = $LASTEXITCODE
    } catch { $output += "`n$($_.Exception.Message)" } finally { Pop-Location }
    [IO.File]::WriteAllText($log, $output)
    $end = (Get-Date).ToUniversalTime()
    $record = [pscustomobject]@{
        gate = $Gate; command = $Executable; arguments = $Arguments; cwd = $WorkingDirectory
        startUtc = $start.ToString('O'); endUtc = $end.ToString('O'); exitCode = $exit
        log = $log; logSha256 = (Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash
        output = $output
    }
    $script:commands.Add($record)
    if ($exit -ne 0) { throw "Command failed ($exit): $Executable. See $log" }
    return $record
}

function Invoke-Gate {
    param([string]$GateName, [string]$Kind, [scriptblock]$Action)
    $result = [pscustomobject]@{ gate = $GateName; kind = $Kind; status = 'FAIL'; executed = 0; passed = 0; failed = 0; notes = '' }
    try {
        & $Action $result | Out-Null
        $result.status = switch ($Kind) { 'TEST' { 'PASS' } 'BUILD' { 'BUILD_OK' } 'RUNTIME' { 'RUNTIME_OBSERVED' } default { 'SOURCE_CHECKED' } }
        if ($Kind -eq 'TEST') { Assert-NonzeroTests 0 $result.executed $result.passed $result.failed }
    } catch { $result.status = 'FAIL'; $result.notes = $_.Exception.Message }
    $script:results.Add($result)
    Write-Host "[$($result.status)] $GateName $($result.notes)"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$runId = [guid]::NewGuid().ToString('N')
$initial = Get-SourceIdentity $repoRoot
if ($Mode -eq 'Auto') { $Mode = if ($IsWindows) { 'Windows' } else { 'Portable' } }
if ($Mode -eq 'Windows' -and -not $IsWindows) { throw 'Windows mode requires Windows runtime, not EnableWindowsTargeting.' }
if ($SkipWeb -and $Mode -ne 'Android') { throw 'Web gates are required for Windows/Portable modes.' }
if ($RequireAndroid -and $Mode -ne 'Android') { throw 'Use a separate -Mode Android invocation for required Android qualification.' }
if ($SkipAndroid -and $Mode -eq 'Android') { throw 'Cannot skip required Android gate.' }
if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = Join-Path $repoRoot 'artifacts/verification' }
$runDir = Join-Path $OutputDir $runId
New-Item -ItemType Directory -Path $runDir | Out-Null
$trxDir = Join-Path $runDir 'trx'
New-Item -ItemType Directory -Path $trxDir | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
$commands = [System.Collections.Generic.List[object]]::new()
$artifacts = [System.Collections.Generic.List[object]]::new()
$forgeEnv = if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'SalrynForgeTools/forge-env.ps1' } else { $null }
if ($forgeEnv -and (Test-Path -LiteralPath $forgeEnv)) { . $forgeEnv }

$script:resolvedCoreSmokeProfile = if ($Mode -eq 'Windows') { 'UNRESOLVED' } else { 'NOT_APPLICABLE' }
$script:releaseEntitledVerdict = if ($Mode -eq 'Windows') { 'NOT_EVALUATED' } else { 'NOT_APPLICABLE' }

Invoke-Gate 'toolchain-dotnet' 'SOURCE' { param($r) Invoke-RecordedCommand 'dotnet-version' 'dotnet' @('--info') }
if ($Mode -eq 'Windows') {
    Invoke-Gate 'solution-build' 'BUILD' { param($r) Invoke-RecordedCommand 'solution-build' 'dotnet' @('build', 'Salryn.sln', '-c', $Configuration, '--nologo') }
    Invoke-Gate 'core-smoke' 'RUNTIME' {
        param($r)
        $licenseFixturePath = $env:SALRYN_QA_RELEASE_LICENSE_PATH
        try {
            $script:resolvedCoreSmokeProfile = Resolve-CoreSmokeVerificationProfile -Configuration $Configuration -RequestedProfile $CoreSmokeProfile -LicenseFixturePath $licenseFixturePath
        } catch {
            if ($_.Exception.Message -match 'BLOCKED_EXTERNAL') {
                $script:resolvedCoreSmokeProfile = 'ReleaseEntitled'
                $script:releaseEntitledVerdict = 'BLOCKED_EXTERNAL (NOT RUN: missing authorized signed Release QA license fixture)'
            }
            throw
        }
        $smokeArgs = @('run','--project','tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj','-c',$Configuration,'--no-build','--','--profile','cafe','--verification-profile',$script:resolvedCoreSmokeProfile)
        if ($script:resolvedCoreSmokeProfile -eq 'ReleaseEntitled') {
            $smokeArgs += @('--license-fixture', $licenseFixturePath)
        }
        $record = Invoke-RecordedCommand 'core-smoke' 'dotnet' $smokeArgs
        if ($record.output -notmatch 'SALRYN Core smoke checks passed\.') { throw 'Missing CoreSmoke completion marker.' }
        if ($script:resolvedCoreSmokeProfile -eq 'ReleaseUnlicensed') {
            $script:releaseEntitledVerdict = 'BLOCKED_EXTERNAL (NOT RUN: no signed Release QA license fixture in SALRYN_QA_RELEASE_LICENSE_PATH)'
            $r.notes = 'ReleaseUnlicensed CoreSmoke passed (asserted 401 anonymous + 403 License.FeatureDenied on Release binaries with zero writes; ReleaseEntitled checkout remains BLOCKED_EXTERNAL / NOT RUN).'
        } elseif ($script:resolvedCoreSmokeProfile -eq 'ReleaseEntitled') {
            $script:releaseEntitledVerdict = 'PASS'
            $r.notes = 'ReleaseEntitled CoreSmoke passed against authorized signed QA license fixture.'
        } else {
            $script:releaseEntitledVerdict = 'NOT_APPLICABLE_DEBUG'
            $r.notes = 'DebugSynthetic CoreSmoke passed (synthetic Debug license mode; asserted 403 Auth.PermissionDenied RBAC and Cafe checkout boundaries; not Release entitlement proof).'
        }
    }
}

foreach ($relative in (Get-RequiredSuitePaths $Mode)) {
    $project = Join-Path $repoRoot $relative
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    Invoke-Gate "xunit:$name" 'TEST' {
        param($r)
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Required suite missing: $relative" }
        if ($Mode -ne 'Windows') { Assert-PortableProject $project ([System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)) }
        $record = Invoke-RecordedCommand "xunit-$name" 'dotnet' @('test',$project,'-c',$Configuration,'--nologo','--logger',"trx;LogFileName=$name.trx",'--results-directory',$trxDir)
        $c = Read-TrxCounts (Join-Path $trxDir "$name.trx")
        Assert-NonzeroTests $record.exitCode $c.executed $c.passed $c.failed
        $r.executed = $c.executed; $r.passed = $c.passed; $r.failed = $c.failed
        $r.notes = "TRX=$name.trx total=$($c.total) skipped=$($c.notExecuted); disposable synthetic fixtures required by suites."
    }
}

if ($Mode -ne 'Android') {
    foreach ($relative in @('packages/salryn-cafe-ui','apps/cafe-web')) {
        $dir = Join-Path $repoRoot $relative
        $name = $relative.Replace('/', '-')
        Invoke-Gate "web:$name" 'TEST' {
            param($r)
            if (-not (Test-Path -LiteralPath (Join-Path $dir 'package-lock.json'))) { throw "Required locked web project missing: $relative" }
            Invoke-RecordedCommand "$name-npm-version" 'npm' @('--version') $dir
            Invoke-RecordedCommand "$name-ci" 'npm' @('ci') $dir
            $jsonPath = Join-Path $runDir "$name-vitest.json"
            Invoke-RecordedCommand "$name-test" 'npm' @('test','--','--reporter=json',"--outputFile=$jsonPath") $dir
            $report = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
            Assert-NonzeroTests 0 $report.numTotalTests $report.numPassedTests $report.numFailedTests
            if (-not $report.success -or $report.numPendingTests -ne 0 -or $report.numPassedTests -ne $report.numTotalTests) { throw 'Vitest failure or unexecuted tests.' }
            $r.executed = $report.numTotalTests; $r.passed = $report.numPassedTests; $r.failed = $report.numFailedTests
            $r.notes = 'MOCKED_UI unless suite explicitly proves a live backend. Vitest JSON retained.'
        }
        Invoke-Gate "web-build:$name" 'BUILD' { param($r) Invoke-RecordedCommand "$name-build" 'npm' @('run','build') $dir }
    }
}

if ($Mode -eq 'Android') {
    $androidProject = Join-Path $repoRoot 'src/Salryn.App.Android/Salryn.App.Android.csproj'
    Invoke-Gate 'android-boundary' 'SOURCE' {
        param($r)
        if (-not (Test-Path -LiteralPath $androidProject)) { throw 'Required Android application missing.' }
        # Recurse the entire project graph, not only direct Android references.
        $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        Assert-PortableProject $androidProject $seen
        foreach ($path in $seen) {
            if ($path -match 'Salryn\.(Api\.Local|Desktop\.WebView|App\.WinForms)') { throw "Forbidden host dependency: $path" }
        }
    }
    Invoke-Gate 'android-apk' 'BUILD' {
        param($r)
        $apkDir = Join-Path $runDir 'android-package'
        # Unique empty output directory fences earlier build artifacts.
        Invoke-RecordedCommand 'android-publish' 'dotnet' @('publish',$androidProject,'-c',$Configuration,'-r','android-arm64','-p:AndroidPackageFormats=apk','-p:AndroidKeyStore=false','--output',$apkDir,'--nologo')
        $apks = @(Get-ProducedApks $apkDir)
        foreach ($apk in $apks) {
            $artifacts.Add([pscustomobject]@{ path = $apk.FullName; bytes = $apk.Length; sha256 = (Get-FileHash -LiteralPath $apk.FullName -Algorithm SHA256).Hash; runId = $runId; commitSha = $initial.head; signing = 'TEST_ONLY' })
        }
    }
}

Invoke-Gate 'source-identity-final' 'SOURCE' { param($r) Assert-SourceUnchanged $initial (Get-SourceIdentity $repoRoot) }
$status = Get-OverallStatus $results.ToArray()
$summary = [ordered]@{
    runId = $runId; generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    repository = 'Kirjane/Dev-Salryn-Cafe'; commitSha = $initial.head; prHeadSha = (Get-PrHeadSha $initial.head); treeSha = $initial.tree
    mode = $Mode; configuration = $Configuration; coreSmokeProfile = $script:resolvedCoreSmokeProfile; releaseEntitledVerdict = $script:releaseEntitledVerdict
    os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    overallStatus = $status; releaseReadiness = 'NOT EVALUATED: scoped checks are not full C1-C11/live E2E/installer/hardware acceptance'
    fixturePolicy = 'Synthetic/disposable only; source fixture identities are bound by commit/tree; no production data permitted'
    gates = $results.ToArray(); commands = @($commands | Select-Object * -ExcludeProperty output); artifacts = $artifacts.ToArray()
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runDir 'verification-summary.json') -Encoding utf8
Write-Host "Verification $status ($Mode) at $($initial.head): $runDir"
if ($status -ne 'PASS') { exit 1 }
exit 0
