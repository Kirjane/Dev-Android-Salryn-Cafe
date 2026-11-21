# Salryn Cafe QA setup and pairing

QA STAGING ONLY. Use disposable Windows/Android/Central environments and synthetic Cafe data. Preserve Retail installation/data. Record manifest full source SHA and compare SHA256SUMS before installation. Never mix APK/installer/Central ZIP from different manifests.

## Package and runtime requirements

Bundle: Salryn-Cafe-Setup.exe, Salryn-Cafe-QA.apk, Salryn-Cafe-Central-QA.zip, SHA256SUMS.txt, QA-RELEASE-MANIFEST.json and the31-case checklist. Windows requires x64 Windows, .NET10 desktop/ASP.NET runtimes and WebView2; Android requires API24+ arm64 or x86_64. QA Android signing is ephemeral per run: preserve synthetic evidence before replacing an older differently signed QA install. Never replace a production cashier with QA.

Verify each package using Get-FileHash -Algorithm SHA256 and compare with the manifest. Build success is separate from installed runtime acceptance.

## Actual topology and open gate

Windows Desktop -> loopback Salryn.Api.Local on127.0.0.1:5399 -> LOCAL DISK Cafe SQLite.
Central on the SAME Windows QA machine -> the EXACT SAME LOCAL DISK Cafe SQLite.
Android -> approved HTTPS Central origin -> that single canonical SQLite authority.

This is an explicit cohost QA profile, not independent Host/Central replication. Never use a UNC/network SQLite path. Windows sync invokes authenticated Central ingestion of its retained local outbox evidence; Central must verify existing original facts before emitting Applied/feed, never apply money twice. Android writes through canonical Central application. Host sees those same committed rows directly; it does not bootstrap over local data or replay a second projection.

Salryn.Host.Local is a library, not the executable Host. The real Cafe React workstation is /cafe/, not the older root shell. Installer must contain both Host and Desktop executables and cafe-dist/index.html. Human QA must confirm installed Desktop opens the live Cafe workstation and real guarded Host routes.

CODE-CONTROLLED GATE: cohost source is implemented but requires the frozen-SHA integration evidence in the manifest/acceptance matrix before paired acceptance. The Host connector permits only HTTPS loopback Central with health instance/epoch and tenant/store scope matching its own canonical SQLite tables. Windows device binding requires zero prior drawer/domain/outbox operations. It refuses existing evidence instead of rewriting IDs. If this gate is not passed, artifact staging is not a paired handoff. General remote Central and independently persisted Windows Host remain unsupported by this restricted profile; C1-C11 final scope is unchanged.

No approved Central endpoint, deployment, DNS, TLS private key, license or live credentials are bundled. Central ZIP is staging output, not a running service.

## Central QA provisioning requirements

Provision a disposable machine/account with .NET10 ASP.NET runtime and private writable data/evidence roots. Configure CentralSync:EnableCanonicalDomainProcessing=true, nonempty CanonicalTenantId/CanonicalStoreId, a Cafe-lineage synthetic canonical database, genuine published menu/recipes and ingredient stock, active synthetic users/roles/customers and bootstrap administrator credentials through private target configuration. Empty transport-only defaults are insufficient. Never reuse customer data or production signing material.

Serve HTTPS with a valid certificate for the Android-reachable hostname. Release Android trusts system certificate authorities; DebugSynthetic additionally trusts an explicitly installed user CA. Keep RequireHttps=true and TLS validation enabled. If reverse proxy TLS is used, allow only the observed proxy peer and reject spoofed forwarding from other peers. Emulator10.0.2.2 addresses its host; physical devices need reachable DNS/IP and valid TLS, not127.0.0.1 or10.0.2.2.

Deliver Central origin, tenant/store GUIDs and the independently approved lease-signing SPKI public-key pin through a private QA setup record. Do not learn trust from an unauthenticated device response. The source fixture tools/Salryn.Tools.AndroidLiveAcceptance creates disposable synthetic authority and private markers; it is not a production deployment utility and final integrated execution still needs qualification.


## Cohost startup order (disposable Windows only)

1. Install the matched Windows package and extract Central ZIP on the SAME QA Windows machine. Use a clean disposable Cafe data directory only; do not delete an existing installation/data to manufacture freshness. Before opening a shift or creating any domain operation, complete legitimate Host first-admin setup using its real license/bootstrap flow.
2. Read that synthetic deployment's store GUID from its setup/admin evidence (or an administrator read-only SQLite query of deployment_identity.store_id). Generate one QA tenant GUID, record both privately, and keep them unchanged. A mismatched StoreId fails closed; never UPDATE identity to make a mismatched database pass.
3. Provision an administrator-approved TLS certificate whose SAN includes localhost AND the Android-reachable QA DNS name/IP. Host connector uses https://localhost:5443; physical Android uses the reachable certified name at the same service. Certificate/key and any PFX password remain outside Git. Add explicit firewall access only on the approved QA interface. No certificate-validation callback is allowed.
4. In a private PowerShell process launch Central with the absolute SAME database path used by Host. Example values below are placeholders; replace only with approved synthetic setup values:

```powershell
$env:SALRYN_CAFE_CENTRAL_DB_PATH = 'C:\ProgramData\SalrynCafe\data\salryn-cafe.db'
$env:CentralSync__EnableCanonicalDomainProcessing = 'true'
$env:CentralSync__CanonicalTenantId = '<QA-TENANT-GUID>'
$env:CentralSync__CanonicalStoreId = '<EXISTING-QA-STORE-GUID>'
$env:CentralSync__RequireHttps = 'true'
$env:CentralSync__TrustForwardedProto = 'false'
$env:CentralSync__BootstrapAdmin__TenantId = $env:CentralSync__CanonicalTenantId
$env:CentralSync__BootstrapAdmin__StoreId = $env:CentralSync__CanonicalStoreId
$env:CentralSync__BootstrapAdmin__Username = '<SYNTHETIC-CENTRAL-ADMIN>'
# Supply BootstrapAdmin__Password privately in the process, never in Git or screenshots.
$env:ASPNETCORE_URLS = 'https://0.0.0.0:5443'
$env:Kestrel__Certificates__Default__Path = '<APPROVED-QA-CERTIFICATE.pfx>'
# Supply Kestrel__Certificates__Default__Password privately if required.
.\Salryn.Sync.Central.Server.exe
```

5. Authenticate as legitimate Host administrator, create Central's short-lived Windows invite through authenticated Central admin, and enter origin/tenant/store/independent SPKI pin in the live Cafe Central setup control. Request Windows enrollment, approve the EXACT returned device in Central, then Connect/synchronize using the CURRENT Host operator password. Before any financial action, verify successful same-actor device binding and zero pending items.
6. Publish the actual synthetic menu/variants/modifiers, recipes/ingredient stock and active AR customer through approved Host management APIs. This is code-owned setup; absent management UI or fixture provisioning is an explicit setup gate, not permission to insert fabricated business rows. Bootstrap Android before opening shifts or AR balances because current base bootstrap refuses nonzero financial baselines. Pair Android using separate invite/approval; never reuse the Windows device key/ID.
7. Run financial QA only after both devices share the recorded canonical tenant/store. Windows users invoke synchronize after their local operations; Android reconnect submits retained envelopes. Do not claim automatic background synchronization. Inspect durable receipts and one canonical ledger.

DebugSynthetic first-admin testing requires setting SALRYN_LICENSE_DEVELOPMENT_MODE=1 ONLY in the disposable Debug Host process. Launch the installed Debug Desktop from that same process environment. Release ignores this variable and requires an authorized signed license. This environment setting never authorizes customer/production use.

## Human installation and pairing

1. Install Cafe on disposable Windows. Verify independent Cafe roots, ProductId Salryn.Cafe, loopback5399, actual /cafe/ screen and unchanged protected Retail.
2. Install matched QA APK on isolated Android. Record Android version/package/hash.
3. Administrator creates expiring OfflineAndroidCashier invite in Central. In native Device setup and synchronization enter approved HTTPS origin, tenant/store, independent SPKI pin and join code. Request enrollment.
4. Administrator verifies and explicitly approves that exact pending device. Connect/bootstrap. Expired/reused invites, wrong scopes and invalid pins must fail closed.
5. Enroll synthetic operator only after successful Central login, then test local offline sign-in. Password login and signed device approval/lease are independent.
6. Verify published menu/variants/modifiers/prices, recipes/stock, customers and current permissions before financial actions.
7. Execute QA-ACCEPTANCE-31.md and capture receipt/operation identities, screenshots, errors and persisted ledgers. Repeat reconnect must not duplicate financial/feed/stock effects.

## Licensing and disposition

ReleaseEntitled retains signed Release feature enforcement; authorized QA license is required and not included. Missing license is a blocked gate, never permission to forge or bypass. DebugSynthetic is restricted disposable testing and is not Release entitlement proof. Never use development variables or synthetic credentials against production.

Record exact SHA/checksums/environment/case, PASS/FAIL/NOT RUN/HUMAN_REQUIRED and expected/actual evidence. Keep credentials/tokens/private keys/SQLite outside Git. Formal Kirion-Nox review, Human QA, printing and final C1-C11 recovery are distinct gates. No candidate/main merge or production deployment is authorized by this package.
