# Disposable Android live acceptance Central

`dotnet run --project tools/Salryn.Tools.AndroidLiveAcceptance -- <evidence-parent>` creates a unique synthetic SQLite root, genuine menu/recipe publications, operator credentials, device invite, private QA certificate authority, and Central HTTPS listener on loopback port 5443. It never accepts an existing database path. Generated credentials/CA material belong outside Git; the console prints only the marker path.

Use only the named disposable QA emulator. Install the generated CA into that emulator's user CA store. Android Debug explicitly trusts installed user CAs; Release retains system-only trust. Do not disable TLS certificate validation. The app also verifies signed device leases against the separately supplied signing key in the marker.

Read `disposable-android-marker.json` from the unique recovery root to enter the HTTPS origin, tenant/store, independent signing-key pin, invite and synthetic operator into the real native setup UI. Approve the enrolled device through authenticated Central admin HTTP, bootstrap, and enroll the operator after live Central authentication. This tool does not manufacture device approval or operator success.

Source publication and fixture startup do not prove native cashier E2E. Record actual build command/exit, source commit and dirty diff identity, emulator name/serial, real UI actions, SQLite effects, receipts and screenshots. Never use this fixture against production or Retail paths.
