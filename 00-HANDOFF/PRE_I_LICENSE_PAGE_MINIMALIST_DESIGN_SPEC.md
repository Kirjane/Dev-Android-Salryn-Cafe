# PRE-I License Page — Minimalist Design Spec

Companion to `PRE_I_SURGICAL_STATE_LICENSE_UX_REVIEW.md`.  
**Design lock for future implementation only. No code in this file’s lane.**

---

## 1. Intent

Activate a local Host POS with quiet confidence.  
Not a SaaS launch page. Not AI-gradient theater.

**One job (fresh):** prove the device needs a signed license and help the operator obtain/import it.  
**One job (active):** confirm activation status and allow careful replacement.

---

## 2. Visual system

| Token | Value |
| --- | --- |
| Page background | `#F4F5F7` |
| Card surface | `#FFFFFF` |
| Text primary | `#111827` |
| Text secondary | `#4B5563` |
| Text muted | `#6B7280` |
| Border | `#E5E7EB` |
| Accent | `#1F4B7A` (buttons, focus ring) |
| Focus ring | 2px solid accent, offset 2px |
| Radius | 12px cards, 8px controls |
| Shadow | none preferred; max `0 1px 2px rgba(0,0,0,0.06)` on modal |

**Forbidden:** multi-stop gradients, glass blur panels, neon glows, contour geometry, grid overlays, sparkles icons, giant gradient wordmarks.

**Type:** system-ui / Segoe UI / Inter. Mono only for Machine Code.

**Motion:** none beyond `:focus-visible`.

---

## 3. Layout

### Fresh

```
max-width 920px, centered, padding 24px

Header row:
  Salryn · by Kirjane Labs          Status: Not activated

Two columns (stack at <720px):
  Left 55%  Activation card
  Right 45% Machine Code + support
```

### Active

```
Single column card
Title · summary · actions · optional <details> technical
Footer credits
```

---

## 4. Copy (locked draft)

**Fresh title:** Activate Salryn  
**Fresh subtitle:** This device needs a signed local license before setup can continue.  
**Fresh body:** Salryn runs locally on this device. Send this Machine Code to Kirjane Labs to receive a signed license file.  
**Primary CTA:** Import license  
**Secondary:** Copy Machine Code  
**Active title:** Salryn is activated  
**Active body:** This Host is licensed for local use on this device.  
**Replace CTA:** Replace license  

---

## 5. Modal (3 steps)

1. **Machine Code** — code, copy, email note  
2. **Terms** — scroll + checkbox (full text in parent review report §10)  
3. **Import** — paste signed license + Install  

Import shortcut still requires Terms if not accepted.

Replace: same steps + “replaces the active local license” checkbox.

---

## 6. Credits

Footer only:

```
Salryn by Kirjane Labs
Technology Lead: Kirch Ivan Balite
```

---

## 7. Accessibility / responsive

- 390px width: single column, no horizontal overflow  
- Visible keyboard focus on all controls  
- Contrast AA for text/accent on light bg  
- Usable at 80% browser zoom  
- No min-width above 320px content  

---

## 8. Implementation acceptance criteria

- [ ] No radial/gradient hero backdrop  
- [ ] No feature pill wall / value columns on fresh page  
- [ ] Machine Code visible without opening marketing chrome  
- [ ] Terms text matches review report (beta seriousness)  
- [ ] Terms + replace re-ack still required before install  
- [ ] Host vs Terminal messaging retained  
- [ ] Support email retained  
- [ ] Technical IDs collapsed when active  
- [ ] Existing CoreSmoke behavioral license gates still pass after guard string updates  
- [ ] Manual screenshots: fresh, terms, import, active, replace  

---

## 9. Explicit non-goals

- New illustration system  
- Animation library  
- Dark “night mode launch” aesthetic  
- Cloud activation UX  
- Host License ID / Store ID as operator tasks  

---

*Lock this before the License rebuild implementation lane.*
