# DEV ONLY: archives the current .salryn-dev SQLite database and reseeds known dev users.
# Refuses to run against production ProgramData paths.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $repoRoot "Salryn.sln"))) {
    throw "Could not locate Salryn.sln from $repoRoot"
}

Push-Location $repoRoot
try {
    dotnet run --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- flow-reset-dev-seed
    if ($LASTEXITCODE -ne 0) {
        throw "flow-reset-dev-seed failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}