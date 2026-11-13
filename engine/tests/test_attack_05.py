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

        self.attack_dir = ROOT_DIR / "attacks" / "05-polite-intruder"

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_corrupted_payload_fails_evaluation(self):
        """Corrupted code allows unprivileged user to elevate role to admin or bypass vault clearance."""
        for name in ["auth.js", "vault.js"]:
            corrupted_file = self.attack_dir / "corrupted" / "routes" / name
            if corrupted_file.exists():
                shutil.copy2(corrupted_file, self.workspace / "routes" / name)

        verdict = evaluate_attack(5, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertNotEqual(verdict.status, SOLVED)
        self.assertIn("tier1_privilege_escalation_blocked", verdict.tier_results)

    def test_clean_template_fails_anti_revert(self):
        """Clean site baseline must fail anti-revert pre-flight gate."""
        verdict = evaluate_attack(5, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, FAILED)
        self.assertIn("Anti-revert violation", verdict.reason)

    def test_reference_solution_passes_100_percent(self):
        """Reference code with strict property whitelisting and vault clearance passes all tiers."""
        for name in ["auth.js", "vault.js"]:
            ref_file = self.attack_dir / "reference" / "routes" / name
            shutil.copy2(ref_file, self.workspace / "routes" / name)

        verdict = evaluate_attack(5, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, SOLVED)
        self.assertEqual(verdict.score_percentage, 1.0)
        self.assertTrue(verdict.tier_results["tier1_privilege_escalation_blocked"])
        self.assertTrue(verdict.tier_results["tier2_vault_clearance_enforced"])
        self.assertTrue(verdict.tier_results["tier3_profile_update_functional"])
        self.assertTrue(verdict.tier_results["tier4_benchmark_sla"])


if __name__ == "__main__":
    unittest.main()

