import unittest
from breach_engine import scoring as s


class ScoringTests(unittest.TestCase):
    def test_time_bonus_decays_linearly(self):
        self.assertEqual(s.time_bonus(0, 2700), 15)
        self.assertEqual(s.time_bonus(1350, 2700), 7.5)
        self.assertEqual(s.time_bonus(9999, 2700), 0)

    def test_prompt_penalty_only_beyond_threshold(self):
        self.assertEqual(s.prompt_penalty(8, 8), 0)
        self.assertEqual(s.prompt_penalty(3, 8), 0)
        self.assertEqual(s.prompt_penalty(18, 8), -2.0)

    def test_components(self):
        c = s.solve_components(0, 2700, 10, 8)
        self.assertEqual(c, {"base": 85.0, "time_bonus": 15.0, "prompt_penalty": -0.4})
        self.assertEqual(sum(c.values()), 99.6)

    def test_failed_submission_penalty_constant(self):
        self.assertEqual(s.FAILED_SUBMISSION_PENALTY, -3.0)


if __name__ == "__main__":
    unittest.main()
