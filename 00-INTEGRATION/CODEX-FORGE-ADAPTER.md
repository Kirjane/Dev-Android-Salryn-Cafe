# Project-scoped Codex Forge adapter

Authority: [Technical Authority takeover directive](https://github.com/Kirjane/Dev-Salryn-Cafe/issues/27#issuecomment-7048261423).
Source: `Kirch-Nairu/Operation-FORGE.kirion`, branch
`feat/antigravity-software-factory-r1`, exact SHA
`0e8765de6e0e87ea9b4503714f08fa650e1a79c6`.

Adoption is opt-in candidate for Salryn Cafe only. Original harness and global
configuration are untouched. `.forge/source-manifest.json` binds the copied 16
core and 16 specialist skills and portable factory modules to original bytes.
No skill collisions occurred; inherited Salryn skills are preserved.

The pinned factory retains mission schemas, deterministic scheduler, ownership
leases, SQLite WAL hash-chain journal, evidence classification, recovery, review
validation and bounded self-healing. Import via `.forge/vendor/kirion` on Python's
module path. Original persona strings are schema vocabulary, not active agents.
Do not run original CLI dispatch or native Antigravity workflows in Codex.
The imported dispatcher/CLI require the original complete plugin layout; they
are provenance/reference only in this subset, not Codex entrypoints. Native
`agent()`/`phase()`, plugin registration and hooks are not ported by copying files.

Codex `.codex/config.toml` limits spawned threads to two plus the primary.
Installed CLI `0.164.0` accepted config under `--strict-config`. Session policy
also enforces the three-total ceiling and sequential default. No nested fleet.

`scripts/forge/codex_guard.py` translates Codex `tool_name`/`tool_input` shell
payloads to the pinned guard and emits Codex denial output. Additional project
rules reject reset-hard, clean, stash and rebase. Synthetic commands never run.
Other tool types, shell obfuscation, arbitrary scripts and direct filesystem/API
writes remain subject to primary policy/ownership review and platform permissions.
This hook is advisory defense, not a security sandbox or complete command parser.

`.codex/hooks.json` supplies a project registration candidate. **Real hook
registration/trust/execution: NOT RUN.** No trust bypass or global trust mutation
was used. Do not claim enforcement parity. The absolute command is local to this
Windows recovery workspace and needs regeneration on relocation. Continue safe
work with project policy/manual evidence checks as the directive permits.
Codex requires review/trust of non-managed definitions; see
[official hook documentation](https://learn.chatgpt.com/docs/hooks).

## Qualification and reproducibility

Restricted recovery root: `C:\Users\arane\SalrynForgeRecovery\20261121-takeover`.
Source archive has SHA-256 `f70ca7620b36ffeae3e172ba98f89aac2072bb630fd67ec1106d2620646814c9`.
Run original portable tests in a disposable full Git clone with
`core.autocrlf=false`: `python -m unittest discover -s integrations/antigravity/tests -v`.
On the exact source SHA: **16 tests, exit 0, PASS**; log
`portable-qualified-tests.log`. This tests factory mechanics and synthetic
benchmarks, not Salryn acceptance or native Codex hooks.

Earlier archive-only run: **FAIL**, 16 tests, one error (fixture needs Git).
Earlier default-CRLF Git clone: **FAIL**, 16 tests, one package-digest mismatch.
Both logs retained; no source/assertion changes used to obtain passing tests.

Adapter command: `python scripts/forge/test_codex_adapter.py` (candidate diff
against `b803910502dee3b1e5fc7c1b15603427d8e3c9b6`), Windows/Python 3.13.5:
initial **6 tests, exit 0, PASS**. Exact committed adapter validation and
independent review are still required. Host-native Antigravity qualification:
**NOT RUN**. Physical/production acceptance: **HUMAN_REQUIRED**.
