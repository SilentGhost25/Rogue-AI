import unittest
from breach_engine import scoring as s


class ScoringTests(unittest.TestCase):
    def test_speed_decays_linearly(self):
        self.assertEqual(s.speed_points(30, 0, 1800), 30)
        self.assertEqual(s.speed_points(30, 900, 1800), 15)
        self.assertEqual(s.speed_points(30, 5000, 1800), 0)

    def test_prompt_efficiency(self):
        self.assertEqual(s.prompt_efficiency_points(20, 4, 5), 20)
        self.assertEqual(s.prompt_efficiency_points(20, 15, 5), 0)
        self.assertEqual(s.prompt_efficiency_points(20, 10, 5), 10)

    def test_performance_normalised(self):
        self.assertEqual(s.performance_points(25, 800, 800, 100), 0)
        self.assertEqual(s.performance_points(25, 100, 800, 100), 25)
        self.assertEqual(s.performance_points(25, 450, 800, 100), 12.5)

    def test_penalty_cap(self):
        self.assertEqual(s.failed_submission_penalty(2), -6)
        self.assertEqual(s.failed_submission_penalty(99), -15)

    def test_survival(self):
        self.assertEqual(s.survival_bonus(1000, 250), 75)

    def test_golden_zone_speed_points(self):
        # < 15 min: capped at 25%
        self.assertEqual(s.golden_zone_speed_points(100, 300), 25.0)
        # 15-35 min: full marks
        self.assertEqual(s.golden_zone_speed_points(100, 1200), 100.0)
        # 35-60 min: decays from 100% down to 20%
        decayed = s.golden_zone_speed_points(100, 2850)
        self.assertTrue(20.0 <= decayed <= 100.0)
        # > 60 min: 0
        self.assertEqual(s.golden_zone_speed_points(100, 4000), 0.0)

    def test_progressive_prompt_efficiency_points(self):
        # 1 prompt: flags one-shot attempt -> 0 pts
        self.assertEqual(s.progressive_prompt_efficiency_points(50, 1), 0.0)
        # 4 prompts: ramp up
        self.assertTrue(0.0 < s.progressive_prompt_efficiency_points(50, 4) < 50.0)
        # 8 prompts: optimal multi-turn analysis -> full marks
        self.assertEqual(s.progressive_prompt_efficiency_points(50, 8), 50.0)
        # > 25 prompts: 0
        self.assertEqual(s.progressive_prompt_efficiency_points(50, 26), 0.0)

    def test_progressive_tier_points(self):
        tiers = [
            {"id": "t1", "weight": 20},
            {"id": "t2", "weight": 30},
            {"id": "t3", "weight": 50},
        ]
        results = {"t1": True, "t2": True, "t3": False}
        pts, pct, passed, failed = s.progressive_tier_points(tiers, results, base_points=1000.0)
        self.assertEqual(pct, 0.5)
        self.assertEqual(pts, 500.0)
        self.assertEqual(passed, ["t1", "t2"])
        self.assertEqual(failed, ["t3"])

    def test_gentle_submission_friction(self):
        self.assertEqual(s.gentle_submission_friction(0), 0)
        self.assertEqual(s.gentle_submission_friction(1), 0)
        self.assertEqual(s.gentle_submission_friction(2), -5)
        self.assertEqual(s.gentle_submission_friction(3), -10)
        self.assertEqual(s.gentle_submission_friction(4), -15)
        self.assertEqual(s.gentle_submission_friction(10), -15)


if __name__ == "__main__":
    unittest.main()
