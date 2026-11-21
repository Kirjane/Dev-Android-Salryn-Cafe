<#
.SYNOPSIS
    Fail-closed matched QA staging validator and manifest generator for Salryn Cafe.
.DESCRIPTION
    Validates clean Git source identity, release tier policy, CI provenance, package
    directory hygiene, embedded source SHA and profile consistency across Windows Setup,
    Android APK, and Central Server ZIP, persistent QA Android signing identity, and
    post-write manifest/checksum integrity. Never authorizes production or RC promotion.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceSha,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [ValidateSet('ReleaseEntitled', 'DebugSynthetic')][string]$Profile = 'ReleaseEntitled',
    [ValidateSet('', 'InternalDebugSynthetic', 'ControlledQA', 'ReleaseCandidate')][string]$ReleaseTier = '',
    [ValidateSet('PERSISTENT_QA_KEY', 'EPHEMERAL_INTERNAL_ONLY')][string]$SigningMode = 'PERSISTENT_QA_KEY',
    [string]$ExpectedSignerSha256 = '',
    [string]$RunUrl = '',
    [switch]$RequireCiProvenance,
    [string]$VerificationSummaryPath = '',
    [string]$FormalNoxApprovedSha = '',
    [switch]$RequireApksignerVerify,
    [string]$RepoRoot = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Resolve-EffectiveReleaseTier {
    param([string]$RequestedTier, [string]$Profile)
    if ([string]::IsNullOrWhiteSpace($RequestedTier)) {
        return $(if ($Profile -eq 'DebugSynthetic') { 'InternalDebugSynthetic' } else { 'ControlledQA' })
    }
    return $RequestedTier
}

function Assert-CleanSourceSha {
    param([string]$Root, [string]$ExpectedSha)
    $observed = (& git -C $Root rev-parse HEAD 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $observed -cne $ExpectedSha) {
        throw "Frozen source SHA mismatch: expected $ExpectedSha, observed '$observed'."
    }
    $status = (& git -C $Root status --porcelain=v1 --untracked-files=all 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw 'Git status inspection failed during QA staging.'
    }
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        throw 'QA staging requires a clean Git working tree (no tracked, staged, or untracked modifications).'
    }
}

function Assert-CiProvenance {
    param([string]$RunUrl, [bool]$RequireCiProvenance)
    $pattern = '^https://github\.com/Kirjane/Dev-Salryn-Cafe/actions/runs/\d+$'
    if ($RequireCiProvenance) {
        if ([string]::IsNullOrWhiteSpace($RunUrl)) {
            throw 'CI provenance required (-RequireCiProvenance): -RunUrl cannot be empty.'
        }
        if ($RunUrl -notmatch $pattern) {
            throw "CI provenance rejected: -RunUrl '$RunUrl' does not match $pattern."
        }
    } elseif (-not [string]::IsNullOrWhiteSpace($RunUrl) -and $RunUrl -notmatch $pattern) {
        throw "Invalid -RunUrl format: '$RunUrl' must match $pattern."
    }
}

function Assert-TierAndVerificationPolicy {
    param(
        [string]$Tier,
        [string]$Profile,
        [string]$SigningMode,
        [string]$ExpectedSignerSha256,
        [string]$VerificationSummaryPath,
        [string]$SourceSha,
        [string]$FormalNoxApprovedSha
    )
    if ($Tier -eq 'InternalDebugSynthetic') {
        if ($Profile -ne 'DebugSynthetic') {
            throw "ReleaseTier 'InternalDebugSynthetic' requires Profile 'DebugSynthetic' (got '$Profile')."
        }
        return [pscustomobject]@{ verificationStatus = 'OPTIONAL_INTERNAL_DEBUG'; releaseEntitledVerdict = 'NOT_APPLICABLE_DEBUG'; formalNoxApproval = 'NOT_ASSERTED' }
    }

    if ($Profile -ne 'ReleaseEntitled') {
        throw "ReleaseTier '$Tier' requires Profile 'ReleaseEntitled' (got '$Profile')."
    }
    if ($SigningMode -ne 'PERSISTENT_QA_KEY') {
        throw "ReleaseTier '$Tier' forbids ephemeral Android signing; SigningMode must be 'PERSISTENT_QA_KEY'."
    }
    if ($ExpectedSignerSha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "ReleaseTier '$Tier' requires a 64-hex persistent QA Android signer SHA-256 (-ExpectedSignerSha256)."
    }
    if ([string]::IsNullOrWhiteSpace($VerificationSummaryPath) -or -not (Test-Path -LiteralPath $VerificationSummaryPath -PathType Leaf)) {
        throw "ReleaseTier '$Tier' requires an existing verification-summary.json (-VerificationSummaryPath)."
    }
    $summary = Get-Content -LiteralPath $VerificationSummaryPath -Raw | ConvertFrom-Json
    if ($summary.overallStatus -cne 'PASS') {
        throw "ReleaseTier '$Tier' blocked: verification-summary.json overallStatus is '$($summary.overallStatus)' (must be 'PASS')."
    }
    $summaryShaMatches = ($summary.commitSha -ceq $SourceSha) -or ($summary.prHeadSha -ceq $SourceSha)
    if (-not $summaryShaMatches) {
        throw "Verification summary SHA mismatch: expected $SourceSha, summary had commitSha='$($summary.commitSha)' prHeadSha='$($summary.prHeadSha)'."
    }
    $failedGates = @($summary.gates | Where-Object { $_.status -notin @('PASS', 'BUILD_OK', 'RUNTIME_OBSERVED', 'SOURCE_CHECKED') })
    if ($failedGates.Count -gt 0) {
        $names = ($failedGates | ForEach-Object { $_.gate }) -join ', '
        throw "Verification summary contains non-passing gates: $names"
    }

    $formalNox = 'NOT_ASSERTED'
    if ($Tier -eq 'ReleaseCandidate') {
        if ($FormalNoxApprovedSha -cne $SourceSha) {
            throw "ReleaseTier 'ReleaseCandidate' blocked: FormalNoxApprovedSha must match frozen source SHA '$SourceSha'."
        }
        if ($summary.releaseEntitledVerdict -cne 'PASS') {
            throw "ReleaseTier 'ReleaseCandidate' blocked: releaseEntitledVerdict is '$($summary.releaseEntitledVerdict)' (must be 'PASS')."
        }
        $formalNox = "APPROVED_AT_$SourceSha"
    }

    return [pscustomobject]@{
        verificationStatus = $summary.overallStatus
        releaseEntitledVerdict = $(if ($summary.releaseEntitledVerdict) { [string]$summary.releaseEntitledVerdict } else { 'UNKNOWN' })
        formalNoxApproval = $formalNox
    }
}

function Assert-PackageDirectoryHygiene {
    param([string]$Root, [string[]]$RequiredFiles)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Package directory not found: $Root"
    }
    $entries = @(Get-ChildItem -LiteralPath $Root -Force)
    $unexpected = @($entries | Where-Object { $_.PSIsContainer -or ($RequiredFiles -notcontains $_.Name) })
    if ($unexpected.Count -gt 0) {
        $names = ($unexpected | ForEach-Object { $_.Name }) -join ', '
        throw "Package directory contains unexpected/stale files prior to staging: $names"
    }
    foreach ($name in $RequiredFiles) {
        $path = Join-Path $Root $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing matched QA artifact: $name"
        }
        if ((Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Empty artifact: $name"
        }
    }
}

function Test-InstallerProvenance {
    param([string]$ExePath, [string]$SourceSha)
    $bytes = [IO.File]::ReadAllBytes($ExePath)
    $isPe = ($bytes.Length -ge 2 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A)
    if ($isPe) {
        $verInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath)
        $desc = [string]$verInfo.FileDescription
        if ([string]::IsNullOrWhiteSpace($desc) -or ($desc -notmatch [regex]::Escape($SourceSha))) {
            throw "Windows installer PE FileDescription does not embed exact source SHA $SourceSha (observed: '$desc')."
        }
        if ($desc -notmatch 'Salryn Cafe') {
            throw "Windows installer PE FileDescription does not identify 'Salryn Cafe' (observed: '$desc')."
        }
        return [pscustomobject]@{ verified = $true; method = 'PE_FILE_DESCRIPTION'; detail = $desc }
    }
    # Synthetic non-PE test fixture fallback: must explicitly embed Salryn Cafe + exact SHA.
    $ascii = [System.Text.Encoding]::UTF8.GetString($bytes)
    $utf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
    $hasSha = ($ascii.Contains($SourceSha)) -or ($utf16.Contains($SourceSha))
    $hasName = ($ascii.Contains('Salryn Cafe')) -or ($utf16.Contains('Salryn Cafe'))
    if (-not ($hasSha -and $hasName)) {
        throw "Installer artifact does not embed exact source SHA $SourceSha and 'Salryn Cafe' identity."
    }
    return [pscustomobject]@{ verified = $true; method = 'SYNTHETIC_MARKER'; detail = "Embedded $SourceSha" }
}

function Find-AndroidBuildTool {
    param([string[]]$CandidateNames)
    $cmd = Get-Command @CandidateNames -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    foreach ($sdkRoot in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'SalrynForgeTools\android-sdk'))) {
        if ([string]::IsNullOrWhiteSpace($sdkRoot)) { continue }
        $bt = Join-Path $sdkRoot 'build-tools'
        if (Test-Path -LiteralPath $bt -PathType Container) {
            foreach ($name in $CandidateNames) {
                $found = Get-ChildItem -LiteralPath $bt -Recurse -File -Filter $name -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
                if ($found) { return $found.FullName }
            }
        }
    }
    return $null
}

function Find-ApksignerExecutable {
    return (Find-AndroidBuildTool -CandidateNames @('apksigner.bat', 'apksigner'))
}

function Find-Aapt2Executable {
    return (Find-AndroidBuildTool -CandidateNames @('aapt2.exe', 'aapt2'))
}

function Get-ApkSignerSha256 {
    param([string]$ApkPath, [string]$ApksignerPath)
    $out = (& $ApksignerPath verify --print-certs $ApkPath 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "apksigner verification failed for ${ApkPath}: $out"
    }
    if ($out -match 'Signer #1 certificate SHA-256 digest:\s*([0-9a-fA-F]{64})') {
        return $Matches[1].ToLowerInvariant()
    }
    throw "apksigner did not emit Signer #1 SHA-256 digest for $ApkPath."
}

function Test-AndroidApkProvenance {
    param(
        [string]$ApkPath,
        [string]$SourceSha,
        [string]$Profile,
        [string]$ExpectedSignerSha256,
        [bool]$RequireApksignerVerify
    )
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ApkPath)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName })
        if ($names -notcontains 'AndroidManifest.xml') {
            throw 'Android APK missing AndroidManifest.xml.'
        }
        $provEntry = $zip.GetEntry('assets/build-provenance.json')
        if ($null -eq $provEntry) {
            throw 'Android APK missing assets/build-provenance.json source provenance.'
        }
        $reader = [System.IO.StreamReader]::new($provEntry.Open())
        try { $provJson = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $prov = $provJson | ConvertFrom-Json
        if ($prov.sourceSha -cne $SourceSha) {
            throw "Android APK provenance sourceSha mismatch: expected $SourceSha, got '$($prov.sourceSha)'."
        }
        if ($prov.profile -cne $Profile) {
            throw "Android APK provenance profile mismatch: expected $Profile, got '$($prov.profile)'."
        }
        if ($prov.packageId -cne 'ph.kirjanelabs.salryncafe') {
            throw "Android APK packageId mismatch: expected 'ph.kirjanelabs.salryncafe', got '$($prov.packageId)'."
        }
        $vc = 0
        if (-not [int]::TryParse([string]$prov.versionCode, [ref]$vc) -or $vc -lt 1) {
            throw "Android APK provenance invalid versionCode: '$($prov.versionCode)'."
        }
        if ($Profile -eq 'ReleaseEntitled') {
            $debugEntries = @($names | Where-Object { $_ -match '(^|/)libmono-component-debugger\.so$|\.pdb(\.so)?$' })
            if ($debugEntries.Count -gt 0) {
                throw "ReleaseEntitled APK contains debug-only artifacts: $($debugEntries -join ', ')"
            }
        }
    } finally {
        $zip.Dispose()
    }

    $observedSigner = ''
    $apksigner = Find-ApksignerExecutable
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSignerSha256)) {
        $expectedLower = $ExpectedSignerSha256.ToLowerInvariant()
        if ($apksigner -and ($RequireApksignerVerify -or ($prov.signingVerificationMode -ne 'SYNTHETIC_UNIT_TEST'))) {
            $observedSigner = Get-ApkSignerSha256 -ApkPath $ApkPath -ApksignerPath $apksigner
            if ($observedSigner -cne $expectedLower) {
                throw "Android APK signer SHA-256 mismatch: expected $expectedLower, observed $observedSigner."
            }
        } elseif ($RequireApksignerVerify) {
            throw 'apksigner tool not found while -RequireApksignerVerify was requested.'
        } else {
            $provSigner = [string]$prov.signerSha256
            if ([string]::IsNullOrWhiteSpace($provSigner) -or $provSigner.ToLowerInvariant() -cne $expectedLower) {
                throw "Android APK signer SHA-256 mismatch in provenance: expected $expectedLower, got '$provSigner'."
            }
            $observedSigner = $provSigner.ToLowerInvariant()
        }
    } elseif ($apksigner -and $prov.signingVerificationMode -ne 'SYNTHETIC_UNIT_TEST') {
        $observedSigner = Get-ApkSignerSha256 -ApkPath $ApkPath -ApksignerPath $apksigner
    }

    $aapt2 = Find-Aapt2Executable
    if ($aapt2 -and $prov.signingVerificationMode -ne 'SYNTHETIC_UNIT_TEST') {
        $badging = (& $aapt2 dump badging $ApkPath 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) { throw "aapt2 dump badging failed for ${ApkPath}: $badging" }
        if ($badging -notmatch "package:\s+name='([^']+)'\s+versionCode='(\d+)'") {
            throw "aapt2 badging did not emit package name and versionCode for $ApkPath."
        }
        if ($Matches[1] -cne [string]$prov.packageId) {
            throw "APK manifest packageId '$($Matches[1])' does not match provenance '$($prov.packageId)'."
        }
        if ([int]$Matches[2] -ne $vc) {
            throw "APK manifest versionCode '$($Matches[2])' does not match provenance versionCode '$vc'."
        }
    }

    return [pscustomobject]@{
        verified = $true
        packageId = [string]$prov.packageId
        versionCode = $vc
        versionName = [string]$prov.versionName
        signerSha256 = $observedSigner
    }
}

function Test-CentralZipProvenance {
    param([string]$ZipPath, [string]$SourceSha, [string]$Profile)
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        foreach ($req in @(
            'Salryn.Sync.Central.Server.dll',
            'Salryn.Sync.Central.Server.runtimeconfig.json',
            'appsettings.json',
            'build-provenance.json',
            'deploy-reference/README.md',
            'deploy-reference/.env.example'
        )) {
            if ($names -notcontains $req) {
                throw "Central QA zip missing required file: $req"
            }
        }
        $secretFiles = @($names | Where-Object { $_ -match '(^|/)\.env$|\.(keystore|jks|pfx|p12|key|pem)$' })
        if ($secretFiles.Count -gt 0) {
            throw "Central QA zip contains forbidden secret/key material: $($secretFiles -join ', ')"
        }
        $provEntry = $zip.GetEntry('build-provenance.json')
        $reader = [System.IO.StreamReader]::new($provEntry.Open())
        try { $prov = ($reader.ReadToEnd() | ConvertFrom-Json) } finally { $reader.Dispose() }
        if ($prov.sourceSha -cne $SourceSha) {
            throw "Central QA zip provenance sourceSha mismatch: expected $SourceSha, got '$($prov.sourceSha)'."
        }
        if ($prov.profile -cne $Profile) {
            throw "Central QA zip provenance profile mismatch: expected $Profile, got '$($prov.profile)'."
        }
        $expectedConfig = if ($Profile -eq 'DebugSynthetic') { 'Debug' } else { 'Release' }
        if ($prov.configuration -cne $expectedConfig) {
            throw "Central QA zip configuration mismatch: expected $expectedConfig for $Profile, got '$($prov.configuration)'."
        }

        $rcEntry = $zip.GetEntry('Salryn.Sync.Central.Server.runtimeconfig.json')
        $rcReader = [System.IO.StreamReader]::new($rcEntry.Open())
        try { $rc = ($rcReader.ReadToEnd() | ConvertFrom-Json) } finally { $rcReader.Dispose() }
        $tfm = [string]$rc.runtimeOptions.tfm
        if ([string]::IsNullOrWhiteSpace($tfm)) {
            throw 'Central runtimeconfig.json missing runtimeOptions.tfm.'
        }
        return [pscustomobject]@{ verified = $true; runtimeTfm = $tfm }
    } finally {
        $zip.Dispose()
    }
}

function Test-StagedManifestIntegrity {
    param([string]$PackageDirectory, [string]$ExpectedSourceSha)
    $manifestPath = Join-Path $PackageDirectory 'QA-RELEASE-MANIFEST.json'
    $sumsPath = Join-Path $PackageDirectory 'SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Missing QA-RELEASE-MANIFEST.json.' }
    if (-not (Test-Path -LiteralPath $sumsPath -PathType Leaf)) { throw 'Missing SHA256SUMS.txt.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.sourceSha -cne $ExpectedSourceSha) {
        throw "Manifest sourceSha tampered: expected $ExpectedSourceSha, got '$($manifest.sourceSha)'."
    }
    $sumLines = @(Get-Content -LiteralPath $sumsPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($sumLines.Count -ne @($manifest.artifacts).Count) {
        throw 'SHA256SUMS.txt entry count does not match manifest artifacts.'
    }
    $sumMap = @{}
    foreach ($line in $sumLines) {
        if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
            throw "Malformed SHA256SUMS.txt line: '$line'"
        }
        $sumMap[$Matches[2]] = $Matches[1]
    }
    foreach ($art in $manifest.artifacts) {
        $filePath = Join-Path $PackageDirectory $art.file
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "Manifest references missing file: $($art.file)"
        }
        $actual = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $art.sha256) {
            throw "Manifest SHA-256 mismatch for $($art.file): expected $($art.sha256), actual $actual."
        }
        if (-not $sumMap.ContainsKey($art.file) -or $sumMap[$art.file] -cne $actual) {
            throw "SHA256SUMS.txt mismatch for $($art.file)."
        }
    }
}

$effectiveRepoRoot = if ([string]::IsNullOrWhiteSpace($RepoRoot)) { Split-Path $PSScriptRoot -Parent } else { $RepoRoot }
$effectiveTier = Resolve-EffectiveReleaseTier -RequestedTier $ReleaseTier -Profile $Profile

Assert-CleanSourceSha -Root $effectiveRepoRoot -ExpectedSha $SourceSha
Assert-CiProvenance -RunUrl $RunUrl -RequireCiProvenance:$RequireCiProvenance
$policyResult = Assert-TierAndVerificationPolicy `
    -Tier $effectiveTier `
    -Profile $Profile `
    -SigningMode $SigningMode `
    -ExpectedSignerSha256 $ExpectedSignerSha256 `
    -VerificationSummaryPath $VerificationSummaryPath `
    -SourceSha $SourceSha `
    -FormalNoxApprovedSha $FormalNoxApprovedSha

$root = (Resolve-Path -LiteralPath $PackageDirectory).Path
$required = @('Salryn-Cafe-Setup.exe', 'Salryn-Cafe-QA.apk', 'Salryn-Cafe-Central-QA.zip')
Assert-PackageDirectoryHygiene -Root $root -RequiredFiles $required

$installerCheck = Test-InstallerProvenance -ExePath (Join-Path $root 'Salryn-Cafe-Setup.exe') -SourceSha $SourceSha
$apkCheck = Test-AndroidApkProvenance -ApkPath (Join-Path $root 'Salryn-Cafe-QA.apk') -SourceSha $SourceSha -Profile $Profile -ExpectedSignerSha256 $ExpectedSignerSha256 -RequireApksignerVerify:$RequireApksignerVerify
$centralCheck = Test-CentralZipProvenance -ZipPath (Join-Path $root 'Salryn-Cafe-Central-QA.zip') -SourceSha $SourceSha -Profile $Profile

$files = foreach ($name in $required) {
    $path = Join-Path $root $name
    $item = Get-Item -LiteralPath $path
    [ordered]@{
        file = $name
        bytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        sourceSha = $SourceSha
    }
}

Copy-Item -LiteralPath (Join-Path $effectiveRepoRoot '00-INTEGRATION/QA-SETUP-PAIRING.md') -Destination $root
Copy-Item -LiteralPath (Join-Path $effectiveRepoRoot '00-INTEGRATION/QA-ACCEPTANCE-31.md') -Destination $root

$manifest = [ordered]@{
    schemaVersion = 2
    repository = 'Kirjane/Dev-Salryn-Cafe'
    sourceSha = $SourceSha
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    workflowRunUrl = $RunUrl
    classification = 'QA_STAGING_ONLY'
    releaseTier = $effectiveTier
    releaseCandidateAccepted = $false
    productionAuthorized = $false
    profile = $Profile
    provenanceVerified = $true
    installerProvenance = $installerCheck.method
    androidPackageId = $apkCheck.packageId
    androidVersionCode = $apkCheck.versionCode
    androidVersionName = $apkCheck.versionName
    androidSigning = $SigningMode
    androidSignerSha256 = $apkCheck.signerSha256
    windowsSigning = 'UNSIGNED_QA'
    centralRuntimeRequirement = "Framework-dependent ($($centralCheck.runtimeTfm); requires ASP.NET Core 10.0 Runtime on host)"
    licensing = if ($Profile -eq 'ReleaseEntitled') { 'Signed Release entitlements enforced; authorized QA license required and not included' } else { 'Debug-only synthetic environment; no Release entitlement acceptance' }
    verificationStatus = $policyResult.verificationStatus
    releaseEntitledVerdict = $policyResult.releaseEntitledVerdict
    topologyStatus = 'WINDOWS_LOCAL_DISK_COHOST_QA; frozen-SHA integration evidence and Human setup required; no approved endpoint bundled'
    formalNoxApproval = $policyResult.formalNoxApproval
    humanAcceptance = 'NOT_RUN'
    artifacts = @($files)
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'QA-RELEASE-MANIFEST.json') -Encoding utf8
$files | ForEach-Object { "$($_.sha256)  $($_.file)" } | Set-Content -LiteralPath (Join-Path $root 'SHA256SUMS.txt') -Encoding ascii

Test-StagedManifestIntegrity -PackageDirectory $root -ExpectedSourceSha $SourceSha
Write-Output "QA staging manifest verified and created for $SourceSha (tier=$effectiveTier profile=$Profile); no production or RC acceptance asserted."
