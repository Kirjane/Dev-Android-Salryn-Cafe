<#
.SYNOPSIS
    Canonical Salryn Cafe candidate verification runner (Issue #9 I7 / Directive #4).
.DESCRIPTION
    Eliminates zero-test false-green passes by:
      1. Never running legacy non-xUnit tests/Salryn.Tests as a pass gate.
      2. Enforcing Executed > 0 and Passed > 0 on every discovered xUnit TRX and Vitest suite.
      3. Emitting machine-readable JSON + Markdown summaries and SHA-256 artifact manifests for I8 (#15).
#>
param(
    [string]$Configuration = "Release",
    [switch]$EnableWindowsTargeting,
    [switch]$RequireXunitSuites,
    [switch]$RequireAndroid,
    [switch]$SkipWeb,
    [switch]$SkipAndroid,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\verification"
}
$trxDir = Join-Path $OutputDir "trx"
New-Item -ItemType Directory -Force -Path $OutputDir, $trxDir | Out-Null

# Load local Forge toolchain environment when available on Windows developer hosts
$forgeEnv = if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA "SalrynForgeTools\forge-env.ps1" } else { $null }
if ($forgeEnv -and (Test-Path $forgeEnv)) {
    . $forgeEnv
}

$headSha = (git -C $repoRoot rev-parse HEAD 2>$null)
if (-not $headSha) { $headSha = "unknown" }
$branchName = (git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null)
if (-not $branchName) { $branchName = "unknown" }

$results = New-Object System.Collections.Generic.List[object]
$overallFailed = $false

function Add-GateResult {
    param(
        [string]$Gate,
        [string]$Command,
        [string]$Status,
        [int]$ExitCode,
        [int]$Executed = 0,
        [int]$Passed = 0,
        [int]$Failed = 0,
        [int]$Skipped = 0,
        [double]$DurationSeconds = 0,
        [string]$Notes = ""
    )
    $entry = [ordered]@{
        gate            = $Gate
        command         = $Command
        status          = $Status
        exitCode        = $ExitCode
        executed        = $Executed
        passed          = $Passed
        failed          = $Failed
        skipped         = $Skipped
        durationSeconds = [math]::Round($DurationSeconds, 2)
        notes           = $Notes
    }
    $script:results.Add([pscustomobject]$entry) | Out-Null
    Write-Host ("[{0}] {1} (exit={2}, executed={3}, passed={4}, failed={5}) {6}" -f $Status, $Gate, $ExitCode, $Executed, $Passed, $Failed, $Notes)
    if ($Status -eq "FAIL") {
        $script:overallFailed = $true
    }
}

function Parse-TrxCounts {
    param([string]$TrxPath)
    if (-not (Test-Path $TrxPath)) {
        return @{ Found = $false; Total = 0; Executed = 0; Passed = 0; Failed = 0; Skipped = 0 }
    }
    [xml]$xml = Get-Content -Path $TrxPath -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
    $counters = $xml.SelectSingleNode("//t:ResultSummary/t:Counters", $ns)
    if (-not $counters) {
        return @{ Found = $false; Total = 0; Executed = 0; Passed = 0; Failed = 0; Skipped = 0 }
    }
    $total = [int]$counters.GetAttribute("total")
    $executed = [int]$counters.GetAttribute("executed")
    $passed = [int]$counters.GetAttribute("passed")
    $failed = [int]$counters.GetAttribute("failed")
    $notExecuted = [int]$counters.GetAttribute("notExecuted")
    return @{ Found = $true; Total = $total; Executed = $executed; Passed = $passed; Failed = $failed; Skipped = $notExecuted }
}

# 1. Solution Build
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$buildArgs = @("build", (Join-Path $repoRoot "Salryn.sln"), "-c", $Configuration, "--nologo")
if ($EnableWindowsTargeting) {
    $buildArgs += "-p:EnableWindowsTargeting=true"
}
$buildCmd = "dotnet " + ($buildArgs -join " ")
& dotnet @buildArgs
$buildExit = $LASTEXITCODE
$sw.Stop()
Add-GateResult -Gate "solution-build" -Command $buildCmd -Status ($(if ($buildExit -eq 0) { "PASS" } else { "FAIL" })) -ExitCode $buildExit -DurationSeconds $sw.Elapsed.TotalSeconds

# 2. CoreSmoke Executable Check (replaces legacy 0-test dotnet test tests/Salryn.Tests)
$coreSmokeProj = Join-Path $repoRoot "tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj"
if (Test-Path $coreSmokeProj) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $smokeArgs = @("run", "--project", $coreSmokeProj, "-c", $Configuration, "--no-build")
    if ($EnableWindowsTargeting) { $smokeArgs += "-p:EnableWindowsTargeting=true" }
    Push-Location $repoRoot
    try {
        $smokeOut = (& dotnet @smokeArgs 2>&1) | Out-String
        $smokeExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    $sw.Stop()
    $hasPassMarker = $smokeOut -match "SALRYN Core smoke checks passed\."
    $smokeStatus = if ($smokeExit -eq 0 -and $hasPassMarker) { "PASS" } else { "FAIL" }
    $smokeNotes = if ($hasPassMarker) { "Observed pass marker: SALRYN Core smoke checks passed." } else { "Missing CoreSmoke pass marker." }
    Add-GateResult -Gate "core-smoke" -Command ("dotnet " + ($smokeArgs -join " ")) -Status $smokeStatus -ExitCode $smokeExit -Executed $(if ($hasPassMarker) { 1 } else { 0 }) -Passed $(if ($smokeStatus -eq "PASS") { 1 } else { 0 }) -Failed $(if ($smokeStatus -eq "FAIL") { 1 } else { 0 }) -DurationSeconds $sw.Elapsed.TotalSeconds -Notes $smokeNotes
} else {
    Add-GateResult -Gate "core-smoke" -Command "n/a" -Status "NOT RUN" -ExitCode 0 -Notes "CoreSmoke project not present."
}

# 3. Real xUnit Test Suites (auto-discover tests/Salryn.Cafe*Tests/*.csproj; hard Executed > 0 gate)
$xunitProjects = Get-ChildItem -Path (Join-Path $repoRoot "tests") -Directory -Filter "Salryn.Cafe*Tests" -ErrorAction SilentlyContinue |
    ForEach-Object { Get-ChildItem -Path $_.FullName -Filter "*.csproj" -File } |
    Sort-Object FullName

if (-not $xunitProjects -or $xunitProjects.Count -eq 0) {
    $status = if ($RequireXunitSuites) { "FAIL" } else { "NOT RUN" }
    Add-GateResult -Gate "xunit-suites" -Command "discover tests/Salryn.Cafe*Tests/*.csproj" -Status $status -ExitCode $(if ($status -eq "FAIL") { 1 } else { 0 }) -Notes "No Salryn.Cafe*Tests xUnit projects discovered on this commit (legacy Salryn.Tests intentionally excluded from xUnit pass gate)."
} else {
    foreach ($proj in $xunitProjects) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($proj.Name)
        $trxName = "$name.trx"
        $trxPath = Join-Path $trxDir $trxName
        if (Test-Path $trxPath) { Remove-Item -Force $trxPath }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $testArgs = @("test", $proj.FullName, "-c", $Configuration, "--nologo", "--logger", "trx;LogFileName=$trxName", "--results-directory", $trxDir)
        if ($EnableWindowsTargeting) { $testArgs += "-p:EnableWindowsTargeting=true" }
        & dotnet @testArgs
        $testExit = $LASTEXITCODE
        $sw.Stop()

        $counts = Parse-TrxCounts -TrxPath $trxPath
        $passedGate = ($testExit -eq 0) -and $counts.Found -and ($counts.Executed -gt 0) -and ($counts.Passed -gt 0) -and ($counts.Failed -eq 0)
        $notes = if (-not $counts.Found) {
            "TRX file missing or unreadable ($trxPath)."
        } elseif ($counts.Executed -le 0 -or $counts.Passed -le 0) {
            "ZERO-TEST FALSE-GREEN BLOCKED: executed=$($counts.Executed), passed=$($counts.Passed)."
        } else {
            "TRX verified: total=$($counts.Total), executed=$($counts.Executed), passed=$($counts.Passed), failed=$($counts.Failed), skipped=$($counts.Skipped)."
        }

        Add-GateResult -Gate "xunit:$name" -Command ("dotnet " + ($testArgs -join " ")) -Status ($(if ($passedGate) { "PASS" } else { "FAIL" })) -ExitCode $testExit -Executed $counts.Executed -Passed $counts.Passed -Failed $counts.Failed -Skipped $counts.Skipped -DurationSeconds $sw.Elapsed.TotalSeconds -Notes $notes
    }
}

# 4. Web / UI Suites (packages/salryn-cafe-ui and apps/cafe-web)
if (-not $SkipWeb) {
    $webDirs = @(
        @{ Gate = "web:salryn-cafe-ui"; RelPath = "packages\salryn-cafe-ui" },
        @{ Gate = "web:cafe-web"; RelPath = "apps\cafe-web" }
    )
    foreach ($w in $webDirs) {
        $dir = Join-Path $repoRoot $w.RelPath
        $pkgJson = Join-Path $dir "package.json"
        if (-not (Test-Path $pkgJson)) {
            Add-GateResult -Gate $w.Gate -Command "n/a" -Status "NOT RUN" -ExitCode 0 -Notes "$($w.RelPath) not present on this commit."
            continue
        }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $lockFile = Join-Path $dir "package-lock.json"
        if (-not (Test-Path (Join-Path $dir "node_modules"))) {
            if (Test-Path $lockFile) { & npm --prefix $dir ci } else { & npm --prefix $dir install }
            if ($LASTEXITCODE -ne 0) {
                $sw.Stop()
                Add-GateResult -Gate $w.Gate -Command "npm ci/install" -Status "FAIL" -ExitCode $LASTEXITCODE -DurationSeconds $sw.Elapsed.TotalSeconds -Notes "Dependency install failed."
                continue
            }
        }

        $testOut = (& npm --prefix $dir test 2>&1) | Out-String
        $testExit = $LASTEXITCODE
        $passedCount = 0
        if ($testOut -match 'Tests\s+(\d+)\s+passed') {
            $passedCount = [int]$Matches[1]
        }

        $buildExit = 0
        if ($testExit -eq 0) {
            & npm --prefix $dir run build | Out-Null
            $buildExit = $LASTEXITCODE
        }
        $sw.Stop()

        $ok = ($testExit -eq 0) -and ($buildExit -eq 0) -and ($passedCount -gt 0)
        $notes = if ($passedCount -le 0) { "ZERO-TEST FALSE-GREEN BLOCKED: Vitest passed count was 0." } else { "Vitest passed=$passedCount; buildExit=$buildExit." }
        Add-GateResult -Gate $w.Gate -Command "npm --prefix $($w.RelPath) test && npm run build" -Status ($(if ($ok) { "PASS" } else { "FAIL" })) -ExitCode $(if ($testExit -ne 0) { $testExit } else { $buildExit }) -Executed $passedCount -Passed $passedCount -Failed $(if ($ok) { 0 } else { 1 }) -DurationSeconds $sw.Elapsed.TotalSeconds -Notes $notes
    }
}

# 5. Android Forbidden-Dependency & Compile Gate
if (-not $SkipAndroid) {
    $androidProj = Join-Path $repoRoot "src\Salryn.App.Android\Salryn.App.Android.csproj"
    if (-not (Test-Path $androidProj)) {
        $status = if ($RequireAndroid) { "FAIL" } else { "NOT RUN" }
        Add-GateResult -Gate "android-compile" -Command "n/a" -Status $status -ExitCode $(if ($status -eq "FAIL") { 1 } else { 0 }) -Notes "src/Salryn.App.Android not present on this commit."
    } else {
        $projContent = Get-Content -Path $androidProj -Raw
        $forbidden = @("Salryn.App.WinForms", "Salryn.Desktop.WebView", "Salryn.Api.Local") | Where-Object { $projContent -match [regex]::Escape($_) }
        if ($forbidden.Count -gt 0) {
            Add-GateResult -Gate "android-boundary" -Command "inspect Salryn.App.Android.csproj" -Status "FAIL" -ExitCode 1 -Notes ("Forbidden desktop/host references detected: " + ($forbidden -join ", "))
        } else {
            Add-GateResult -Gate "android-boundary" -Command "inspect Salryn.App.Android.csproj" -Status "PASS" -ExitCode 0 -Executed 1 -Passed 1 -Notes "Zero forbidden desktop/host references."
        }

        $workloads = (& dotnet workload list 2>&1) | Out-String
        if ($workloads -match '\bandroid\b' -and $env:ANDROID_HOME -and (Test-Path $env:ANDROID_HOME)) {
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            & dotnet build $androidProj -c $Configuration --nologo
            $aExit = $LASTEXITCODE
            $sw.Stop()
            Add-GateResult -Gate "android-compile" -Command "dotnet build src/Salryn.App.Android/Salryn.App.Android.csproj -c $Configuration" -Status ($(if ($aExit -eq 0) { "PASS" } else { "FAIL" })) -ExitCode $aExit -Executed 1 -Passed $(if ($aExit -eq 0) { 1 } else { 0 }) -Failed $(if ($aExit -ne 0) { 1 } else { 0 }) -DurationSeconds $sw.Elapsed.TotalSeconds
        } else {
            $status = if ($RequireAndroid) { "FAIL" } else { "NOT RUN" }
            Add-GateResult -Gate "android-compile" -Command "dotnet build src/Salryn.App.Android" -Status $status -ExitCode $(if ($status -eq "FAIL") { 1 } else { 0 }) -Notes "Android workload or ANDROID_HOME not configured on host."
        }
    }
}

# 6. Artifact SHA-256 Manifest Helper (for Issue #15 I8)
$artifactsRoot = Join-Path $repoRoot "artifacts"
$artifactManifest = New-Object System.Collections.Generic.List[object]
if (Test-Path $artifactsRoot) {
    $deliverables = Get-ChildItem -Path $artifactsRoot -Recurse -File -Include "*.exe", "*.apk", "*.aab", "*.zip" -ErrorAction SilentlyContinue | Sort-Object FullName
    $shaLines = New-Object System.Collections.Generic.List[string]
    foreach ($f in $deliverables) {
        $hash = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash
        $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        $artifactManifest.Add([pscustomobject][ordered]@{
            path   = $rel
            bytes  = $f.Length
            sha256 = $hash
        }) | Out-Null
        $shaLines.Add("$hash  $rel") | Out-Null
    }
    if ($shaLines.Count -gt 0) {
        $shaLines | Set-Content -Path (Join-Path $OutputDir "sha256-manifest.txt") -Encoding UTF8
    }
}

# 7. Emit Structured JSON + Markdown Verification Reports
$summary = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    repository     = "Kirjane/Dev-Salryn-Cafe"
    branch         = $branchName
    commitSha      = $headSha
    configuration  = $Configuration
    os             = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    dotnetVersion  = ((& dotnet --version 2>$null) | Select-Object -First 1)
    overallStatus  = if ($overallFailed) { "FAIL" } else { "PASS" }
    gates          = $results
    artifacts      = $artifactManifest
}

$jsonPath = Join-Path $OutputDir "verification-summary.json"
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add("# Salryn Cafe Verification Summary")
$mdLines.Add("")
$mdLines.Add("- **Commit SHA:** ``$headSha`` (``$branchName``)")
$mdLines.Add("- **Configuration:** ``$Configuration`` | **OS:** $($summary.os) | **.NET:** ``$($summary.dotnetVersion)``")
$mdLines.Add("- **Overall Status:** **$($summary.overallStatus)**")
$mdLines.Add("")
$mdLines.Add("| Gate | Status | Exit | Executed | Passed | Failed | Duration (s) | Notes |")
$mdLines.Add("|---|---:|---:|---:|---:|---:|---:|---|")
foreach ($r in $results) {
    $mdLines.Add("| $($r.gate) | **$($r.status)** | $($r.exitCode) | $($r.executed) | $($r.passed) | $($r.failed) | $($r.durationSeconds) | $($r.notes) |")
}
$mdPath = Join-Path $OutputDir "verification-summary.md"
$mdLines | Set-Content -Path $mdPath -Encoding UTF8

Write-Host "`nVerification summary written to: $jsonPath"
if ($overallFailed) {
    Write-Error "One or more verification gates FAILED. See $mdPath."
    exit 1
}
exit 0
