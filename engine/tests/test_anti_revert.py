import tempfile
import unittest
import shutil
from pathlib import Path
from breach_engine.evaluator import check_anti_revert, evaluate_attack, FAILED

ROOT_DIR = Path(__file__).resolve().parents[2]


class TestAntiRevert(unittest.TestCase):

    def setUp(self):
        self.temp_dir = Path(tempfile.mkdtemp(prefix="test_antirevert_"))
        self.workspace = self.temp_dir / "workspace"
        self.workspace.mkdir()
        self.template_dir = self.temp_dir / "site_template"
        self.template_dir.mkdir()
        (self.template_dir / "lib").mkdir()
        (self.template_dir / "lib" / "auth.js").write_text("console.log('clean auth');", encoding="utf-8")

    def tearDown(self):
        try:
            shutil.rmtree(self.temp_dir, ignore_errors=True)
        except Exception:
            pass

    def test_anti_revert_disabled_passes(self):
        manifest_data = {
            "anti_revert": False,
            "payload": {"conflict_files": ["lib/auth.js"]}
        }
        (self.workspace / "lib").mkdir(parents=True)
        (self.workspace / "lib" / "auth.js").write_text("console.log('clean auth');", encoding="utf-8")
        ok, msg = check_anti_revert(self.workspace, manifest_data, self.template_dir)
        self.assertTrue(ok)

    def test_anti_revert_detects_copied_template(self):
        manifest_data = {
            "anti_revert": True,
            "payload": {"conflict_files": ["lib/auth.js"]}
        }
        (self.workspace / "lib").mkdir(parents=True)
        # Copy exact pristine template
        (self.workspace / "lib" / "auth.js").write_text("console.log('clean auth');", encoding="utf-8")
        ok, msg = check_anti_revert(self.workspace, manifest_data, self.template_dir)
        self.assertFalse(ok)
        self.assertIn("Anti-revert violation", msg)
        self.assertIn("lib/auth.js", msg)

    def test_anti_revert_passes_when_improved(self):
        manifest_data = {
            "anti_revert": True,
            "payload": {"conflict_files": ["lib/auth.js"]}
        }
        (self.workspace / "lib").mkdir(parents=True)
        # Modified improved code
        (self.workspace / "lib" / "auth.js").write_text("console.log('hardened and improved auth');", encoding="utf-8")
        ok, msg = check_anti_revert(self.workspace, manifest_data, self.template_dir)
        self.assertTrue(ok)
        self.assertIn("passed", msg)
