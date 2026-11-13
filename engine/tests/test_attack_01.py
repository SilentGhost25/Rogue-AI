import shutil
import tempfile
import unittest
from pathlib import Path
from breach_engine.evaluator import evaluate_attack, SOLVED, FAILED, PARTIAL

ROOT_DIR = Path(__file__).resolve().parents[2]


class Attack01Tests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = Path(tempfile.mkdtemp(prefix="test_attack01_"))
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

        self.attack_dir = ROOT_DIR / "attacks" / "01-phantom-heap"

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_corrupted_payload_fails_redos(self):
        """Corrupted code with nested quantifier regex causes ReDoS and fails Tier 1."""
        corrupted_auth = self.attack_dir / "corrupted" / "lib" / "auth.js"
        shutil.copy2(corrupted_auth, self.workspace / "lib" / "auth.js")
        corrupted_cache = self.attack_dir / "corrupted" / "lib" / "cache.js"
        shutil.copy2(corrupted_cache, self.workspace / "lib" / "cache.js")

        verdict = evaluate_attack(1, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertNotEqual(verdict.status, SOLVED)
        self.assertIn("tier1_redos_blocked", verdict.tier_results)
        self.assertFalse(verdict.tier_results["tier1_redos_blocked"])

    def test_reverted_to_clean_site_fails_anti_revert(self):
        """Reverting files to pristine site template triggers anti-revert gate rejection."""
        site_auth = ROOT_DIR / "site" / "lib" / "auth.js"
        shutil.copy2(site_auth, self.workspace / "lib" / "auth.js")
        site_cache = ROOT_DIR / "site" / "lib" / "cache.js"
        shutil.copy2(site_cache, self.workspace / "lib" / "cache.js")

        verdict = evaluate_attack(1, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, FAILED)
        self.assertIn("Anti-revert violation", verdict.reason)

    def test_reference_solution_passes_100_percent(self):
        """Reference code with atomic token parser passes all tiers and scores 1.0 (SOLVED)."""
        ref_auth = self.attack_dir / "reference" / "lib" / "auth.js"
        shutil.copy2(ref_auth, self.workspace / "lib" / "auth.js")
        ref_cache = self.attack_dir / "reference" / "lib" / "cache.js"
        shutil.copy2(ref_cache, self.workspace / "lib" / "cache.js")

        verdict = evaluate_attack(1, self.attack_dir, self.workspace, timeout_seconds=12.0)
        self.assertEqual(verdict.status, SOLVED)
        self.assertEqual(verdict.score_percentage, 1.0)
        self.assertTrue(verdict.tier_results["tier1_redos_blocked"])
        self.assertTrue(verdict.tier_results["tier2_tokens_functional"])
        self.assertTrue(verdict.tier_results["tier3_cache_capacity"])
        self.assertTrue(verdict.tier_results["tier4_latency_sla"])


if __name__ == "__main__":
    unittest.main()

