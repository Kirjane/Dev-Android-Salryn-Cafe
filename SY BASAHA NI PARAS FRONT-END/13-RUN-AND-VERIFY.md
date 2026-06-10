# 13 - Run And Verify

Set root:

    $Root = Join-Path ([Environment]::GetFolderPath("Desktop")) "POS\Salryn"
    Set-Location $Root

Build:

    dotnet build "Salryn.sln"

Expected:

    Build succeeded

Focused frontend smoke:

    dotnet run --project "tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" -- pos-checkout-ui-presenter-viewmodel

Expected:

    POS checkout UI presenter / viewmodel smoke checks succeeded.

Full smoke:

    dotnet run --project "tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" -- full

Expected:

    FULL SMOKE TEST PASSED AND CLEANED UP.

DB / SQL check:

    Get-ChildItem -Path $Root -Recurse -File |
      Where-Object {
        $_.Extension -in ".db", ".sqlite", ".sql"
      } |
      Select-Object FullName

Expected:

    No output

Git status:

    git status --short

Unexpected files:

    *.db
    *.sqlite
    *.sql
    src/Salryn.Infrastructure/Data/Migrations/...
