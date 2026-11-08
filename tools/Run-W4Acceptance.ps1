[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipFrontendInstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputDirectory) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputDirectory = Join-Path $repoRoot ".salryn-dev\w4-acceptance-$stamp"
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$logs = Join-Path $outputRoot 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
$branch = (& git -C $repoRoot branch --show-current).Trim()
$started = [DateTimeOffset]::Now
$results = [System.Collections.Generic.List[object]]::new()

function Invoke-W4Gate {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [bool]$Critical = $true
    )

    $safeName = $Name -replace '[^a-zA-Z0-9._-]', '-'
    $commandPath = Join-Path $logs "$safeName.command.txt"
    $logPath = Join-Path $logs "$safeName.log"
    $exitPath = Join-Path $logs "$safeName.exit.txt"
    $Command | Out-File -LiteralPath $commandPath -Encoding utf8
    $gateStarted = [DateTimeOffset]::Now
    Write-Host "[W4] RUN $Name"
    Push-Location $WorkingDirectory
    try {
        $output = & powershell -NoProfile -Command $Command 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $output | Out-File -LiteralPath $logPath -Encoding utf8
    $exitCode | Out-File -LiteralPath $exitPath -Encoding ascii
    $result = [ordered]@{
        name = $Name
        command = $Command
        workingDirectory = $WorkingDirectory
        critical = $Critical
        exitCode = $exitCode
        status = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
        startedAt = $gateStarted.ToString('O')
        completedAt = [DateTimeOffset]::Now.ToString('O')
        log = "logs/$safeName.log"
    }
    $results.Add([pscustomobject]$result)
    Write-Host "[W4] $($result.status) $Name (exit $exitCode)"
    if ($Critical -and $exitCode -ne 0) {
        Write-Summary
        throw "Critical W4 gate failed: $Name (exit $exitCode). See $logPath"
    }
}

function Write-Summary {
    $summary = [ordered]@{
        schema = 'salryn-w4-acceptance-summary-v1'
        exactHead = $head
        branch = $branch
        startedAt = $started.ToString('O')
        completedAt = [DateTimeOffset]::Now.ToString('O')
        licensingOpsCanonicalProof = 'NOT_RUN_LAST_GATE'
        physicalDrawerAcceptance = 'BLOCKED_HUMAN_PHYSICAL'
        financialPolicyApproval = 'BLOCKED_KIRCH_APPROVAL'
        results = @($results)
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputRoot 'summary.json') -Encoding utf8
}

$core = 'tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj'
try {
    Invoke-W4Gate 'dotnet-restore' 'dotnet restore Salryn.sln' $repoRoot
    Invoke-W4Gate 'release-build' 'dotnet build Salryn.sln -c Release --no-restore' $repoRoot
    foreach ($check in @(
        'w4-cash-drawer-reconciliation',
        'v2-host-shift',
        'v2-host-checkout',
        'u1-accounts-receivable',
        'u1-entitlement-gating',
        'owner-analytics-w0-w3',
        'pos-r1-owner-reports',
        'pre-beta-d-portable-backup-import-export')) {
        Invoke-W4Gate "coresmoke-$check" "dotnet run --project $core -c Release --no-build -- --only $check" $repoRoot
    }
    Invoke-W4Gate 'infrastructure-cashier-session-edge' 'dotnet run --project tools/Salryn.Tools.InfrastructureSmoke/Salryn.Tools.InfrastructureSmoke.csproj -c Release --no-build -- cashier-session-service-edge' $repoRoot
    if (-not $SkipFrontendInstall) {
        Invoke-W4Gate 'npm-ci' 'npm ci' (Join-Path $repoRoot 'src\Salryn.App.Web')
    }
    Invoke-W4Gate 'npm-build' 'npm run build' (Join-Path $repoRoot 'src\Salryn.App.Web')
    Invoke-W4Gate 'npm-audit' 'npm audit --audit-level=high' (Join-Path $repoRoot 'src\Salryn.App.Web')
    Invoke-W4Gate 'nuget-vulnerability-scan' 'dotnet list Salryn.sln package --vulnerable --include-transitive' $repoRoot
    Invoke-W4Gate 'git-diff-check' 'git diff --check' $repoRoot
    Write-Summary
}
catch {
    Write-Summary
    throw
}

Write-Host "[W4] Acceptance run complete: $outputRoot"
Write-Host "[W4] Exact HEAD: $head"
