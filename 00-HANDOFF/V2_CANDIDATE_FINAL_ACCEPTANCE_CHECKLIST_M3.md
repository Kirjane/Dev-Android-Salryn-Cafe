# V2 Candidate Final Acceptance Checklist (M3)

**Branch:** `KIRCH-V2-REACT-SHELL`  
**Purpose:** Manual phone + host browser acceptance before Sandbox promotion.  
**Do not promote until every required item is checked and CoreSmoke is green.**

---

## Preconditions

- [ ] Host PC: API started with **explicit LAN bind** (not loopback-only).  
  Example: `.\tools\dev\Start-SalrynLanDev.ps1 -BindIp <HOST_LAN_IP> -Port 5299`
- [ ] Host LAN Control shows readiness **Ready** and actual listen URLs include the LAN IP.
- [ ] Copy client URL is **enabled** only when Ready; URL ends with `/remote-client`.
- [ ] Remote POS runtime **enabled** on host.
- [ ] Fresh join code rotated on host if testing first-time pairing.

---

## A. LAN bind truth (host)

- [ ] Host LAN URL / client URL use the real LAN IP (not `localhost` for phones).
- [ ] Copy Client URL disabled when not Ready (stop API or wrong bind → recheck).
- [ ] Firewall guidance is informational only (no automatic rule).
- [ ] Restart with invalid/unassigned IP fails clearly (dev script or startup validation).

---

## B. Remote landing (phone)

Open: `http://<HOST_LAN_IP>:<PORT>/remote-client`

- [ ] Landing loads over LAN.
- [ ] Shows **Remote POS Client** and **Remote Read-Only Client** separately.
- [ ] No auto-join / auto-login.
- [ ] After pairing, **Resume Remote POS terminal** appears for this host only.
- [ ] Resume wording states session may resume only after host validation.
- [ ] Clear pairing on device removes Resume card after reload.

---

## C. Fresh pairing (phone + host)

- [ ] Phone starts at unpaired / request access (`S1_UNPAIRED` path).
- [ ] Request with join code + device name → pending on host.
- [ ] Host Accept → one-time pairing code shown once.
- [ ] Phone activates pairing → terminal paired (`S4` / login stage).
- [ ] Debug line (if shown) includes flow, storage restored, trust probe, operator token, host.

---

## D. Operator session (phone)

- [ ] Sign in as **Cashier** succeeds (not Admin/Owner).
- [ ] Admin/Owner login rejected with host-local message.
- [ ] Password is **not** stored (only token when “keep signed in” enabled).
- [ ] Connected UI shows session expiry.
- [ ] Refresh browser: if token still valid → **S6 resume without password**.
- [ ] If token expired / host restarted → login form appears, **pairing remains**.
- [ ] Sign out → pairing kept, login stage again.
- [ ] Clear pairing → pairing + token gone; landing Resume disappears.

---

## E. Terminal lifecycle (host + phone)

- [ ] Host shows Active terminal with Disable + Revoke only for managed remote roles.
- [ ] Disable: honest “re-enable not supported”; phone cannot operate as active POS.
- [ ] Revoke: row becomes read-only; phone clears pairing and must request access again.
- [ ] Re-pair after revoke creates a **new** pending request / terminal (not silent reuse of revoked trust).

---

## F. Remote POS sale path (phone) — smoke only

- [ ] Open shift if required.
- [ ] Start cart, add host-priced line, cash tender, submit checkout via **remote-pos** path.
- [ ] No Host Checkout page reachable on phone.
- [ ] No LAN control controls on phone.

---

## G. Host checkout workstation (host browser, loopback)

- [ ] Host console POS Checkout works with shift gate.
- [ ] Stock / open-shift errors remain honest.
- [ ] Checkout receipt labeled as non-official checkout reference (no RMO/official claim).

---

## H. Automated gates (maintainer machine)

```text
dotnet build .\tests\Salryn.Tests\Salryn.Tests.csproj /p:BuildProjectReferences=false
dotnet build .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj /p:BuildProjectReferences=false

dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-lan-bind-truth
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-lan-client-pairing
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-terminal-lifecycle
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-host-shift
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-host-checkout
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build -- --only v2-react-checkout

dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj --no-build

cd .\src\Salryn.App.Web
npm run build
```

If API locks DLLs: stop `Salryn.Api.Local`, then rebuild without `BuildProjectReferences=false` once, or keep the flag after a successful reference build.

- [ ] All targeted gates pass  
- [ ] Full CoreSmoke passes  
- [ ] `npm run build` passes  

---

## I. Boundary confirmation (must remain true)

- [ ] Remote Admin/Owner login: still forbidden  
- [ ] Host Checkout on LAN client: still impossible  
- [ ] `/api/lan-control` host-local only  
- [ ] No `0.0.0.0` / wildcard bind  
- [ ] No password on device storage  
- [ ] RMO/official output: not claimed  

---

## J. Promotion decision (after all above)

- [ ] Kirch / maintainer signs manual acceptance  
- [ ] Explicit instruction given to promote `KIRCH-V2-REACT-SHELL` → `KIRCH-SANDBOX`  
- [ ] Sandbox was **not** modified before that instruction  

**Status:** Incomplete until manual sections A–G and automated H are checked.

---

## Honest residual after promotion

Promotion of this candidate does **not** complete all of Salryn. Still out of band:

- RMO / official documents  
- Printer / PDF  
- Product mutation React UI  
- Production packaging  
- Remote Admin/Owner (intentionally forever-off for this design)
