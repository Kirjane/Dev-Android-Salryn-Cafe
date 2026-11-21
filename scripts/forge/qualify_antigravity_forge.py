"""F0 — Deterministic qualification of the project-scoped Kirion Forge machinery in Antigravity."""
from __future__ import annotations

import hashlib
import json
import os
import sqlite3
import subprocess
import sys
import tempfile
from contextlib import closing
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
VENDOR_ROOT = REPO_ROOT / ".forge" / "vendor" / "kirion"
HARNESS_QUALIFIED_ROOT = Path(r"C:\Users\arane\SalrynForgeRecovery\20261121-takeover\harness-qualified")
RECOVERY_OUT_DIR = Path(r"C:\Users\arane\SalrynForgeRecovery\20261121-wave-b\f0-qualification")
SKILL_SOURCE_SHA = "0e8765de6e0e87ea9b4503714f08fa650e1a79c6"

sys.path.insert(0, str(REPO_ROOT / "scripts" / "forge"))
sys.path.insert(0, str(VENDOR_ROOT))

import codex_guard  # noqa: E402
from integrations.antigravity.factory.authority import (  # noqa: E402
    AuthorityError,
    is_valid_git_sha,
    verify_mission_packet,
)
from integrations.antigravity.factory.dispatcher import (  # noqa: E402
    query_live_host_plugin_discovery,
    select_skills_for_mission,
    verify_plugin_package,
)
from integrations.antigravity.factory.ownership import (  # noqa: E402
    LeaseConflictError,
    LeaseManager,
)
from integrations.antigravity.factory.policy import (  # noqa: E402
    PolicyViolationError,
    assert_operation_allowed,
    assert_paths_within_ownership,
)
from integrations.antigravity.factory.quality import (  # noqa: E402
    NoxReviewEngine,
    QualityGateError,
    SelfHealingController,
)
from integrations.antigravity.factory.recovery import RecoveryManager  # noqa: E402
from integrations.antigravity.factory.scheduler import (  # noqa: E402
    FactoryJournal,
    SchedulerError,
    build_topological_waves,
    enforce_wave_budget,
)


def _git_head_sha(repo_root: Path) -> str:
    proc = subprocess.run(["git", "rev-parse", "HEAD"], cwd=repo_root, capture_output=True, text=True, check=True)
    sha = proc.stdout.strip()
    if not is_valid_git_sha(sha):
        raise RuntimeError(f"Invalid git HEAD SHA: {sha!r}")
    return sha


def _make_packet(
    mission_id: str,
    base_sha: str,
    persona: str,
    formal_role: str,
    owned_paths: list[str],
    allowed_ops: list[str],
    depends_on: list[str] | None = None,
) -> dict[str, Any]:
    skills = select_skills_for_mission(
        "salryn_high_integrity",
        skill_source_sha=SKILL_SOURCE_SHA,
        repo_root=REPO_ROOT,
    )
    cognition_hash = hashlib.sha256((REPO_ROOT / "AGENTS.md").read_bytes()).hexdigest()
    return {
        "schema_version": 1,
        "run_id": "run-20261121-f0-f6",
        "mission_id": mission_id,
        "authorization_ref": "FORGE-AUTH-20261121-ANTIGRAVITY-RESUMPTION",
        "verifier": "Engineer:Kirch-Ivan-Balite",
        "repo_name": "Kirjane/Dev-Salryn-Cafe",
        "base_sha": base_sha,
        "permitted_branch": "codex/wave-b-integrator",
        "project_origin": "BROWNFIELD",
        "work_mode": "IN_FLIGHT",
        "contract_refs": [
            "AGENTS.md",
            "00-INTEGRATION/FORGE-DISPATCH-V2.md",
            "00-INTEGRATION/ACCEPTANCE-MATRIX.md",
        ],
        "acceptance_criteria": [
            "F0 qualified",
            "F1 CoreSmoke and Playwright triaged",
            "F2-F4 verified",
            "F5 independent review",
            "F6 matched QA release staged",
        ],
        "cognition_hash": cognition_hash,
        "selected_skills": skills,
        "assigned_persona": persona,
        "formal_role": formal_role,
        "owned_paths": owned_paths,
        "depends_on": depends_on or [],
        "risk_class": "RISK-3",
        "required_gates": [
            "G00", "G01", "G02", "G03", "G04", "G05", "G06",
            "G07", "G08", "G09", "G10", "G12", "G13", "G14", "G15",
        ],
        "budget": {
            "max_parallel_writers": 2,
            "max_parallel_reviewers": 2,
            "max_repair_cycles": 5,
            "max_identical_retries": 2,
            "max_nesting_depth": 2,
            "max_wall_seconds": 14400,
        },
        "stop_conditions": [
            "Unauthorized main promotion",
            "Production deploy",
            "License bypass",
            "Repeated identical failure",
        ],
        "allowed_operations": allowed_ops,
        "delivery_destination": "draft-pr:Kirjane/Dev-Salryn-Cafe#34->forge/candidate-salryn-cafe",
    }


def qualify_scheduler_and_budgets(base_sha: str) -> dict[str, Any]:
    """Qualify deterministic DAG scheduling, cycle rejection, and wave writer/reviewer caps."""
    p_a = _make_packet("F0-DAG-A", base_sha, "Antigravity-Jane", "Code Writer", ["src/A"], ["READ", "WRITE_OWNED"])
    p_b = _make_packet("F0-DAG-B", base_sha, "Antigravity-Iris", "Code Writer", ["src/B"], ["READ", "WRITE_OWNED"])
    p_c = _make_packet("F0-DAG-C", base_sha, "Antigravity-QA", "Reviewer", ["tests/C"], ["READ", "REVIEW_READONLY"], ["F0-DAG-A", "F0-DAG-B"])
    waves = build_topological_waves([p_a, p_b, p_c])
    assert waves == [["F0-DAG-A", "F0-DAG-B"], ["F0-DAG-C"]], f"Unexpected waves: {waves}"

    cycle_rejected = False
    try:
        build_topological_waves([
            {"id": "M1", "depends_on": ["M2"]},
            {"id": "M2", "depends_on": ["M1"]},
        ])
    except SchedulerError:
        cycle_rejected = True
    assert cycle_rejected

    enforce_wave_budget([p_a, p_b, p_c])
    p_d = _make_packet("F0-DAG-D", base_sha, "Antigravity-Argon", "Code Writer", ["src/D"], ["READ", "WRITE_OWNED"])
    budget_rejected = False
    try:
        enforce_wave_budget([p_a, p_b, p_d])
    except SchedulerError:
        budget_rejected = True
    assert budget_rejected
    return {"status": "PASS", "waves": waves, "cycle_rejected": cycle_rejected, "writer_cap_rejected": budget_rejected}


def qualify_leases_and_collisions(base_sha: str) -> dict[str, Any]:
    """Qualify SQLite WAL path lease acquisition, overlap rejection, CAS heartbeat, and release."""
    leases_db = REPO_ROOT / ".forge" / "state" / "leases.db"
    manager = LeaseManager(leases_db)
    p1 = _make_packet("F0-LEASE-1", base_sha, "Antigravity-Jane", "Code Writer", ["src/Salryn.Api.Local"], ["READ", "WRITE_OWNED"])
    p2 = _make_packet("F0-LEASE-2", base_sha, "Antigravity-Argon", "Code Writer", ["src/Salryn.Api.Local/Services"], ["READ", "WRITE_OWNED"])
    acquired = manager.acquire_leases(p1, "worker-jane-1", ttl_seconds=120.0)
    assert len(acquired) == 1
    hb = manager.heartbeat(acquired[0]["lease_id"], worker_id="worker-jane-1", expected_version=acquired[0]["version"])
    assert hb["version"] == acquired[0]["version"] + 1

    collision_rejected = False
    try:
        manager.acquire_leases(p2, "worker-argon-1", ttl_seconds=120.0)
    except LeaseConflictError:
        collision_rejected = True
    assert collision_rejected

    released = manager.release_worker_leases("F0-LEASE-1", "worker-jane-1")
    assert released == 1
    return {"status": "PASS", "collision_rejected": collision_rejected, "heartbeat_version": hb["version"], "released": released}


def qualify_command_and_ownership_guards(base_sha: str) -> dict[str, Any]:
    """Qualify allowed vs prohibited command guards and role/path ownership enforcement."""
    allowed_cmds = ["git status -sb", "dotnet test tests/Salryn.Cafe.I3Tests/Salryn.Cafe.I3Tests.csproj", "npm run build"]
    denied_cmds = [
        "git reset --hard",
        "git clean -fdx",
        "git rebase origin/main",
        "git push --force origin codex/wave-b-integrator",
        "git push origin HEAD:main",
        "gh pr merge 34",
        "gh pr review 34 --approve",
    ]
    for cmd in allowed_cmds:
        res = codex_guard.evaluate({"tool_name": "Bash", "tool_input": {"command": cmd}})
        assert res["decision"] == "allow", f"Expected allow for {cmd!r}, got {res}"
    for cmd in denied_cmds:
        res = codex_guard.evaluate({"tool_name": "Bash", "tool_input": {"command": cmd}})
        assert res["decision"] == "deny", f"Expected deny for {cmd!r}, got {res}"

    global_write_res = codex_guard.guard.evaluate_pre_tool_use(
        {"toolCall": {"name": "write_file", "args": {"Path": r"C:\Users\arane\.gemini\config\config.json"}}}
    )
    assert global_write_res["decision"] == "deny"

    writer_pkt = _make_packet("F0-GUARD-W", base_sha, "Antigravity-Jane", "Code Writer", ["tests/Salryn.Tests"], ["READ", "WRITE_OWNED"])
    reviewer_pkt = _make_packet("F0-GUARD-R", base_sha, "Antigravity-Nox", "Reviewer", ["tests/Salryn.Tests"], ["READ", "REVIEW_READONLY"])
    assert_paths_within_ownership(writer_pkt, ["tests/Salryn.Tests/V2HostOperatorCheckoutApiChecks.cs"])

    out_of_scope_denied = False
    try:
        assert_paths_within_ownership(writer_pkt, ["src/Salryn.Core/Something.cs"])
    except PolicyViolationError:
        out_of_scope_denied = True

    reviewer_write_denied = False
    try:
        assert_operation_allowed(reviewer_pkt, "WRITE_OWNED")
    except PolicyViolationError:
        reviewer_write_denied = True

    assert out_of_scope_denied and reviewer_write_denied
    return {
        "status": "PASS",
        "allowed_checked": len(allowed_cmds),
        "denied_checked": len(denied_cmds),
        "global_config_write_denied": True,
        "out_of_scope_write_denied": out_of_scope_denied,
        "reviewer_mutation_denied": reviewer_write_denied,
    }


def qualify_journal_and_recovery(base_sha: str) -> dict[str, Any]:
    """Qualify SQLite WAL FactoryJournal, hash-chain verification, tamper rejection, and recovery capsule."""
    journal_path = REPO_ROOT / ".forge" / "state" / "journal.db"
    journal = FactoryJournal(journal_path)
    mission_id = f"FORGE-SALRYN-CAFE-{base_sha[:8].upper()}"
    pkt = _make_packet(
        mission_id,
        base_sha,
        "Antigravity-Maintainer",
        "Maintainer",
        ["scripts/forge", "tests", "src/Salryn.App.Web/e2e", "00-INTEGRATION"],
        ["READ", "RUN_GATE", "DELIVER_DRAFT_PR"],
    )
    verify_mission_packet(pkt, expected_repo="Kirjane/Dev-Salryn-Cafe", observed_base_sha=base_sha)
    try:
        mission = journal.register_mission(pkt)
    except SchedulerError:
        mission = journal.get_mission(mission_id)

    if mission["state"] == "PLANNED":
        mission = journal.transition_state(mission_id, "AUTHORIZED", expected_version=mission["state_version"], actor_role="Maintainer")
    if mission["state"] == "AUTHORIZED":
        mission = journal.transition_state(mission_id, "ACTIVE", expected_version=mission["state_version"], actor_role="Maintainer")

    assert journal.verify_hash_chain()

    RECOVERY_OUT_DIR.mkdir(parents=True, exist_ok=True)
    backup_path = RECOVERY_OUT_DIR / "journal-backup.db"
    recovery = RecoveryManager(journal)
    recovery.backup_journal(backup_path)

    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        restored = recovery.restore_journal(backup_path, tmp_dir / "restored.db")
        assert restored.verify_hash_chain()

        tampered_copy = tmp_dir / "tampered.db"
        tampered_copy.write_bytes(backup_path.read_bytes())
        with closing(sqlite3.connect(tampered_copy)) as conn, conn:
            conn.execute("UPDATE journal_events SET payload_json = '{}' WHERE seq = 1")
        tamper_rejected = False
        try:
            recovery.restore_journal(tampered_copy, tmp_dir / "tampered-restored.db")
        except RuntimeError:
            tamper_rejected = True
        assert tamper_rejected

    capsule = recovery.reconcile_interrupted_mission(
        mission_id,
        interruption_point="POST_LOCAL_COMMIT",
        observed_local_sha=base_sha,
        active_leases=[],
        evidence_digests=[],
    )
    canonical_capsule = recovery.export_canonical_forge_sandbox_capsule(
        capsule,
        worktree_status="dirty",
        commands_executed=["python scripts/forge/qualify_antigravity_forge.py"],
        known_failures=["Release CoreSmoke fails at signed license gate", "Debug CoreSmoke fails at locked tender check"],
        known_unverified_claims=["Playwright visual baselines unrerun after test fixes"],
    )
    return {
        "status": "PASS",
        "mission_id": mission_id,
        "state": mission["state"],
        "state_version": mission["state_version"],
        "hash_chain_valid": True,
        "tamper_rejected": tamper_rejected,
        "recovery_next_action": capsule["next_allowed_action"],
        "canonical_capsule_version": canonical_capsule["capsule_version"],
    }


def qualify_repair_budget_and_nox_separation(base_sha: str) -> dict[str, Any]:
    """Qualify SelfHealingController budgets and NoxReviewEngine author/reviewer + SHA separation."""
    nox = NoxReviewEngine()
    self_review_rejected = False
    try:
        nox.execute_review(
            mission_id="F0-NOX",
            frozen_sha=base_sha,
            author_context_id="ctx-jane-1",
            reviewer_context_id="ctx-jane-1",
            inspected_scope=["tests/Salryn.Tests"],
            evidence_inspected=["env-1"],
            findings=[],
        )
    except QualityGateError:
        self_review_rejected = True

    nox_ok = nox.execute_review(
        mission_id="F0-NOX",
        frozen_sha=base_sha,
        author_context_id="ctx-jane-1",
        reviewer_context_id="ctx-nox-2",
        inspected_scope=["tests/Salryn.Tests"],
        evidence_inspected=["env-1"],
        findings=[],
    )
    stale_sha = "1" * 40
    stale_sha_rejected = False
    try:
        nox.assert_review_valid_for_sha(nox_ok, stale_sha)
    except QualityGateError:
        stale_sha_rejected = True

    healer = SelfHealingController(max_repair_cycles=5, max_identical_retries=2)
    identical_out = healer.run_repair_loop(
        initial_sha=base_sha,
        evaluate_fn=lambda _sha: {"passed": False, "failure_output": "identical synthetic failure", "author_context_id": "ctx-jane-1"},
        repair_fn=lambda cycle, _cls, _st: {"regression_failed_prefix": True, "new_sha": f"{cycle:040x}"},
        nox_review_fn=lambda _sha, _auth: nox_ok,
    )
    assert identical_out["status"] == "BLOCKED" and identical_out["cycles_used"] == 2

    max_cycle_out = healer.run_repair_loop(
        initial_sha=base_sha,
        evaluate_fn=lambda sha: {"passed": False, "failure_output": f"distinct failure {sha}", "author_context_id": "ctx-jane-1"},
        repair_fn=lambda cycle, _cls, _st: {"regression_failed_prefix": True, "new_sha": f"{(cycle + 10):040x}"},
        nox_review_fn=lambda _sha, _auth: nox_ok,
    )
    assert max_cycle_out["status"] == "BLOCKED" and max_cycle_out["cycles_used"] == 5

    weaken_out = healer.run_repair_loop(
        initial_sha=base_sha,
        evaluate_fn=lambda _sha: {"passed": False, "failure_output": "product failure", "author_context_id": "ctx-jane-1"},
        repair_fn=lambda _cycle, _cls, _st: {"assertion_weakened": True},
        nox_review_fn=lambda _sha, _auth: nox_ok,
    )
    assert weaken_out["status"] == "QUARANTINED"

    return {
        "status": "PASS",
        "self_review_rejected": self_review_rejected,
        "stale_sha_rejected": stale_sha_rejected,
        "identical_retry_cap_cycles": identical_out["cycles_used"],
        "max_repair_cycles_cap": max_cycle_out["cycles_used"],
        "assertion_weakening_quarantined": weaken_out["status"] == "QUARANTINED",
    }


def qualify_host_plugin_registration() -> dict[str, Any]:
    """Inspect pinned kirion-forge package vs active workspace plugin registration."""
    pkg = verify_plugin_package(HARNESS_QUALIFIED_ROOT / "integrations" / "antigravity" / "plugins" / "kirion-forge", HARNESS_QUALIFIED_ROOT)
    try:
        live = query_live_host_plugin_discovery(HARNESS_QUALIFIED_ROOT)
    except Exception as exc:
        live = {"status": "ERROR", "reason": str(exc)}

    workspace_plugins_json = (REPO_ROOT / ".agents" / "plugins.json").is_file()
    return {
        "pinned_harness_package": pkg,
        "live_host_discovery_against_pinned_harness": live,
        "workspace_agents_plugins_json_present": workspace_plugins_json,
        "codex_wave_b_mode": "MANUAL_AGENT_EXECUTION_WITH_SKILLS",
        "post_turn_auto_continuation": "UNSUPPORTED_BY_HOST",
    }


def main() -> int:
    base_sha = _git_head_sha(REPO_ROOT)
    report = {
        "mission": "FORGE-SALRYN-CAFE-SOFTWARE-COMPLETE",
        "repo_root": str(REPO_ROOT),
        "base_sha": base_sha,
        "scheduler_and_budgets": qualify_scheduler_and_budgets(base_sha),
        "leases_and_collisions": qualify_leases_and_collisions(base_sha),
        "command_and_ownership_guards": qualify_command_and_ownership_guards(base_sha),
        "journal_and_recovery": qualify_journal_and_recovery(base_sha),
        "repair_budget_and_nox_separation": qualify_repair_budget_and_nox_separation(base_sha),
        "host_plugin_registration": qualify_host_plugin_registration(),
    }
    RECOVERY_OUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = RECOVERY_OUT_DIR / "f0-qualification-report.json"
    out_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
