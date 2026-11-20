# Salryn Cafe Windows Installer (`Salryn-Cafe-Setup.exe`)

Independent side-by-side Inno Setup 6 packaging for **Salryn Cafe** (`ProductId = Salryn.Cafe`).

## Isolation Guarantees (ADR-I1 / Issue #3)
- **Install Dir:** `C:\Program Files\Kirjane Information Technology Services - Cafe` *(blocks installing inside protected Retail `C:\Program Files\Kirjane Information Technology Services`)*
- **Shared Data Root:** `C:\ProgramData\SalrynCafe` (`data/salryn-cafe.db`, `backups`, `config`, `exports`, `logs`, `receipts`; preserved across upgrade/uninstall)
- **Per-User Root:** `%LocalAppData%\SalrynCafe` (`Licensing`, `WebView2`, `Reports/NativePdf`)
- **Ports:** `127.0.0.1:5399` (loopback desktop host), `:5400` (opt-in LAN HTTPS)
- **Inno AppId GUID:** `{8F4C2E19-6B3A-4D7E-9A51-3C8E7F2B4D60}` *(distinct from Retail `{A4E2B8C1-7D3F-4A91-9C2E-5F6A8B1C0D3E}`)*
- **OS Mutexes:** `Global\Kirjane.SalrynCafe.Desktop`, `Global\Kirjane.SalrynCafe.LocalApi`

## Build Command
```powershell
.\installer\Build-SalrynCafeInstaller.ps1 -Configuration Release
```
Produces `artifacts\installer\setup\Salryn-Cafe-Setup.exe` and prints its SHA-256 digest.
