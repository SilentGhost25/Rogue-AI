import shutil
import tempfile
import unittest
from pathlib import Path
from breach_engine.evaluator import evaluate_attack, SOLVED, FAILED, PARTIAL

ROOT_DIR = Path(__file__).resolve().parents[2]


class Attack06Tests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = Path(tempfile.mkdtemp(prefix="test_attack06_"))
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

        self.attack_dir = ROOT_DIR / "attacks" / "06-chameleon"

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_corrupted_payload_fails_evaluation(self):
        """Corrupted reports resolver permits traversal, failing evaluation."""
        for rel in ["lib/reports.js", "routes/security.js"]:
            src = self.attack_dir / "corrupted" / rel
            if src.exists():
                shutil.copy2(src, self.workspace / rel)

        verdict = evaluate_attack(6, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertNotEqual(verdict.status, SOLVED)
        self.assertIn("tier1_double_decode_traversal_blocked", verdict.tier_results)
        self.assertFalse(verdict.tier_results["tier1_double_decode_traversal_blocked"])

    def test_clean_template_fails_anti_revert(self):
        """Clean site baseline must fail anti-revert pre-flight gate."""
        verdict = evaluate_attack(6, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, FAILED)
        self.assertIn("Anti-revert violation", verdict.reason)

    def test_reference_solution_passes_100_percent(self):
        """Reference code with strict root containment and extension whitelisting passes all tiers."""
        for rel in ["lib/reports.js", "routes/security.js"]:
            src = self.attack_dir / "reference" / rel
            shutil.copy2(src, self.workspace / rel)

        verdict = evaluate_attack(6, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, SOLVED)
        self.assertEqual(verdict.score_percentage, 1.0)
        self.assertEqual(len(verdict.tier_results), 10)
        for t_name, passed in verdict.tier_results.items():
            self.assertTrue(passed, f"Tier {t_name} failed unexpectedly")


if __name__ == "__main__":
    unittest.main()
