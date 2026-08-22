# Pre-LAN Hardening Closure

Status: **PRE-LAN HARDENING CLOSED** on `KIRCH-FREEHAND`.

## Sprint Completion

| Sprint | Status | Summary |
|--------|--------|---------|
| PRELAN-HARDEN-1 | Complete | Product tax classification persisted end-to-end; official checkout uses real classification; `CheckStatus` read-only |
| PRELAN-HARDEN-2 | Complete | Engineering evidence wired into trusted text-file export; enriched Z-reading payload persisted |
| PRELAN-HARDEN-3 | Complete | Official document lifecycle, X/Z reading, and evidence export operator surfaces with permission gates |
| PRELAN-HARDEN-4 | Complete | Verification hygiene documented; NU1903 remediated; MSB3277 formally closed |

## Warning Status (PRELAN-HARDEN-4)

### NU1903 — SQLitePCLRaw.lib.e_sqlite3

**Resolved.**

`Directory.Build.props` adds `SQLitePCLRaw.bundle_e_sqlite3` `3.0.3`, overriding the vulnerable transitive `2.1.11` stack from `Microsoft.Data.Sqlite`. Runtime native SQLite is supplied by `SourceGear.sqlite3` `3.50.4.5` (GHSA-2m69-gcr7-jv3q / CVE-2025-6965).

### MSB3277 — WindowsBase 4.0 vs 5.0 in Salryn.Desktop.WebView

**Accepted and formally closed.**

`Salryn.Desktop.WebView` is a standalone WEB-A4 native dev host using `Microsoft.Web.WebView2`. The package ships a WPF companion assembly that references `WindowsBase` 5.0 while the .NET SDK provides 4.0 for WinForms-only hosts. MSBuild reports MSB3277; runtime assembly unification is safe for this isolated host.

Closure action: project-level `NoWarn` for `MSB3277` with documented reason in `Salryn.Desktop.WebView.csproj`. No WebView rewrite; no shell behavior change.

## Verification Targets

Salryn guard execution is intentionally Windows-targeted. See `VERIFICATION_TARGETS.md`.

## LAN Doctrine (unchanged)

- Offline-first, LAN-ready architecture remains documented.
- **Store Host owns database access.**
- Clients must not open shared SQLite directly.
- **No LAN implementation has started** in pre-LAN hardening.

## Next Step

LAN work may begin from the Store Host doctrine when promoted and scheduled. Pre-LAN hardening does not include:

- installer role wizard
- host/client network transport
- pairing
- Salryn Store page
- shared SQLite over network
