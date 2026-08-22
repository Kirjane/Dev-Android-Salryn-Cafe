# Diagram Render Notes

## Result

**Mermaid PDF rendering succeeded** for both diagrams after pointing Puppeteer at the system Microsoft Edge binary.

| Output | Status |
|---|---|
| `current_state.pdf` | Created |
| `hypothesized_complete_state.pdf` | Created |
| `current_state.mmd` | Source preserved |
| `hypothesized_complete_state.mmd` | Source preserved |

## Initial failure (resolved)

First attempt used `mmdc` with default Puppeteer cache inside the Cursor sandbox temp dir:

```
C:\Users\kirch\AppData\Local\Temp\cursor-sandbox-cache\...\puppeteer
```

Error:

```
Could not find Chrome (ver. 148.0.7778.97)
```

A follow-up attempt to download `chrome-headless-shell@148.0.7778.97` was interrupted after a long wait and left a partial folder under the repo root (`chrome-headless-shell/`). That folder was removed; worktree is dirty only for `00-HANDOFF/RMO_STATE_1_RECONCILIATION_STUDY/`.

## Successful workaround

System Edge was found at:

```
C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe
```

Commands used:

```powershell
$env:PUPPETEER_EXECUTABLE_PATH="C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
$env:PUPPETEER_CACHE_DIR="C:\Users\kirch\.cache\puppeteer"

mmdc -i 00-HANDOFF/RMO_STATE_1_RECONCILIATION_STUDY/diagrams/current_state.mmd `
  -o 00-HANDOFF/RMO_STATE_1_RECONCILIATION_STUDY/diagrams/current_state.pdf -b transparent

mmdc -i 00-HANDOFF/RMO_STATE_1_RECONCILIATION_STUDY/diagrams/hypothesized_complete_state.mmd `
  -o 00-HANDOFF/RMO_STATE_1_RECONCILIATION_STUDY/diagrams/hypothesized_complete_state.pdf -b transparent
```

Exit code: `0` for both.

## Raw logs

Raw render/install logs were not committed. They contained local cache paths, PowerShell wrapper noise, and partial chrome-headless-shell download output. The relevant facts are summarized in this file only.
