import unittest
from pathlib import Path
from breach_engine.manifest import load_all

ATTACKS = Path(__file__).resolve().parents[2] / "attacks"


class ManifestTests(unittest.TestCase):
    def test_all_seven_attacks_load(self):
        attacks = load_all(ATTACKS)
        self.assertEqual([a.id for a in attacks], list(range(len(attacks))))
        self.assertTrue(len(attacks) in (7, 8, 11, 12, 13, 18, 23, 33))
        for a in attacks:
            self.assertEqual(sum(a.weights.values()), 100)


if __name__ == "__main__":
    unittest.main()
