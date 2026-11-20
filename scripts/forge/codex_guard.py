"""Codex payload adapter for pinned Forge policy. Advisory, never a sandbox.

Registration/trust must be observed separately. Synthetic tests do not prove hooks run.
"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / '.forge/vendor/kirion/integrations/antigravity/plugins/kirion-forge/hooks/forge_guard.py'
spec = importlib.util.spec_from_file_location('pinned_forge_guard', SOURCE)
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


def evaluate(payload: dict) -> dict:
    name = payload.get('tool_name')
    args = payload.get('tool_input')
    if not isinstance(name, str) or not isinstance(args, dict):
        return {'decision': 'deny', 'reason': 'Malformed Codex hook payload.'}
    if name in ('Bash', 'exec_command', 'functions.exec_command'):
        command = args.get('command', args.get('cmd'))
        if not isinstance(command, str) or not command.strip():
            return {'decision': 'deny', 'reason': 'Missing shell command.'}
        normalized = guard._normalize_shell_continuations(command)
        # The inherited plugin did not cover these repository-specific prohibitions.
        if re.search(r'\bgit(?:\.exe)?\b[^\n]*(?:\breset\s+--hard\b|\bclean\b|\brebase\b|\bstash\b)', normalized, re.I):
            return {'decision': 'deny', 'reason': 'Preserved-history/worktree boundary.'}
        return guard.evaluate_pre_tool_use({'toolCall': {'name': 'run_command', 'args': {'CommandLine': command}}})
    # Other tool shapes require scoped/manual ownership review, not inferred shell approval.
    return {'decision': 'allow'}


def main() -> int:
    try:
        payload = json.load(sys.stdin)
        result = evaluate(payload) if isinstance(payload, dict) else {'decision': 'deny', 'reason': 'Expected object.'}
    except Exception:
        result = {'decision': 'deny', 'reason': 'Invalid hook payload or policy unavailable.'}
    if result['decision'] == 'deny':
        print(json.dumps({'hookSpecificOutput': {'hookEventName': 'PreToolUse', 'permissionDecision': 'deny', 'permissionDecisionReason': result['reason']}}))
    else:
        print('{}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
