<# Synthetic positive and negative regression tests for scripts/stage-qa-release.ps1. #>
param([string]$StagerPath = (Join-Path $PSScriptRoot 'stage-qa-release.ps1'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($StagerPath, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw ($errors | Out-String) }
foreach ($fn in $ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($fn.Extent.Text))
}

$assertions = 0
function Assert {
    param([bool]$Condition, [string]$Message)
    $script:assertions++
    if (-not $Condition) { throw "Assertion failed: $Message" }
}
function Assert-Throws {
    param([scriptblock]$Action, [string]$Message)
    $thrown = $false
    try { & $Action | Out-Null } catch { $thrown = $true }
    Assert $thrown $Message
}

function New-SyntheticZip {
    param([string]$Path, [hashtable]$Entries)
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($key in $Entries.Keys) {
            $entry = $zip.CreateEntry($key)
            $writer = [System.IO.StreamWriter]::new($entry.Open())
            try { $writer.Write([string]$Entries[$key]) } finally { $writer.Dispose() }
        }
    } finally {
        $zip.Dispose()
    }
}

function New-ValidPackageDir {
    param(
        [string]$Dir,
        [string]$Sha,
        [string]$Profile = 'ReleaseEntitled',
        [string]$SignerSha256 = ('a' * 64),
        [int]$VersionCode = 2,
        [hashtable]$ApkExtraEntries = @{},
        [hashtable]$CentralOverrides = @{},
        [string]$InstallerContent = ''
    )
    if (Test-Path -LiteralPath $Dir) { Remove-Item -LiteralPath $Dir -Recurse -Force }
    New-Item -ItemType Directory -Path $Dir | Out-Null
    $config = if ($Profile -eq 'DebugSynthetic') { 'Debug' } else { 'Release' }
    $exeText = if ([string]::IsNullOrEmpty($InstallerContent)) { "SYNTHETIC Salryn Cafe Setup rev $Sha" } else { $InstallerContent }
    [IO.File]::WriteAllText((Join-Path $Dir 'Salryn-Cafe-Setup.exe'), $exeText)

    $apkProv = ([ordered]@{
        sourceSha = $Sha
        profile = $Profile
        configuration = $config
        packageId = 'ph.kirjanelabs.salryncafe'
        versionCode = $VersionCode
        versionName = "1.0.0-qa.$VersionCode"
        signerSha256 = $SignerSha256
        signingVerificationMode = 'SYNTHETIC_UNIT_TEST'
    } | ConvertTo-Json -Depth 4)
    $apkEntries = @{
        'AndroidManifest.xml' = 'SYNTHETIC_BINARY_MANIFEST'
        'assets/build-provenance.json' = $apkProv
    }
    foreach ($k in $ApkExtraEntries.Keys) { $apkEntries[$k] = $ApkExtraEntries[$k] }
    New-SyntheticZip -Path (Join-Path $Dir 'Salryn-Cafe-QA.apk') -Entries $apkEntries

    $centralProv = ([ordered]@{
        sourceSha = $Sha
        profile = $Profile
        configuration = $config
    } | ConvertTo-Json -Depth 4)
    $centralEntries = @{
        'Salryn.Sync.Central.Server.dll' = "SYNTHETIC_DLL_$Sha"
        'Salryn.Sync.Central.Server.runtimeconfig.json' = '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.AspNetCore.App","version":"10.0.0"}}}'
        'appsettings.json' = '{"Logging":{"LogLevel":{"Default":"Information"}}}'
        'build-provenance.json' = $centralProv
        'deploy-reference/README.md' = '# Central QA deploy reference'
        'deploy-reference/.env.example' = 'ASPNETCORE_URLS=http://127.0.0.1:5450'
    }
    foreach ($k in $CentralOverrides.Keys) {
        if ($null -eq $CentralOverrides[$k]) { $centralEntries.Remove($k) }
        else { $centralEntries[$k] = $CentralOverrides[$k] }
    }
    New-SyntheticZip -Path (Join-Path $Dir 'Salryn-Cafe-Central-QA.zip') -Entries $centralEntries
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('salryn-stage-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $docsDir = Join-Path $fixture '00-INTEGRATION'
    New-Item -ItemType Directory -Path $docsDir | Out-Null
    '# Setup' | Set-Content -LiteralPath (Join-Path $docsDir 'QA-SETUP-PAIRING.md')
    '# Acceptance' | Set-Content -LiteralPath (Join-Path $docsDir 'QA-ACCEPTANCE-31.md')
    & git -C $fixture init --quiet
    & git -C $fixture config user.name 'Synthetic Stage Test'
    & git -C $fixture config user.email 'synthetic@example.invalid'
    & git -C $fixture config core.autocrlf false
    & git -C $fixture add -- 00-INTEGRATION/QA-SETUP-PAIRING.md 00-INTEGRATION/QA-ACCEPTANCE-31.md
    & git -C $fixture commit --quiet -m 'synthetic qa stage base'
    if ($LASTEXITCODE -ne 0) { throw 'Fixture git commit failed.' }
    $sha = (& git -C $fixture rev-parse HEAD).Trim()
    $wrongSha = ('0' * 40)
    $signerSha = ('b' * 64)
    $validRunUrl = 'https://github.com/Kirjane/Dev-Salryn-Cafe/actions/runs/44899027883'

    $workRoot = Join-Path ([IO.Path]::GetTempPath()) ('salryn-stage-pkgs-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $workRoot | Out-Null

    $greenSummaryPath = Join-Path $workRoot 'green-summary.json'
    ([ordered]@{
        commitSha = $sha; prHeadSha = $sha; mode = 'Windows'; configuration = 'Release'; overallStatus = 'PASS'
        releaseEntitledVerdict = 'BLOCKED_EXTERNAL (NOT RUN)'
        gates = @(@{ gate = 'xunit:Salryn.Cafe.Tests'; status = 'PASS' }, @{ gate = 'core-smoke'; status = 'RUNTIME_OBSERVED' })
    } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $greenSummaryPath

    $redSummaryPath = Join-Path $workRoot 'red-summary.json'
    ([ordered]@{
        commitSha = $sha; prHeadSha = $sha; mode = 'Windows'; configuration = 'Release'; overallStatus = 'FAIL'
        releaseEntitledVerdict = 'BLOCKED_EXTERNAL'
        gates = @(@{ gate = 'core-smoke'; status = 'FAIL' })
    } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $redSummaryPath

    $debugSummaryPath = Join-Path $workRoot 'debug-summary.json'
    ([ordered]@{
        commitSha = $sha; prHeadSha = $sha; mode = 'Windows'; configuration = 'Debug'; overallStatus = 'PASS'
        releaseEntitledVerdict = 'NOT_APPLICABLE_DEBUG'
        gates = @(@{ gate = 'core-smoke'; status = 'RUNTIME_OBSERVED' })
    } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $debugSummaryPath

    $androidModeSummaryPath = Join-Path $workRoot 'android-summary.json'
    ([ordered]@{
        commitSha = $sha; prHeadSha = $sha; mode = 'Android'; configuration = 'Release'; overallStatus = 'PASS'
        releaseEntitledVerdict = 'NOT_APPLICABLE'
        gates = @(@{ gate = 'xunit:Salryn.Cafe.I6BTests'; status = 'PASS' })
    } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $androidModeSummaryPath

    $emptyGatesSummaryPath = Join-Path $workRoot 'empty-gates-summary.json'
    ([ordered]@{
        commitSha = $sha; prHeadSha = $sha; mode = 'Windows'; configuration = 'Release'; overallStatus = 'PASS'
        releaseEntitledVerdict = 'BLOCKED_EXTERNAL'
        gates = @()
    } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $emptyGatesSummaryPath

    $rcEntitledSummaryPath = Join-Path $workRoot 'rc-entitled-summary.json'
    ([ordered]@{
        commitSha = $sha; prHeadSha = $sha; mode = 'Windows'; configuration = 'Release'; overallStatus = 'PASS'
        releaseEntitledVerdict = 'PASS'
        gates = @(@{ gate = 'core-smoke'; status = 'RUNTIME_OBSERVED' })
    } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $rcEntitledSummaryPath

    # 1. Negative (Aegis P2): Non-PE installer & unsigned APK with untrusted SYNTHETIC_UNIT_TEST JSON MUST fail closed without explicit test switch!
    $pkgNoSwitch = Join-Path $workRoot 'neg-synthetic-without-switch'
    New-ValidPackageDir -Dir $pkgNoSwitch -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha -VersionCode 2
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgNoSwitch -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Non-PE installer and untrusted APK JSON cannot bypass strict PE/apksigner checks in ControlledQA.'
    Assert-Throws {
        Test-AndroidApkProvenance -ApkPath (Join-Path $pkgNoSwitch 'Salryn-Cafe-QA.apk') -SourceSha $sha -Profile 'ReleaseEntitled' `
            -Tier 'ControlledQA' -ExpectedSignerSha256 $signerSha -RequireApksignerVerify:$false -AllowSyntheticFixtures:$false
    } 'Untrusted APK JSON signingVerificationMode cannot bypass apksigner/aapt2 in ControlledQA.'

    # 2. Positive (explicit unit-test synthetic mode): ControlledQA policy passes and honestly stamps SYNTHETIC_UNIT_TEST_ONLY + provenanceVerified=$false
    $pkgOk = Join-Path $workRoot 'ok-controlled-qa'
    New-ValidPackageDir -Dir $pkgOk -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha -VersionCode 2
    & $StagerPath -SourceSha $sha -PackageDirectory $pkgOk -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
        -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
        -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture | Out-Null
    Assert (Test-Path -LiteralPath (Join-Path $pkgOk 'QA-RELEASE-MANIFEST.json')) 'ControlledQA emits manifest.'
    Assert (Test-Path -LiteralPath (Join-Path $pkgOk 'SHA256SUMS.txt')) 'ControlledQA emits SHA256SUMS.'
    $manifest = Get-Content -LiteralPath (Join-Path $pkgOk 'QA-RELEASE-MANIFEST.json') -Raw | ConvertFrom-Json
    Assert ($manifest.sourceSha -eq $sha -and $manifest.releaseTier -eq 'ControlledQA') 'Manifest records exact SHA and tier.'
    Assert ($manifest.classification -eq 'SYNTHETIC_UNIT_TEST_ONLY' -and $manifest.provenanceVerified -eq $false) 'Unit-test synthetic switch cannot claim QA_STAGING_ONLY or provenanceVerified=true.'
    Assert ($manifest.androidSigning -eq 'PERSISTENT_QA_KEY' -and $manifest.androidSignerSha256 -eq $signerSha -and $manifest.androidVersionCode -eq 2) 'Manifest records persistent QA signer & versionCode.'
    Assert ($manifest.releaseCandidateAccepted -eq $false -and $manifest.productionAuthorized -eq $false) 'Manifest never asserts RC or production acceptance.'

    # 3. Positive: InternalDebugSynthetic with ephemeral signing passes
    $pkgDbg = Join-Path $workRoot 'ok-internal-debug'
    New-ValidPackageDir -Dir $pkgDbg -Sha $sha -Profile 'DebugSynthetic' -SignerSha256 '' -VersionCode 1
    & $StagerPath -SourceSha $sha -PackageDirectory $pkgDbg -Profile 'DebugSynthetic' -ReleaseTier 'InternalDebugSynthetic' `
        -SigningMode 'EPHEMERAL_INTERNAL_ONLY' -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture | Out-Null
    $dbgManifest = Get-Content -LiteralPath (Join-Path $pkgDbg 'QA-RELEASE-MANIFEST.json') -Raw | ConvertFrom-Json
    Assert ($dbgManifest.releaseTier -eq 'InternalDebugSynthetic' -and $dbgManifest.androidSigning -eq 'EPHEMERAL_INTERNAL_ONLY') 'InternalDebugSynthetic accepts ephemeral signing.'

    # 4. Negative: Tampered manifest rejected
    $tamperManifest = Get-Content -LiteralPath (Join-Path $pkgOk 'QA-RELEASE-MANIFEST.json') -Raw | ConvertFrom-Json
    $tamperManifest.artifacts[0].sha256 = ('f' * 64)
    $tamperManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $pkgOk 'QA-RELEASE-MANIFEST.json')
    Assert-Throws { Test-StagedManifestIntegrity -PackageDirectory $pkgOk -ExpectedSourceSha $sha } 'Tampered manifest hash rejected.'

    # 5. Negative: Wrong source SHA rejected
    $pkgWrongSha = Join-Path $workRoot 'neg-wrong-sha'
    New-ValidPackageDir -Dir $pkgWrongSha -Sha $wrongSha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    Assert-Throws {
        & $StagerPath -SourceSha $wrongSha -PackageDirectory $pkgWrongSha -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'Frozen source SHA mismatch rejected.'

    # 6. Negative: APK provenance SHA mismatch rejected
    $pkgApkWrongSha = Join-Path $workRoot 'neg-apk-wrong-sha'
    New-ValidPackageDir -Dir $pkgApkWrongSha -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha `
        -ApkExtraEntries @{ 'assets/build-provenance.json' = ('{"sourceSha":"' + $wrongSha + '","profile":"ReleaseEntitled","packageId":"ph.kirjanelabs.salryncafe","versionCode":2,"signerSha256":"' + $signerSha + '"}') }
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgApkWrongSha -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'APK source SHA mismatch rejected.'

    # 7. Negative: Debug debugger artifact inside ReleaseEntitled APK rejected
    $pkgProfileMismatch = Join-Path $workRoot 'neg-apk-debug-in-release'
    New-ValidPackageDir -Dir $pkgProfileMismatch -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha `
        -ApkExtraEntries @{ 'lib/arm64-v8a/libmono-component-debugger.so' = 'DEBUGGER' }
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgProfileMismatch -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'Debug artifact in ReleaseEntitled APK rejected.'

    # 8. Negative: Missing Central runtimeconfig.json rejected
    $pkgMissingRc = Join-Path $workRoot 'neg-central-missing-rc'
    New-ValidPackageDir -Dir $pkgMissingRc -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha `
        -CentralOverrides @{ 'Salryn.Sync.Central.Server.runtimeconfig.json' = $null }
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgMissingRc -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'Central zip missing runtimeconfig.json rejected.'

    # 9. Negative: Secret .env, .env.production, and *credentials*.json in Central zip rejected
    foreach ($secretEntry in @('deploy-reference/.env', 'deploy-reference/.env.production', 'deploy-reference/service-credentials.json')) {
        $pkgCentralSecret = Join-Path $workRoot ('neg-central-secret-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
        New-ValidPackageDir -Dir $pkgCentralSecret -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha `
            -CentralOverrides @{ $secretEntry = 'SECRET=1' }
        Assert-Throws {
            & $StagerPath -SourceSha $sha -PackageDirectory $pkgCentralSecret -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
                -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
                -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
        } "Central zip containing secret '$secretEntry' rejected."
    }

    # 10. Negative: Installer missing source SHA rejected
    $pkgBadExe = Join-Path $workRoot 'neg-installer-no-sha'
    New-ValidPackageDir -Dir $pkgBadExe -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha -InstallerContent 'Salryn Cafe Setup without revision'
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgBadExe -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'Installer missing source SHA rejected.'

    # 11. Negative: Empty or invalid RunUrl with -RequireCiProvenance rejected
    $pkgNoUrl = Join-Path $workRoot 'neg-no-run-url'
    New-ValidPackageDir -Dir $pkgNoUrl -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgNoUrl -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl '' -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'Empty RunUrl rejected when -RequireCiProvenance set.'
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgNoUrl -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl 'https://example.invalid/run/1' -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'Untrusted RunUrl rejected.'

    # 12. Negative: Stale extra file in package directory rejected
    $pkgStale = Join-Path $workRoot 'neg-stale-file'
    New-ValidPackageDir -Dir $pkgStale -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    'STALE' | Set-Content -LiteralPath (Join-Path $pkgStale 'old-artifact.apk')
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgStale -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'Pre-existing stale file in package directory rejected.'

    # 13. Negative: Ephemeral signing under ControlledQA rejected
    $pkgEph = Join-Path $workRoot 'neg-ephemeral-controlled-qa'
    New-ValidPackageDir -Dir $pkgEph -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgEph -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'EPHEMERAL_INTERNAL_ONLY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'Ephemeral signing rejected under ControlledQA.'

    # 14. Negative (Aegis P2): Red, Debug, Android-mode, or empty-gates verification summary rejected under ControlledQA
    foreach ($badSummary in @(
        @{ path = $redSummaryPath; label = 'Red verification summary' },
        @{ path = $debugSummaryPath; label = 'Debug verification summary' },
        @{ path = $androidModeSummaryPath; label = 'Android-only verification summary' },
        @{ path = $emptyGatesSummaryPath; label = 'Empty-gates verification summary' }
    )) {
        $pkgBadSum = Join-Path $workRoot ('neg-bad-sum-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
        New-ValidPackageDir -Dir $pkgBadSum -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
        Assert-Throws {
            & $StagerPath -SourceSha $sha -PackageDirectory $pkgBadSum -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
                -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
                -VerificationSummaryPath $badSummary.path -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
        } "$($badSummary.label) rejected under ControlledQA."
    }

    # 15. Negative: ReleaseCandidate without formal Nox approval or ReleaseEntitled PASS rejected
    $pkgRc = Join-Path $workRoot 'neg-rc-unapproved'
    New-ValidPackageDir -Dir $pkgRc -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgRc -Profile 'ReleaseEntitled' -ReleaseTier 'ReleaseCandidate' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $rcEntitledSummaryPath -FormalNoxApprovedSha '' -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'ReleaseCandidate without formal Nox approval rejected.'
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgRc -Profile 'ReleaseEntitled' -ReleaseTier 'ReleaseCandidate' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -FormalNoxApprovedSha $sha -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'ReleaseCandidate with BLOCKED_EXTERNAL releaseEntitledVerdict rejected.'

    # 16. Negative: Dirty Git working tree rejected
    'DIRTY' | Set-Content -LiteralPath (Join-Path $fixture 'untracked.txt')
    $pkgDirty = Join-Path $workRoot 'neg-dirty-tree'
    New-ValidPackageDir -Dir $pkgDirty -Sha $sha -Profile 'DebugSynthetic' -SignerSha256 ''
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgDirty -Profile 'DebugSynthetic' -ReleaseTier 'InternalDebugSynthetic' `
            -SigningMode 'EPHEMERAL_INTERNAL_ONLY' -AllowSyntheticFixturesForUnitTestOnly -RepoRoot $fixture
    } 'Dirty Git working tree rejected.'

    Write-Output "PASS: $assertions assertions in test-stage-qa-release.ps1"
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
    if ($workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
exit 0
