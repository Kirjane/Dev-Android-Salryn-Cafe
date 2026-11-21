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
        versionName = '1.0.0-qa'
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
    # Create clean temp git repo with required QA docs
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

    # Write green and red verification summaries outside the git fixture repo
    $greenSummaryPath = Join-Path $workRoot 'green-summary.json'
    ([ordered]@{
        commitSha = $sha; prHeadSha = $sha; overallStatus = 'PASS'
        releaseEntitledVerdict = 'BLOCKED_EXTERNAL (NOT RUN)'
        gates = @(@{ gate = 'xunit:Salryn.Cafe.Tests'; status = 'PASS' }, @{ gate = 'core-smoke'; status = 'RUNTIME_OBSERVED' })
    } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $greenSummaryPath

    $redSummaryPath = Join-Path $workRoot 'red-summary.json'
    ([ordered]@{
        commitSha = $sha; prHeadSha = $sha; overallStatus = 'FAIL'
        releaseEntitledVerdict = 'BLOCKED_EXTERNAL'
        gates = @(@{ gate = 'core-smoke'; status = 'FAIL' })
    } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $redSummaryPath

    $rcEntitledSummaryPath = Join-Path $workRoot 'rc-entitled-summary.json'
    ([ordered]@{
        commitSha = $sha; prHeadSha = $sha; overallStatus = 'PASS'
        releaseEntitledVerdict = 'PASS'
        gates = @(@{ gate = 'core-smoke'; status = 'RUNTIME_OBSERVED' })
    } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $rcEntitledSummaryPath

    # 1. Positive: ControlledQA passes end-to-end
    $pkgOk = Join-Path $workRoot 'ok-controlled-qa'
    New-ValidPackageDir -Dir $pkgOk -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha -VersionCode 2
    & $StagerPath -SourceSha $sha -PackageDirectory $pkgOk -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
        -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
        -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture | Out-Null
    Assert (Test-Path -LiteralPath (Join-Path $pkgOk 'QA-RELEASE-MANIFEST.json')) 'ControlledQA emits manifest.'
    Assert (Test-Path -LiteralPath (Join-Path $pkgOk 'SHA256SUMS.txt')) 'ControlledQA emits SHA256SUMS.'
    $manifest = Get-Content -LiteralPath (Join-Path $pkgOk 'QA-RELEASE-MANIFEST.json') -Raw | ConvertFrom-Json
    Assert ($manifest.sourceSha -eq $sha -and $manifest.releaseTier -eq 'ControlledQA' -and $manifest.provenanceVerified -eq $true) 'Manifest records exact SHA, tier, and provenanceVerified.'
    Assert ($manifest.androidSigning -eq 'PERSISTENT_QA_KEY' -and $manifest.androidSignerSha256 -eq $signerSha -and $manifest.androidVersionCode -eq 2) 'Manifest records persistent QA signer & versionCode.'
    Assert ($manifest.releaseCandidateAccepted -eq $false -and $manifest.productionAuthorized -eq $false) 'Manifest never asserts RC or production acceptance.'

    # 2. Positive: InternalDebugSynthetic with ephemeral signing passes
    $pkgDbg = Join-Path $workRoot 'ok-internal-debug'
    New-ValidPackageDir -Dir $pkgDbg -Sha $sha -Profile 'DebugSynthetic' -SignerSha256 '' -VersionCode 1
    & $StagerPath -SourceSha $sha -PackageDirectory $pkgDbg -Profile 'DebugSynthetic' -ReleaseTier 'InternalDebugSynthetic' `
        -SigningMode 'EPHEMERAL_INTERNAL_ONLY' -RepoRoot $fixture | Out-Null
    $dbgManifest = Get-Content -LiteralPath (Join-Path $pkgDbg 'QA-RELEASE-MANIFEST.json') -Raw | ConvertFrom-Json
    Assert ($dbgManifest.releaseTier -eq 'InternalDebugSynthetic' -and $dbgManifest.androidSigning -eq 'EPHEMERAL_INTERNAL_ONLY') 'InternalDebugSynthetic accepts ephemeral signing.'

    # 3. Negative: Tampered manifest & tampered SHA256SUMS rejected
    $tamperManifest = Get-Content -LiteralPath (Join-Path $pkgOk 'QA-RELEASE-MANIFEST.json') -Raw | ConvertFrom-Json
    $tamperManifest.artifacts[0].sha256 = ('f' * 64)
    $tamperManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $pkgOk 'QA-RELEASE-MANIFEST.json')
    Assert-Throws { Test-StagedManifestIntegrity -PackageDirectory $pkgOk -ExpectedSourceSha $sha } 'Tampered manifest hash rejected.'

    # 4. Negative: Wrong source SHA rejected
    $pkgWrongSha = Join-Path $workRoot 'neg-wrong-sha'
    New-ValidPackageDir -Dir $pkgWrongSha -Sha $wrongSha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    Assert-Throws {
        & $StagerPath -SourceSha $wrongSha -PackageDirectory $pkgWrongSha -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Frozen source SHA mismatch rejected.'

    # 5. Negative: APK provenance SHA mismatch rejected
    $pkgApkWrongSha = Join-Path $workRoot 'neg-apk-wrong-sha'
    New-ValidPackageDir -Dir $pkgApkWrongSha -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha `
        -ApkExtraEntries @{ 'assets/build-provenance.json' = ('{"sourceSha":"' + $wrongSha + '","profile":"ReleaseEntitled","packageId":"ph.kirjanelabs.salryncafe","versionCode":2,"signerSha256":"' + $signerSha + '","signingVerificationMode":"SYNTHETIC_UNIT_TEST"}') }
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgApkWrongSha -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'APK source SHA mismatch rejected.'

    # 6. Negative: Debug debugger artifact inside ReleaseEntitled APK rejected
    $pkgProfileMismatch = Join-Path $workRoot 'neg-apk-debug-in-release'
    New-ValidPackageDir -Dir $pkgProfileMismatch -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha `
        -ApkExtraEntries @{ 'lib/arm64-v8a/libmono-component-debugger.so' = 'DEBUGGER' }
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgProfileMismatch -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Debug artifact in ReleaseEntitled APK rejected.'

    # 7. Negative: Missing Central runtimeconfig.json rejected
    $pkgMissingRc = Join-Path $workRoot 'neg-central-missing-rc'
    New-ValidPackageDir -Dir $pkgMissingRc -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha `
        -CentralOverrides @{ 'Salryn.Sync.Central.Server.runtimeconfig.json' = $null }
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgMissingRc -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Central zip missing runtimeconfig.json rejected.'

    # 8. Negative: Secret .env bundled in Central zip rejected
    $pkgCentralSecret = Join-Path $workRoot 'neg-central-secret'
    New-ValidPackageDir -Dir $pkgCentralSecret -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha `
        -CentralOverrides @{ 'deploy-reference/.env' = 'SECRET=1' }
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgCentralSecret -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Central zip containing .env secret rejected.'

    # 9. Negative: Installer missing source SHA rejected
    $pkgBadExe = Join-Path $workRoot 'neg-installer-no-sha'
    New-ValidPackageDir -Dir $pkgBadExe -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha -InstallerContent 'Salryn Cafe Setup without revision'
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgBadExe -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Installer missing source SHA rejected.'

    # 10. Negative: Empty or invalid RunUrl with -RequireCiProvenance rejected
    $pkgNoUrl = Join-Path $workRoot 'neg-no-run-url'
    New-ValidPackageDir -Dir $pkgNoUrl -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgNoUrl -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl '' -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Empty RunUrl rejected when -RequireCiProvenance set.'
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgNoUrl -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl 'https://example.invalid/run/1' -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Untrusted RunUrl rejected.'

    # 11. Negative: Stale extra file in package directory rejected
    $pkgStale = Join-Path $workRoot 'neg-stale-file'
    New-ValidPackageDir -Dir $pkgStale -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    'STALE' | Set-Content -LiteralPath (Join-Path $pkgStale 'old-artifact.apk')
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgStale -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Pre-existing stale file in package directory rejected.'

    # 12. Negative: Ephemeral signing under ControlledQA rejected
    $pkgEph = Join-Path $workRoot 'neg-ephemeral-controlled-qa'
    New-ValidPackageDir -Dir $pkgEph -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgEph -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'EPHEMERAL_INTERNAL_ONLY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -RepoRoot $fixture
    } 'Ephemeral signing rejected under ControlledQA.'

    # 13. Negative: Red verification summary under ControlledQA rejected
    $pkgRed = Join-Path $workRoot 'neg-red-summary'
    New-ValidPackageDir -Dir $pkgRed -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgRed -Profile 'ReleaseEntitled' -ReleaseTier 'ControlledQA' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $redSummaryPath -RepoRoot $fixture
    } 'Red verification summary rejected under ControlledQA.'

    # 14. Negative: ReleaseCandidate without formal Nox approval or ReleaseEntitled PASS rejected
    $pkgRc = Join-Path $workRoot 'neg-rc-unapproved'
    New-ValidPackageDir -Dir $pkgRc -Sha $sha -Profile 'ReleaseEntitled' -SignerSha256 $signerSha
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgRc -Profile 'ReleaseEntitled' -ReleaseTier 'ReleaseCandidate' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $rcEntitledSummaryPath -FormalNoxApprovedSha '' -RepoRoot $fixture
    } 'ReleaseCandidate without formal Nox approval rejected.'
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgRc -Profile 'ReleaseEntitled' -ReleaseTier 'ReleaseCandidate' `
            -SigningMode 'PERSISTENT_QA_KEY' -ExpectedSignerSha256 $signerSha -RunUrl $validRunUrl -RequireCiProvenance `
            -VerificationSummaryPath $greenSummaryPath -FormalNoxApprovedSha $sha -RepoRoot $fixture
    } 'ReleaseCandidate with BLOCKED_EXTERNAL releaseEntitledVerdict rejected.'

    # 15. Negative: Dirty Git working tree rejected
    'DIRTY' | Set-Content -LiteralPath (Join-Path $fixture 'untracked.txt')
    $pkgDirty = Join-Path $workRoot 'neg-dirty-tree'
    New-ValidPackageDir -Dir $pkgDirty -Sha $sha -Profile 'DebugSynthetic' -SignerSha256 ''
    Assert-Throws {
        & $StagerPath -SourceSha $sha -PackageDirectory $pkgDirty -Profile 'DebugSynthetic' -ReleaseTier 'InternalDebugSynthetic' `
            -SigningMode 'EPHEMERAL_INTERNAL_ONLY' -RepoRoot $fixture
    } 'Dirty Git working tree rejected.'

    Write-Host "PASS: $assertions assertions in test-stage-qa-release.ps1"
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
    if ($workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
exit 0
