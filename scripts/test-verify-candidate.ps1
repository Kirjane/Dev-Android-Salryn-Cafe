<# Synthetic regression tests for actual runner functions; no application builds. #>
param([string]$RunnerPath = (Join-Path $PSScriptRoot 'verify-candidate.ps1'))
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($RunnerPath, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw ($errors | Out-String) }
foreach ($function in $ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($function.Extent.Text))
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
Assert ((Get-OverallStatus @()) -eq 'FAIL') 'Empty inventory cannot PASS.'
Assert ((Get-OverallStatus @([pscustomobject]@{status='NOT RUN';kind='TEST';executed=0})) -eq 'FAIL') 'Missing suites cannot PASS.'
Assert ((Get-OverallStatus @([pscustomobject]@{status='BUILD_OK';kind='BUILD';executed=0})) -eq 'FAIL') 'Compile-only cannot PASS as tests.'
Assert ((Get-OverallStatus @([pscustomobject]@{status='PASS';kind='TEST';executed=3})) -eq 'PASS') 'Nonzero test gate accepted.'
Assert-Throws { Assert-NonzeroTests 0 0 0 0 } 'Exit zero with zero tests rejected.'
Assert-Throws { Assert-NonzeroTests 1 4 4 0 } 'Nonzero process exit rejected.'
Assert-Throws { Assert-NonzeroTests 0 4 3 1 } 'Failed tests rejected.'
Assert-Throws { Assert-NonzeroTests 0 1 2 0 } 'Impossible counts rejected.'
Assert-NonzeroTests 0 4 4 0
$windowsSuites = @(Get-RequiredSuitePaths 'Windows')
$portableSuites = @(Get-RequiredSuitePaths 'Portable')
$androidSuites = @(Get-RequiredSuitePaths 'Android')
Assert ($windowsSuites.Count -eq 5) 'Windows inventory includes five known suites.'
Assert ($portableSuites.Count -eq 4) 'Portable inventory includes four known suites.'
Assert ($portableSuites -notcontains 'tests/Salryn.Cafe.Tests/Salryn.Cafe.Tests.csproj') 'WindowsDesktop suite excluded on Linux.'
Assert ($androidSuites.Count -gt 0 -and $androidSuites[0] -eq 'tests/Salryn.Cafe.I6BTests/Salryn.Cafe.I6BTests.csproj') 'Android cannot have empty inventory.'
Assert-Throws { Get-RequiredSuitePaths 'Unknown' } 'Unknown mode rejected.'
Assert-Throws { Assert-SourceUnchanged @{head='a';tree='x'} @{head='b';tree='x';dirty=$false} } 'HEAD drift rejected.'
Assert-Throws { Assert-SourceUnchanged @{head='a';tree='x'} @{head='a';tree='y';dirty=$false} } 'Tree drift rejected.'
Assert-Throws { Assert-SourceUnchanged @{head='a';tree='x'} @{head='a';tree='x';dirty=$true} } 'Dirty final source rejected.'

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('salryn-ci-gate-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$script:repoRoot = $fixture
$script:runDir = Join-Path $fixture 'artifacts/run'
New-Item -ItemType Directory -Path $script:runDir | Out-Null
$script:commands = [Collections.Generic.List[object]]::new()
$script:results = [Collections.Generic.List[object]]::new()
$oldPackage = Join-Path $script:runDir 'old-package'
$newPackage = Join-Path $script:runDir 'new-package'
New-Item -ItemType Directory -Path $oldPackage, $newPackage | Out-Null
'SYNTHETIC_OLD_APK' | Set-Content -LiteralPath (Join-Path $oldPackage 'stale.apk')
Assert-Throws { Get-ProducedApks $newPackage } 'Old APK outside current output cannot satisfy package gate.'
Assert-Throws { Get-ProducedApks (Join-Path $script:runDir 'missing-output') } 'Missing Android output rejected.'
'SYNTHETIC_CURRENT_APK' | Set-Content -LiteralPath (Join-Path $newPackage 'current.apk')
Assert (@(Get-ProducedApks $newPackage).Count -eq 1) 'Only current run output is enumerated.'
'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary><Counters total="0" executed="0" passed="0" failed="0" notExecuted="0" /></ResultSummary></TestRun>' | Set-Content -LiteralPath (Join-Path $fixture 'zero.trx')
$zero = Read-TrxCounts (Join-Path $fixture 'zero.trx')
Assert-Throws { Assert-NonzeroTests 0 $zero.executed $zero.passed $zero.failed } 'Actual zero TRX rejected.'
Assert-Throws { Read-TrxCounts (Join-Path $fixture 'absent.trx') } 'Missing TRX rejected.'
'<TestRun />' | Set-Content -LiteralPath (Join-Path $fixture 'bad.trx')
Assert-Throws { Read-TrxCounts (Join-Path $fixture 'bad.trx') } 'Malformed TRX rejected.'
'<Project><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $fixture 'windows.csproj')
'<Project><ItemGroup><ProjectReference Include="windows.csproj" /></ItemGroup></Project>' | Set-Content -LiteralPath (Join-Path $fixture 'outer.csproj')
Assert-Throws { Assert-PortableProject (Join-Path $fixture 'outer.csproj') ([Collections.Generic.HashSet[string]]::new()) } 'Transitive Windows dependency rejected.'
$exe = (Get-Process -Id $PID).Path
$record = Invoke-RecordedCommand 'synthetic-command' $exe @('-NoProfile','-Command',"Write-Output 'SYNTHETIC_FIXTURE'; exit 0")
Assert ($record.exitCode -eq 0 -and $record.output -match 'SYNTHETIC_FIXTURE') 'Actual command result retained.'
Assert ((Get-Content -LiteralPath $record.log -Raw) -match 'SYNTHETIC_FIXTURE') 'Raw command log retained.'
Assert ($record.logSha256 -eq (Get-FileHash -LiteralPath $record.log -Algorithm SHA256).Hash) 'Raw log hash independently matches.'
Assert ([datetime]$record.startUtc -le [datetime]$record.endUtc) 'Command timestamps retained.'
Assert-Throws { Invoke-RecordedCommand 'synthetic-failure' $exe @('-NoProfile','-Command','exit 7') } 'Failed command rejects gate.'
Invoke-Gate 'missing-required-suite' 'TEST' { param($r) throw 'Required suite missing.' }
Assert ((Get-OverallStatus $results.ToArray()) -eq 'FAIL') 'Gate wrapper fails missing required suite.'

& git -C $fixture init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Fixture git init failed.' }
& git -C $fixture config user.name 'Synthetic Gate Test'
& git -C $fixture config user.email 'synthetic@example.invalid'
& git -C $fixture config core.autocrlf false
'artifacts/' | Set-Content -LiteralPath (Join-Path $fixture '.gitignore')
& git -C $fixture add -- .gitignore zero.trx bad.trx windows.csproj outer.csproj
& git -C $fixture commit --quiet -m 'synthetic fixture'
if ($LASTEXITCODE -ne 0) { throw 'Fixture commit failed.' }
$source = Get-SourceIdentity $fixture
Assert ($source.head -match '^[a-f0-9]{40}$' -and $source.tree -match '^[a-f0-9]{40}$') 'Clean source yields real HEAD/tree.'
'changed' | Add-Content -LiteralPath (Join-Path $fixture 'zero.trx')
Assert-Throws { Get-SourceIdentity $fixture } 'Dirty source cannot receive exact-SHA evidence.'
Assert-Throws { Get-SourceIdentity (Join-Path $fixture 'absent-repository') } 'Git failure cannot yield unknown-SHA PASS.'

# MOCKED_RUNNER only: execute the actual script end to end with synthetic tool
# outputs. This verifies orchestration/report binding, not application tests/APKs.
$fixtureScripts = Join-Path $fixture 'scripts'
$fixtureApp = Join-Path $fixture 'src/Salryn.App.Android'
$fixtureTests = Join-Path $fixture 'tests/Salryn.Cafe.I6BTests'
New-Item -ItemType Directory -Path $fixtureScripts, $fixtureApp, $fixtureTests | Out-Null
Copy-Item -LiteralPath $RunnerPath -Destination (Join-Path $fixtureScripts 'verify-candidate.ps1')
'<Project><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $fixtureApp 'Salryn.App.Android.csproj')
'<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $fixtureTests 'Salryn.Cafe.I6BTests.csproj')
& git -C $fixture add -- zero.trx scripts/verify-candidate.ps1 src/Salryn.App.Android/Salryn.App.Android.csproj tests/Salryn.Cafe.I6BTests/Salryn.Cafe.I6BTests.csproj
& git -C $fixture commit --quiet -m 'synthetic runner orchestration fixture'
if ($LASTEXITCODE -ne 0) { throw 'Fixture orchestration commit failed.' }
function dotnet {
    $global:LASTEXITCODE = 0
    $arguments = @($args)
    switch ($arguments[0]) {
        '--info' { Write-Output 'MOCKED_RUNNER dotnet fixture, not a real application test'; return }
        'test' {
            $resultsIndex = [array]::IndexOf($arguments, '--results-directory')
            $loggerIndex = [array]::IndexOf($arguments, '--logger')
            $name = $arguments[$loggerIndex + 1].Split('=')[1]
            $path = Join-Path $arguments[$resultsIndex + 1] $name
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary><Counters total="2" executed="2" passed="2" failed="0" notExecuted="0" /></ResultSummary></TestRun>' | Set-Content -LiteralPath $path
            Write-Output 'MOCKED_RUNNER: synthetic TRX'; return
        }
        'publish' {
            $outputIndex = [array]::IndexOf($arguments, '--output')
            $path = $arguments[$outputIndex + 1]
            New-Item -ItemType Directory -Path $path | Out-Null
            'MOCKED_RUNNER_NOT_INSTALLABLE' | Set-Content -LiteralPath (Join-Path $path 'synthetic.apk')
            Write-Output 'MOCKED_RUNNER: synthetic package'; return
        }
        default { throw 'Unexpected mocked command.' }
    }
}
& (Join-Path $fixtureScripts 'verify-candidate.ps1') -Mode Android
Assert ($LASTEXITCODE -eq 0) 'Actual runner completes synthetic orchestration.'
$summaryFile = Get-ChildItem -LiteralPath (Join-Path $fixture 'artifacts/verification') -Recurse -Filter 'verification-summary.json' | Select-Object -First 1
$summary = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json
Assert ($summary.overallStatus -eq 'PASS' -and $summary.mode -eq 'Android') 'Scoped synthetic report reflects completed gates.'
Assert ($summary.commitSha -eq (Get-SourceIdentity $fixture).head) 'Report binds exact fixture commit.'
Assert ($summary.artifacts.Count -eq 1 -and $summary.artifacts[0].runId -eq $summary.runId) 'Only run-produced package included.'
Assert ($summary.commands.Count -eq 3 -and @($summary.commands | Where-Object { -not (Test-Path -LiteralPath $_.log) }).Count -eq 0) 'All actual orchestration command logs retained.'

$suitePath = Join-Path $fixtureTests 'Salryn.Cafe.I6BTests.csproj'
Move-Item -LiteralPath $suitePath -Destination ($suitePath + '.preserved')
& git -C $fixture add -- tests/Salryn.Cafe.I6BTests/Salryn.Cafe.I6BTests.csproj tests/Salryn.Cafe.I6BTests/Salryn.Cafe.I6BTests.csproj.preserved
& git -C $fixture commit --quiet -m 'synthetic missing mobile suite fixture'
if ($LASTEXITCODE -ne 0) { throw 'Fixture missing-suite commit failed.' }
& (Join-Path $fixtureScripts 'verify-candidate.ps1') -Mode Android
Assert ($LASTEXITCODE -eq 1) 'Actual runner blocks missing mobile suite despite successful synthetic APK build.'

$appPath = Join-Path $fixtureApp 'Salryn.App.Android.csproj'
Move-Item -LiteralPath $appPath -Destination ($appPath + '.preserved')
& git -C $fixture add -- src/Salryn.App.Android/Salryn.App.Android.csproj src/Salryn.App.Android/Salryn.App.Android.csproj.preserved
& git -C $fixture commit --quiet -m 'synthetic missing Android app fixture'
if ($LASTEXITCODE -ne 0) { throw 'Fixture missing-app commit failed.' }
& (Join-Path $fixtureScripts 'verify-candidate.ps1') -Mode Android
Assert ($LASTEXITCODE -eq 1) 'Actual runner blocks missing Android app.'
Remove-Item Function:dotnet
Write-Host "PASS: $assertions assertions; synthetic fixtures retained at $fixture"
