import unittest
from breach_engine import scoring as s


class BenchmarkScoringTests(unittest.TestCase):
    def test_multiplier_optimal_solution(self):
        """Optimal performance within target SLA returns 1.0."""
        sla = {
            "target_p95_ms": 5.0,
            "max_p95_ms": 150.0,
            "target_ram_mb": 15.0,
            "max_ram_mb": 60.0,
            "floor": 0.60
        }
        metrics = {
            "latency_p95_ms": 3.2,
            "peak_memory_mb": 12.0
        }
        mult = s.calculate_benchmark_multiplier(metrics, sla)
        self.assertEqual(mult, 1.0)

    def test_multiplier_naive_solution(self):
        """Slow or unoptimized solution decays towards floor."""
        sla = {
            "target_p95_ms": 5.0,
            "max_p95_ms": 150.0,
            "target_ram_mb": 15.0,
            "max_ram_mb": 60.0,
            "floor": 0.60
        }
        # Mid-point latency (77.5ms) and target RAM
        metrics = {
            "latency_p95_ms": 77.5,
            "peak_memory_mb": 14.0
        }
        mult = s.calculate_benchmark_multiplier(metrics, sla)
        self.assertGreater(mult, 0.75)
        self.assertLess(mult, 0.95)

    def test_multiplier_catastrophic_solution(self):
        """Performance exceeding ceiling receives floor multiplier."""
        sla = {
            "target_p95_ms": 5.0,
            "max_p95_ms": 150.0,
            "floor": 0.60
        }
        metrics = {
            "latency_p95_ms": 250.0
        }
        mult = s.calculate_benchmark_multiplier(metrics, sla)
        self.assertEqual(mult, 0.60)

    def test_multiplier_empty_metrics(self):
        """Missing SLA or metrics defaults safely to 1.0."""
        self.assertEqual(s.calculate_benchmark_multiplier({}, {}), 1.0)
        self.assertEqual(s.calculate_benchmark_multiplier({"latency_p95_ms": 10.0}, None), 1.0)


if __name__ == "__main__":
    unittest.main()

