import tempfile
import unittest
import shutil
from pathlib import Path
from breach_engine.evaluator import evaluate_attack, SOLVED, PARTIAL, FAILED

ROOT_DIR = Path(__file__).resolve().parents[2]


class TestEvaluator(unittest.TestCase):

    def setUp(self):
        self.temp_dir = Path(tempfile.mkdtemp(prefix="test_eval_"))
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

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_standard_fallback_branch(self):
        """Standard check.py without tier_checks must use regression/replay/variant fallback."""
        attack_dir = self.temp_dir / "attack_standard"
        (attack_dir / "checks").mkdir(parents=True)
        
        check_code = """
def regression_check(base_url, context=None):
    return True
def replay_check(base_url, context=None):
    return False # False means not vulnerable (fixed)
def variant_check(base_url, context=None):
    return True
"""
        (attack_dir / "checks" / "check.py").write_text(check_code, encoding="utf-8")
        verdict = evaluate_attack(901, attack_dir, self.workspace, timeout_seconds=10.0)
        self.assertEqual(verdict.status, SOLVED)
        self.assertIn("neutralized", verdict.reason)
        self.assertIsNone(verdict.tier_results)

    def test_tier_checks_weighted_full_pass(self):
        """When tier_checks is present and manifest specifies tiers, calculates weighted score."""
        attack_dir = self.temp_dir / "attack_tiers"
        (attack_dir / "checks").mkdir(parents=True)
        
        manifest_yaml = """
id: 902
title: "Tier Test"
tests:
  tiers:
    - id: tier1_escaping
      weight: 60
    - id: tier2_functional
      weight: 40
"""
        (attack_dir / "manifest.yaml").write_text(manifest_yaml, encoding="utf-8")
        
        check_code = """
def tier_checks(base_url, context=None):
    return {
        "tier1_escaping": True,
        "tier2_functional": True
    }
"""
        (attack_dir / "checks" / "check.py").write_text(check_code, encoding="utf-8")
        verdict = evaluate_attack(902, attack_dir, self.workspace, timeout_seconds=10.0)
        self.assertEqual(verdict.status, SOLVED)
        self.assertEqual(verdict.score_percentage, 1.0)
        self.assertEqual(verdict.tier_results["tier1_escaping"], True)
        self.assertIn("100/100%", verdict.reason)

    def test_tier_checks_weighted_partial_pass(self):
        """When only some tiers pass, calculates exact fractional score and marks PARTIAL."""
        attack_dir = self.temp_dir / "attack_tiers_partial"
        (attack_dir / "checks").mkdir(parents=True)
        
        manifest_yaml = """
id: 903
title: "Tier Partial Test"
tests:
  tiers:
    - id: tier1_escaping
      weight: 70
    - id: tier2_functional
      weight: 30
"""
        (attack_dir / "manifest.yaml").write_text(manifest_yaml, encoding="utf-8")
        
        check_code = """
def tier_checks(base_url, context=None):
    return {
        "tier1_escaping": True,
        "tier2_functional": False
    }
"""
        (attack_dir / "checks" / "check.py").write_text(check_code, encoding="utf-8")
        verdict = evaluate_attack(903, attack_dir, self.workspace, timeout_seconds=10.0)
        self.assertEqual(verdict.status, PARTIAL)
        self.assertEqual(verdict.score_percentage, 0.70)
        self.assertEqual(verdict.tier_results["tier1_escaping"], True)
        self.assertEqual(verdict.tier_results["tier2_functional"], False)
        self.assertIn("70/100%", verdict.reason)

    def test_10_tier_full_pass(self):
        """10 distinct tiers passing 100% yields OPTIMAL verdict with 10 property booleans."""
        attack_dir = self.temp_dir / "attack_10_tiers_full"
        (attack_dir / "checks").mkdir(parents=True)
        
        tiers_yaml = "\n".join([f"    - id: t{i}_prop\n      weight: 10" for i in range(1, 11)])
        manifest_yaml = f"id: 910\ntitle: '10-Tier Test'\ntests:\n  tiers:\n{tiers_yaml}\n"
        (attack_dir / "manifest.yaml").write_text(manifest_yaml, encoding="utf-8")
        
        tier_dict = {f"t{i}_prop": True for i in range(1, 11)}
        check_code = f"def tier_checks(base_url, context=None):\n    return {repr(tier_dict)}\n"
        (attack_dir / "checks" / "check.py").write_text(check_code, encoding="utf-8")
        
        verdict = evaluate_attack(910, attack_dir, self.workspace, timeout_seconds=10.0)
        self.assertEqual(verdict.status, "OPTIMAL")
        self.assertEqual(verdict.score_percentage, 1.0)
        self.assertEqual(len(verdict.tier_results), 10)
        self.assertTrue(all(verdict.tier_results.values()))
        self.assertIn("100/100%", verdict.reason)

    def test_10_tier_progressive_partial_pass(self):
        """Passing 4 out of 10 tiers yields PROGRESSIVE status with 40% score percentage."""
        attack_dir = self.temp_dir / "attack_10_tiers_partial"
        (attack_dir / "checks").mkdir(parents=True)
        
        tiers_yaml = "\n".join([f"    - id: t{i}_prop\n      weight: 10" for i in range(1, 11)])
        manifest_yaml = f"id: 911\ntitle: '10-Tier Progressive Test'\ntests:\n  tiers:\n{tiers_yaml}\n"
        (attack_dir / "manifest.yaml").write_text(manifest_yaml, encoding="utf-8")
        
        # Tiers 1-4 True, 5-10 False
        tier_dict = {f"t{i}_prop": (i <= 4) for i in range(1, 11)}
        check_code = f"def tier_checks(base_url, context=None):\n    return {repr(tier_dict)}\n"
        (attack_dir / "checks" / "check.py").write_text(check_code, encoding="utf-8")
        
        verdict = evaluate_attack(911, attack_dir, self.workspace, timeout_seconds=10.0)
        self.assertEqual(verdict.status, "PROGRESSIVE")
        self.assertEqual(verdict.score_percentage, 0.40)
        self.assertEqual(len(verdict.tier_results), 10)
        self.assertEqual(sum(1 for v in verdict.tier_results.values() if v), 4)
        self.assertIn("40/100%", verdict.reason)
        self.assertIn("4/10 passed", verdict.reason)


if __name__ == "__main__":
    unittest.main()
