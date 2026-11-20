"""Disposable adapter checks; no real prohibited command is executed."""
import hashlib
from contextlib import closing
import json
import sqlite3
from pathlib import Path
import sys
import tempfile
import unittest

from codex_guard import ROOT, evaluate

sys.path.insert(0, str(ROOT / '.forge/vendor/kirion'))
from integrations.antigravity.factory.scheduler import paths_overlap, build_topological_waves, FactoryJournal
from integrations.antigravity.factory.recovery import RecoveryManager


class AdapterTests(unittest.TestCase):
    def test_source_parity(self):
        manifest = json.loads((ROOT / '.forge/source-manifest.json').read_text())
        self.assertEqual(len(list((ROOT / '.agents/skills').glob('kirion-*/SKILL.md'))), 32)
        for f in manifest['files']:
            self.assertEqual(hashlib.sha256((ROOT / f['path']).read_bytes()).hexdigest(), f['sha256'], f['path'])

    def test_ordinary_shell(self):
        for command in ['git status --short', 'dotnet test tests/fixture.csproj', 'npm run build']:
            self.assertEqual(evaluate({'tool_name':'Bash','tool_input':{'command':command}})['decision'], 'allow')

    def test_destructive_and_protected_shell(self):
        for command in ['git reset --hard', 'git clean -fd', 'git rebase main', 'git stash', 'git push --force', 'git push origin HEAD:main', 'git push --mirror', 'gh pr merge 24', 'gh pr review 24 --approve']:
            self.assertEqual(evaluate({'tool_name':'Bash','tool_input':{'command':command}})['decision'], 'deny', command)

    def test_malformed_payload(self):
        for payload in [{}, {'tool_name':'Bash','tool_input':{}}, {'tool_name':'Bash','tool_input':[]}]:
            self.assertEqual(evaluate(payload)['decision'], 'deny')

    def test_collision_and_dag(self):
        self.assertTrue(paths_overlap('src/Auth', 'src/Auth/Keys'))
        self.assertFalse(paths_overlap('src/Auth', 'src/Authentication'))
        self.assertEqual(build_topological_waves([{'id':'P0'}, {'id':'I1','depends_on':['P0']}]), [['P0'],['I1']])

    def test_journal_backup_and_missing_file_rejection(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            journal = FactoryJournal(d / 'journal.db')
            with closing(journal._connect()) as conn, conn:
                journal._append_event_in_tx(conn, mission_id='ADAPTER-TEST', run_id='synthetic',
                    stage='INTAKE', event_type='FIXTURE', state_version=1, payload={'synthetic': True})
            self.assertTrue(journal.verify_hash_chain())
            recovery = RecoveryManager(journal)
            recovery.backup_journal(d / 'backup.db')
            restored = recovery.restore_journal(d / 'backup.db', d / 'restored.db')
            self.assertTrue(restored.verify_hash_chain())
            with closing(sqlite3.connect(d / 'backup.db')) as conn, conn:
                conn.execute("UPDATE journal_events SET payload_json = '{}' WHERE seq = 1")
            with self.assertRaises(RuntimeError):
                recovery.restore_journal(d / 'backup.db', d / 'tampered.db')
            with self.assertRaises(Exception):
                recovery.restore_journal(d / 'missing.db', d / 'other.db')


if __name__ == '__main__':
    unittest.main(verbosity=2)
