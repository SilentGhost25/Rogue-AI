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


if __name__ == "__main__":
    unittest.main()
