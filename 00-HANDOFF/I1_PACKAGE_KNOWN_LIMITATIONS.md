# Salryn I1 Pre-Beta Package Known Limitations

Decision boundary: controlled browser-package beta proof only.

## Package and launch

- The package is framework-dependent and requires a compatible .NET 10 runtime.
- This is not an MSI or public installer.
- No automatic update or rollback mechanism is included.
- `DESKTOP_APP_SHELL_DEFERRED`: the existing WebView2 host is development-oriented and was not packaged or proven.
- No desktop shortcut is included or proven.
- No packaged Salryn product icon is included or proven. The hosted UI uses the `Salryn POS` product identity and `Salryn by Kirjane Labs` credits.
- Browser launch is the only supported I1 package-proof model.

## Accepted product limitations

- Host-local checkout has no server-side idempotency key.
- Checkout idempotency remains the principal known reliability risk.
- Receipt file writing is post-commit.
- Audit trail is operational evidence, not legal-grade atomic evidence.
- LAN runtime no-restart rebind is deferred.
- Terminal UI/UX is outdated but beta-acceptable.
- Physical terminal proof remains manual.
- Printer hardware and paper-output proof remain manual.
- SKU creation/editing remains deferred.

## Claims not made

- No installer proof.
- No WebView2/app-shell proof.
- No production activation.
- No BIR/eSales readiness, accreditation, submission, or tax-compliance claim.
- No branch promotion.
- No protected-ref movement.

## Operational cautions

- Keep signed license files and customer/store data outside the package folder.
- Do not copy `.salryn-dev`, `ProgramData\Salryn`, `%LocalAppData%\Salryn`, logs, or customer backups into a distributed package.
- Reconcile an uncertain checkout response before retrying because the host-local checkout contract has no server-side idempotency key.
- Treat a successful print-queue response separately from physical paper-output proof.
