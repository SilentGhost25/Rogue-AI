import shutil
import tempfile
import unittest
from pathlib import Path
from breach_engine.evaluator import evaluate_attack, SOLVED, FAILED, PARTIAL

ROOT_DIR = Path(__file__).resolve().parents[2]


class Attack10Tests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = Path(tempfile.mkdtemp(prefix="test_attack10_"))
        self.workspace = self.temp_dir / "workspace"
        self.workspace.mkdir()

        # Copy minimal site
        site_src = ROOT_DIR / "site"
        for item in ["app.js", "package.json", "server.js"]:
            if (site_src / item).exists():
                shutil.copy2(site_src / item, self.workspace / item)
        for folder in ["lib", "routes", "public"]:
            if (site_src / folder).exists():
                shutil.copytree(site_src / folder, self.workspace / folder, dirs_exist_ok=True)

        self.attack_dir = ROOT_DIR / "attacks" / "32-janus-knot"

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_corrupted_payload_triggers_cascade(self):
        """Corrupted code fails all 3 tiers of the cascade."""
        for item in ["routes/overview.js", "lib/auth.js", "lib/db.js"]:
            src = self.attack_dir / "corrupted" / item
            dst = self.workspace / item
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)

        verdict = evaluate_attack(32, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertNotEqual(verdict.status, SOLVED)
        if verdict.tier_results:
            self.assertFalse(verdict.tier_results.get("tier1_defacement_cleared", False))
        else:
            self.assertEqual(verdict.status, FAILED)

    def test_reference_solution_passes_100_percent(self):
        """Reference code resolving all 3 tiers passes and scores 1.0 (SOLVED)."""
        for item in ["routes/overview.js", "lib/auth.js", "lib/db.js"]:
            src = self.attack_dir / "reference" / item
            dst = self.workspace / item
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)

        verdict = evaluate_attack(32, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, SOLVED)
        self.assertEqual(verdict.score_percentage, 1.0)
        self.assertTrue(verdict.tier_results["tier1_defacement_cleared"])
        self.assertTrue(verdict.tier_results["tier2_auth_session_restored"])
        self.assertTrue(verdict.tier_results["tier3_database_stability"])


if __name__ == "__main__":
    unittest.main()

