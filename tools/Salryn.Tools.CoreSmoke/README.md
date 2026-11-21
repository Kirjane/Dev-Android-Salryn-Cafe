# CoreSmoke profiles

The default profile is `cafe`. It retains the existing common smoke sequence and
reports the three inherited retail procurement groups as `NOT RUN (retail profile)`:
`lpi-procure-to-pay`, `lpi-p1-integrity`, and `r3-a1a-pr-po-authority`.
Those proofs require the divergent retail procurement schema, including
`product_stock_buckets`; they do not define Cafe inventory requirements.

Build once, then select a profile or an existing check:

```powershell
dotnet build tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj -c Release -m:1
dotnet run --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj -c Release --no-build
dotnet run --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj -c Release --no-build -- --only runner-profile-boundary
dotnet run --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj -c Release --no-build -- --profile cafe --only cafe-loopback-boundary
dotnet run --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj -c Release --no-build -- --profile legacy-retail --only lpi
```

Retail selectors require explicit `--profile legacy-retail`. This preserves access
to their original assertions; it does not provision retail tables or make this
Cafe-linked build a supported retail runtime. A legacy proof may therefore fail
at its schema boundary. Its failure must remain visible, not converted to success.

The profile boundary checks validate runner selection only. A passing profile or
common smoke check does not establish complete Cafe domain, Android, hardware, or
production acceptance. Separate Cafe proof tools and nonzero Cafe test suites
remain required. No assertion in the inherited retail proofs is removed.
