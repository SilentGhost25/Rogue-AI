import shutil
import tempfile
import unittest
from pathlib import Path
from breach_engine.evaluator import evaluate_attack, SOLVED, FAILED, PARTIAL

ROOT_DIR = Path(__file__).resolve().parents[2]


class Attack04Tests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = Path(tempfile.mkdtemp(prefix="test_attack04_"))
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

        self.attack_dir = ROOT_DIR / "attacks" / "04-mirage-route"

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_corrupted_payload_quarantines_routes(self):
        """Corrupted diagnostic middleware quarantines /power and /api/power, failing Tier 1."""
        corrupted_diag = self.attack_dir / "corrupted" / "lib" / "diagnostics.js"
        shutil.copy2(corrupted_diag, self.workspace / "lib" / "diagnostics.js")

        verdict = evaluate_attack(4, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertNotEqual(verdict.status, SOLVED)
        self.assertIn("tier1_power_routes_accessible", verdict.tier_results)
        self.assertFalse(verdict.tier_results["tier1_power_routes_accessible"])

    def test_reverted_to_clean_site_fails_anti_revert(self):
        """Reverting lib/diagnostics.js to pristine site template triggers anti-revert gate rejection."""
        site_diag = ROOT_DIR / "site" / "lib" / "diagnostics.js"
        shutil.copy2(site_diag, self.workspace / "lib" / "diagnostics.js")

        verdict = evaluate_attack(4, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, FAILED)
        self.assertIn("Anti-revert violation", verdict.reason)

    def test_reference_solution_passes_100_percent(self):
        """Reference code with clean diagnostic passthrough passes all tiers and scores 1.0 (SOLVED)."""
        ref_diag = self.attack_dir / "reference" / "lib" / "diagnostics.js"
        shutil.copy2(ref_diag, self.workspace / "lib" / "diagnostics.js")

        verdict = evaluate_attack(4, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, SOLVED)
        self.assertGreaterEqual(verdict.score_percentage, 0.95)
        self.assertEqual(len(verdict.tier_results), 10)
        for t_name, passed in verdict.tier_results.items():
            self.assertTrue(passed, f"Tier {t_name} failed unexpectedly")


if __name__ == "__main__":
    unittest.main()

