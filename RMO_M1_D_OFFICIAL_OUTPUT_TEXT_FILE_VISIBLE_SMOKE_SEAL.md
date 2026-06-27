# RMO-M1-D Official Output Text File Visible Smoke Seal

## Status

RMO-M1-D is sealed as a dev/manual smoke milestone for the visible Official Output Text File flow.

Completed and pushed commits:

- `5ee4d620c00db40d8d43c0b779278fe017a9e8b6` - Add official output text file dev candidate smoke
- `e0518a41cf9db3b7e33d51a3026b7b14a1618f91` - Fix official output text file run button layout

## Dev Candidate Fixture

Command:

```powershell
dotnet run --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- official-output-text-file-dev-candidate
```

Candidate identity:

- Machine profile id: `90000000-0000-0000-0000-000000000001`
- Registration scope: `DEV-SMOKE`
- Official document number: `ORDEV-2026-000001`
- Document kind: `OFFICIAL_RECEIPT`
- Journal entry id: `637e40bf-4e68-4bd4-aefd-d340af1780cb`
- Sale id: `68a4cb40-77d0-4053-b01d-70b3b19d0e9f`
- Terminal id: `DEV-SMOKE-POS`

## Manual UI Smoke

- Login succeeded with `admin / StrongPass123!`.
- The `Official Output Text File` module was visible.
- `DEV-SMOKE` loaded 1 official evidence-backed candidate.
- Blank output root was blocked.
- Run executed once after an explicit output root was supplied.
- UI reported workflow completion with a `salryn://official-external-output-workflow/...` reference.
- Controlled text-file output was written under `C:\Users\kirch\Desktop\SalrynOfficialOutputSmoke\official-output\...`.
- Output included `manifest.txt`, `package.txt`, `ledger.txt`, `dispatch-plan.txt`, `execution-receipt.txt`, and one receipt/invoice payload text file.

## Verification

Passed:

- `git diff --check`
- `dotnet build .\Salryn.sln --no-restore`
- `dotnet test .\Salryn.sln --no-build`
- `CoreSmoke`
- `CheckoutLocalHostSmoke`
- `InfrastructureSmoke -- official-output-text-file`

Known warning left untouched:

- `SQLitePCLRaw.lib.e_sqlite3` NU1903

## Boundaries

This proves the controlled text-file official output path only. It does not claim RMO/BIR certification, does not replace legal or accreditation review, and does not add PDF, Crystal, printing, or ESC/POS. The candidate source uses a dev/manual smoke fixture.
