import shutil
import tempfile
import unittest
from pathlib import Path
from breach_engine.evaluator import evaluate_attack, SOLVED, FAILED

ROOT_DIR = Path(__file__).resolve().parents[2]


class Attack00Tests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = Path(tempfile.mkdtemp(prefix="test_attack00_"))
        self.workspace = self.temp_dir / "workspace"
        self.workspace.mkdir()

        site_src = ROOT_DIR / "site"
        for item in ["app.js", "package.json", "server.js"]:
            if (site_src / item).exists():
                shutil.copy2(site_src / item, self.workspace / item)
        for folder in ["lib", "routes", "public"]:
            if (site_src / folder).exists():
                shutil.copytree(site_src / folder, self.workspace / folder, dirs_exist_ok=True)

        self.attack_dir = ROOT_DIR / "attacks" / "00-warmup"

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_corrupted_payload_fails(self):
        for item in ["lib/html.js"]:
            src = self.attack_dir / "corrupted" / item
            dst = self.workspace / item
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)

        verdict = evaluate_attack(0, self.attack_dir, self.workspace, timeout_seconds=10.0)
        self.assertNotEqual(verdict.status, SOLVED)
        if verdict.tier_results:
            self.assertFalse(verdict.tier_results.get("tier1_title", False))

    def test_reverted_to_clean_site_fails_anti_revert(self):
        # When participant only reverts to pristine site/lib/html.js without the CSP improvement
        clean_src = ROOT_DIR / "site" / "lib" / "html.js"
        dst = self.workspace / "lib" / "html.js"
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(clean_src, dst)

        verdict = evaluate_attack(0, self.attack_dir, self.workspace, timeout_seconds=10.0)
        self.assertEqual(verdict.status, FAILED)
        self.assertIn("Anti-revert violation", verdict.reason)

    def test_reference_solution_passes_100_percent(self):
        for item in ["lib/html.js"]:
            src = self.attack_dir / "reference" / item
            dst = self.workspace / item
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)

        verdict = evaluate_attack(0, self.attack_dir, self.workspace, timeout_seconds=10.0)
        self.assertEqual(verdict.status, SOLVED)
        self.assertEqual(verdict.score_percentage, 1.0)
        self.assertTrue(verdict.tier_results["tier1_title"])
        self.assertTrue(verdict.tier_results["tier2_security_meta"])

