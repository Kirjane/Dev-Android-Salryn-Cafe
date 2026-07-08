# DEV ONLY: start Salryn.Api.Local with explicit localhost + LAN bind.
# Rejects wildcard/0.0.0.0 and requires the bind IP to exist on a local NIC.

param(
    [Parameter(Mandatory = $true)]
    [string]$BindIp,

    [int]$Port = 5299,

    [switch]$StopStale
)

$ErrorActionPreference = "Stop"

function Test-ForbiddenBindAddress {
    param([string]$Address)
    $normalized = $Address.Trim()
    return ($normalized -eq "0.0.0.0" -or $normalized -eq "*" -or $normalized -eq "::" -or $normalized -eq "[::]")
}

function Test-LocalInterfaceAddress {
    param([string]$Address)
    $matches = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -eq $Address -and $_.PrefixOrigin -ne "WellKnown" }
    return [bool]$matches
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $repoRoot "Salryn.sln"))) {
    throw "Could not locate Salryn.sln from $repoRoot"
}

if (Test-ForbiddenBindAddress $BindIp) {
    throw "Wildcard bind is forbidden. Use an explicit LAN IPv4 address assigned to this host."
}

if (-not (Test-LocalInterfaceAddress $BindIp)) {
    throw "Bind IP $BindIp is not assigned to a local network interface on this host."
}

if ($StopStale) {
    Get-Process -Name "Salryn.Api.Local" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
} elseif (Get-Process -Name "Salryn.Api.Local" -ErrorAction SilentlyContinue) {
    Write-Warning "Salryn.Api.Local is already running. Stop it first or rerun with -StopStale."
}

$env:SALRYN_LAN_BIND_IP = $BindIp
$env:SALRYN_PORT = "$Port"

Push-Location $repoRoot
try {
    Write-Host "Starting Salryn.Api.Local with explicit LAN bind..."
    Write-Host "Host URL:       http://127.0.0.1:$Port"
    Write-Host "LAN Client URL: http://${BindIp}:$Port/remote-client"
    dotnet run --project .\src\Salryn.Api.Local\Salryn.Api.Local.csproj -- --dev --lan-bind $BindIp --port $Port
    if ($LASTEXITCODE -ne 0) {
        throw "Salryn.Api.Local failed with exit code $LASTEXITCODE"
    }
}
finally {
    Remove-Item Env:\SALRYN_LAN_BIND_IP -ErrorAction SilentlyContinue
    Remove-Item Env:\SALRYN_PORT -ErrorAction SilentlyContinue
    Pop-Location
}