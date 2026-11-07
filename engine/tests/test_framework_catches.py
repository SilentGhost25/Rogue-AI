"""Tests for the 4 Framework Catches:
- Catch 1: Language-aware static analysis dispatcher (JS/Node, Python, C#)
- Catch 2: Two-tier evaluation (Fast path vs Deep path, queue depth degradation)
- Catch 3: Statistical reference derivation, synthetic naive, bootstrap CI, Mann-Whitney U, composite score
- Catch 4: CLI telemetry ingestion and fallback estimation
"""
from __future__ import annotations
import tempfile
import unittest
from pathlib import Path

from breach_engine.static_analyzer import (
    analyze_file,
    analyze_directory,
    get_analyzer,
    JavaScriptAnalyzer,
    PythonAnalyzer,
    CSharpAnalyzer,
)
from breach_engine.reference_derivation import (
    derive_references_from_pilot,
    synthesize_naive,
    normalize_metric,
    bootstrap_ci,
    mann_whitney_u_test,
    compute_composite_score,
)
from breach_engine.evaluator import (
    check_compliance_fast,
    enqueue_deep_evaluation,
    get_deep_eval_result,
    DEEP_EVAL_QUEUE,
)


class TestFrameworkCatches(unittest.TestCase):
    def test_catch1_javascript_static_analyzer(self):
        """Catch 1: Verifies JS code is parsed without syntax errors, computing LOC, CC, MI, and functions."""
        js_code = """
        // Harbour Notes Note Validator
        const express = require('express');
        
        function validateNote(title, content) {
            if (!title || title.length === 0) {
                return false;
            } else if (title.length > 100) {
                return false;
            }
            if (content && content.length > 5000) {
                return false;
            }
            return true;
        }
        
        module.exports = { validateNote };
        """
        analyzer = JavaScriptAnalyzer()
        metrics = analyzer.analyze(js_code)

        self.assertGreater(metrics["loc"], 5.0)
        self.assertGreaterEqual(metrics["cyclomatic"], 3.0)
        self.assertGreater(metrics["maintainability"], 0.0)
        self.assertLessEqual(metrics["maintainability"], 100.0)
        self.assertGreater(metrics["comment_ratio"], 0.0)
        self.assertGreater(metrics["max_function_length"], 0.0)

    def test_catch1_analyzer_registry_dispatch(self):
        """Catch 1: Verifies analyzer dispatcher correctly maps extensions."""
        self.assertIsInstance(get_analyzer(Path("server.js")), JavaScriptAnalyzer)
        self.assertIsInstance(get_analyzer(Path("module.ts")), JavaScriptAnalyzer)
        self.assertIsInstance(get_analyzer(Path("app.py")), PythonAnalyzer)
        self.assertIsInstance(get_analyzer(Path("Program.cs")), CSharpAnalyzer)

    def test_catch3_reference_derivation_and_synthetic_naive(self):
        """Catch 3: Tests synthetic naive and pilot derivation without 32 hand-written references."""
        ideal = {"runtime": 10.0, "peak_memory": 30.0, "loc": 5.0, "cyclomatic": 1.0, "maintainability": 90.0, "comment_ratio": 0.2}
        naive = synthesize_naive(ideal, factor=3.0)

        self.assertEqual(naive["runtime"], 30.0)
        self.assertGreater(naive["loc"], ideal["loc"])
        self.assertLess(naive["maintainability"], ideal["maintainability"])

        pilot = [
            {"runtime": 15.0, "peak_memory": 32.0, "loc": 7.0, "cyclomatic": 2.0, "maintainability": 80.0, "comment_ratio": 0.1},
            {"runtime": 20.0, "peak_memory": 38.0, "loc": 10.0, "cyclomatic": 3.0, "maintainability": 75.0, "comment_ratio": 0.05},
        ]
        refs = derive_references_from_pilot(1, pilot, ideal, naive)
        self.assertIn("R_ideal", refs)
        self.assertIn("R_naive", refs)
        self.assertIn("R_baseline", refs)
        self.assertIn("R_worst", refs)
        self.assertGreaterEqual(refs["R_worst"]["runtime"], refs["R_baseline"]["runtime"])

    def test_catch3_bootstrap_ci_and_mann_whitney(self):
        """Catch 3: Verifies 95% bootstrap CI and Mann-Whitney U test."""
        runtimes = [12.0, 13.0, 11.5, 12.8, 14.0]
        lo, hi = bootstrap_ci(runtimes, trials=500)
        self.assertLessEqual(lo, hi)
        self.assertGreater(lo, 0.0)

        p = mann_whitney_u_test(runtimes, [25.0, 26.0, 27.0, 28.0])
        self.assertLess(p, 0.05)

    def test_catch3_composite_scoring_hard_gates(self):
        """Catch 3: Outcome and Compliance gates disqualify solutions when failed."""
        # Failed outcome gate -> 0.0
        res = compute_composite_score(
            outcome_passed=False,
            compliance_passed=True,
            time_score=0.9,
            quality_score=0.9,
            efficiency_score=0.9,
            robustness_score=0.9,
            process_score=0.9,
        )
        self.assertEqual(res["composite_score"], 0.0)
        self.assertFalse(res["certified"])

        # Passed gates -> 7 dimensions weighted composite
        res_pass = compute_composite_score(
            outcome_passed=True,
            compliance_passed=True,
            time_score=1.0,
            quality_score=1.0,
            efficiency_score=1.0,
            robustness_score=1.0,
            process_score=1.0,
        )
        self.assertEqual(res_pass["composite_score"], 100.0)
        self.assertTrue(res_pass["certified"])

    def test_catch2_compliance_fast_check_detects_payload(self):
        """Catch 2: Compliance fast-check flags hardcoded attack literals."""
        with tempfile.TemporaryDirectory() as td:
            p = Path(td)
            clean_file = p / "clean.js"
            clean_file.write_text("function sanitize(x) { return x.replace(/[^a-z]/g, ''); }", encoding="utf-8")
            ok, reason = check_compliance_fast(p)
            self.assertTrue(ok)

            dirty_file = p / "dirty.js"
            dirty_file.write_text("if (input === \"' OR 1=1 --\") return true;", encoding="utf-8")
            dirty_ok, dirty_reason = check_compliance_fast(p)
            self.assertFalse(dirty_ok)
            self.assertIn("Hardcoded attack payload literal", dirty_reason)

    def test_catch2_deep_eval_enqueue(self):
        """Catch 2: Enqueuing deep evaluation returns queue size without blocking."""
        with tempfile.TemporaryDirectory() as td:
            p = Path(td)
            qsize = enqueue_deep_evaluation("sub_test_1", "p1", [1], p)
            self.assertGreaterEqual(qsize, 1)


if __name__ == "__main__":
    unittest.main()

