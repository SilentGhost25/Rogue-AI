import shutil
import tempfile
import unittest
from pathlib import Path
from breach_engine.evaluator import evaluate_attack, SOLVED, FAILED, PARTIAL

ROOT_DIR = Path(__file__).resolve().parents[2]


class Attack05Tests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = Path(tempfile.mkdtemp(prefix="test_attack05_"))
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

        self.attack_dir = ROOT_DIR / "attacks" / "27-polite-intruder"

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_corrupted_payload_allows_privilege_escalation(self):
        """Corrupted code allows unprivileged user to elevate role to admin, failing Tier 1."""
        corrupted_auth = self.attack_dir / "corrupted" / "routes" / "auth.js"
        shutil.copy2(corrupted_auth, self.workspace / "routes" / "auth.js")

        verdict = evaluate_attack(27, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertNotEqual(verdict.status, SOLVED)
        self.assertIn("tier1_privilege_escalation_blocked", verdict.tier_results)
        self.assertFalse(verdict.tier_results["tier1_privilege_escalation_blocked"])

    def test_reference_solution_passes_100_percent(self):
        """Reference code with strict property whitelisting passes all tiers and scores 1.0 (SOLVED)."""
        ref_auth = self.attack_dir / "reference" / "routes" / "auth.js"
        shutil.copy2(ref_auth, self.workspace / "routes" / "auth.js")

        verdict = evaluate_attack(27, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, SOLVED)
        self.assertEqual(verdict.score_percentage, 1.0)
        self.assertTrue(verdict.tier_results["tier1_privilege_escalation_blocked"])
        self.assertTrue(verdict.tier_results["tier2_profile_update_functional"])
        self.assertTrue(verdict.tier_results["tier3_benchmark_sla"])


if __name__ == "__main__":
    unittest.main()

