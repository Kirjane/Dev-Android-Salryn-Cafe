# 13 - Run Checks

Set repo root:

    $Root = Join-Path ([Environment]::GetFolderPath("Desktop")) "POS\Salryn"
    Set-Location $Root

Build:

    dotnet build "Salryn.sln"

Expected:

    Build succeeded

Focused UI smoke:

    dotnet run --project "tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" -- pos-checkout-ui-presenter-viewmodel

Expected:

    POS checkout UI presenter / viewmodel smoke checks succeeded.

Full smoke:

    dotnet run --project "tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" -- full

Expected:

    FULL SMOKE TEST PASSED AND CLEANED UP.

Check DB or SQL files:

    Get-ChildItem -Path $Root -Recurse -File |
      Where-Object {
        $_.Extension -in ".db", ".sqlite", ".sql"
      } |
      Select-Object FullName

Expected:

    no output

Git status:

    git status --short

Okay files for this handoff:

    SY BASAHA NI PARAS FRONT-END/...
    00-HANDOFF/...

Not okay unless expected:

    *.db
    *.sqlite
    *.sql
    src/Salryn.Infrastructure/Data/Migrations/...
