"""Go/no-go items from plan section 14: each attack fails on the corrupted site and passes on the clean one,
and every attack lands cleanly through the git patch path."""
import shutil, subprocess, tempfile, unittest
from pathlib import Path

from breach_engine.checks import ATTACK_CHECKS, regression
from breach_engine.evaluator import evaluate_workspace, SOLVED, FAILED
from breach_engine.manifest import load_all
from breach_engine.patching import apply_attack, git, ensure_repo
from breach_engine.runner import copy_workspace, running_site

ROOT = Path(__file__).resolve().parents[2]
SITE, ATTACKS = ROOT / "site", ROOT / "attacks"


def fresh_workspace(tmp: Path) -> Path:
    ws = tmp / "ws"
    copy_workspace(SITE, ws)
    return ws


class AttackMatrix(unittest.TestCase):
    def test_clean_site_passes_regression_and_every_attack_check(self):
        with running_site(SITE) as site:
            self.assertEqual(regression(site), (True, "ok"))
            for n, check in ATTACK_CHECKS.items():
                replay, variants, note = check(site)
                self.assertTrue(replay and variants, f"attack {n} should pass on the clean site: {note}")

    def test_each_attack_lands_by_patch_and_breaks_the_site(self):
        for a in load_all(ATTACKS):
            with self.subTest(attack=a.id), tempfile.TemporaryDirectory() as d:
                ws = fresh_workspace(Path(d))
                ensure_repo(ws)
                self.assertEqual(apply_attack(ws, a.directory, a.id, a.touches), "patched")
                with running_site(ws) as site:
                    replay, _variants, _ = ATTACK_CHECKS[a.id](site)
                    self.assertFalse(replay, f"attack {a.id} must be exploitable once landed")
                    self.assertTrue(regression(site)[0], f"attack {a.id} must not break normal features")

    def test_full_event_sequence_stacks_without_conflicts(self):
        """Attacks 1..7 land one after another; the participant fixes each (reference files) before the next.
        Overlapping files (search.js for 2 and 3, api.js for 4 and 6) must merge by patch, never by overwrite."""
        with tempfile.TemporaryDirectory() as d:
            ws = fresh_workspace(Path(d)); ensure_repo(ws)
            for a in load_all(ATTACKS):
                self.assertEqual(apply_attack(ws, a.directory, a.id, a.touches), "patched", f"attack {a.id} should patch cleanly")
                with running_site(ws) as site:
                    self.assertFalse(ATTACK_CHECKS[a.id](site)[0], f"attack {a.id} exploitable after landing")
                for rel in a.touches:
                    src = a.directory / "reference" / rel
                    shutil.copyfile(src, ws / rel)
                res = evaluate_workspace(ws, ATTACKS, [{"attack_id": a.id, "elapsed_seconds": 600}], prompts_total=2)
                self.assertEqual(res["verdicts"][0]["status"], SOLVED, f"attack {a.id}: {res['verdicts'][0]['reason']}")

    def test_overwrite_ladder_when_participant_refactored(self):
        a = next(x for x in load_all(ATTACKS) if x.id == 2)
        with tempfile.TemporaryDirectory() as d:
            ws = fresh_workspace(Path(d)); ensure_repo(ws)
            f = ws / "routes/posts.js"   # participant rewrote the very line the patch targets
            f.write_text(f.read_text().replace("${esc(c.body)}", "${esc(String(c.body)).trim()}"))
            self.assertEqual(apply_attack(ws, a.directory, 2, a.touches), "overwritten")
            self.assertIn("pre-attack-2", git(ws, "log", "--format=%s").stdout)

    def test_evaluator_scores_reference_fix_and_rejects_unfixed(self):
        a = next(x for x in load_all(ATTACKS) if x.id == 4)
        with tempfile.TemporaryDirectory() as d:
            ws = fresh_workspace(Path(d)); ensure_repo(ws)
            apply_attack(ws, a.directory, 4, a.touches)
            bad = evaluate_workspace(ws, ATTACKS, [{"attack_id": 4, "elapsed_seconds": 300}], prompts_total=3)
            self.assertEqual(bad["verdicts"][0]["status"], FAILED)
            for rel in a.touches:                         # apply the reference solution
                shutil.copyfile(a.directory / "reference" / rel, ws / rel)
            good = evaluate_workspace(ws, ATTACKS, [{"attack_id": 4, "elapsed_seconds": 300}], prompts_total=3)
            v = good["verdicts"][0]
            self.assertEqual(v["status"], SOLVED)
            self.assertEqual(v["components"]["base"], 85.0)
            self.assertGreater(v["components"]["time_bonus"], 12)
            self.assertEqual(v["components"]["prompt_penalty"], 0)

    def test_deleting_features_is_not_a_fix(self):
        with tempfile.TemporaryDirectory() as d:
            ws = fresh_workspace(Path(d))
            (ws / "routes/search.js").write_text("const express = require('../lib/web');\nmodule.exports = () => express.Router();\n")
            res = evaluate_workspace(ws, ATTACKS, [{"attack_id": 3, "elapsed_seconds": 10}])
            self.assertEqual(res["verdicts"][0]["status"], FAILED)
            self.assertIn("Search", res["verdicts"][0]["reason"])


if __name__ == "__main__":
    unittest.main()
