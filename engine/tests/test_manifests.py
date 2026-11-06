import unittest
from pathlib import Path
from breach_engine.manifest import load_all

ATTACKS = Path(__file__).resolve().parents[2] / "attacks"


class ManifestTests(unittest.TestCase):
    def test_all_seven_attacks_load_and_have_packs(self):
        attacks = load_all(ATTACKS)
        self.assertEqual([a.id for a in attacks], list(range(1, 11)))
        for a in attacks:
            self.assertTrue((a.directory / "attack.patch").exists())
            for rel in a.touches:
                self.assertTrue((a.directory / "corrupted" / rel).exists(), f"{a.id}: corrupted/{rel}")

    def test_plan_target_files(self):
        t = {a.id: set(a.touches) for a in load_all(ATTACKS)}
        self.assertEqual(t[1], {"public/style.css", "lib/html.js"})
        self.assertEqual(t[2], {"routes/posts.js", "routes/search.js"})
        self.assertEqual(t[3], {"routes/search.js"})
        self.assertEqual(t[4], {"routes/api.js"})
        self.assertEqual(t[5], {"app.js"})
        self.assertEqual(t[6], {"routes/api.js"})
        self.assertEqual(t[7], {"server.js", "package.json"})
        self.assertEqual(t[8], {"lib/auth.js"})
        self.assertEqual(t[9], {"routes/comments.js"})
        self.assertEqual(t[10], {"lib/web.js"})


if __name__ == "__main__":
    unittest.main()
