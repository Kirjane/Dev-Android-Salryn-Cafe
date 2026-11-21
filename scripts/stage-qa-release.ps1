[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceSha,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [ValidateSet('ReleaseEntitled','DebugSynthetic')][string]$Profile='ReleaseEntitled',
    [string]$RunUrl=''
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$observed=(& git -C $repoRoot rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $observed -ne $SourceSha){throw 'Frozen source SHA mismatch'}
$root=(Resolve-Path -LiteralPath $PackageDirectory).Path
$required=@('Salryn-Cafe-Setup.exe','Salryn-Cafe-QA.apk','Salryn-Cafe-Central-QA.zip')
$files=foreach($name in $required){
    $path=Join-Path $root $name
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing matched QA artifact: $name"}
    $item=Get-Item -LiteralPath $path
    if($item.Length -eq 0){throw "Empty artifact: $name"}
    [ordered]@{file=$name;bytes=$item.Length;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();sourceSha=$SourceSha}
}
Copy-Item -LiteralPath (Join-Path $repoRoot '00-INTEGRATION/QA-SETUP-PAIRING.md') -Destination $root
Copy-Item -LiteralPath (Join-Path $repoRoot '00-INTEGRATION/QA-ACCEPTANCE-31.md') -Destination $root
$manifest=[ordered]@{
    schemaVersion=1;repository='Kirjane/Dev-Salryn-Cafe';sourceSha=$SourceSha
    createdAtUtc=[DateTimeOffset]::UtcNow.ToString('O');workflowRunUrl=$RunUrl
    classification='QA_STAGING_ONLY';releaseCandidateAccepted=$false;productionAuthorized=$false
    profile=$Profile;androidSigning='EPHEMERAL_TEST_KEY';windowsSigning='UNSIGNED_QA'
    licensing=if($Profile -eq 'ReleaseEntitled'){'Signed Release entitlements enforced; authorized QA license required and not included'}else{'Debug-only synthetic environment; no Release entitlement acceptance'}
    topologyStatus='WINDOWS_LOCAL_DISK_COHOST_QA; frozen-SHA integration evidence and Human setup required; no approved endpoint bundled'
    formalNoxApproval='NOT_ASSERTED';humanAcceptance='NOT_RUN';artifacts=@($files)
}
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $root 'QA-RELEASE-MANIFEST.json') -Encoding utf8
$files|ForEach-Object{"$($_.sha256)  $($_.file)"}|Set-Content -LiteralPath (Join-Path $root 'SHA256SUMS.txt') -Encoding ascii
Write-Output "QA staging manifest created for $SourceSha; no release acceptance asserted."
