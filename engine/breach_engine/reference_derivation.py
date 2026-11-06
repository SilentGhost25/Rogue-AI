"""Reference solution derivation and benchmarking algorithm.
Implements:
- 2 hand-authored references (R_ideal, R_naive) with statistical derivation of R_baseline and R_worst
- Synthetic naive fallback if manual naive is absent
- Metric normalization to [0, 1]
- Mann-Whitney U test vs baseline
- Bootstrap 95% Confidence Interval (1000 resamples)
- Composite 7-dimension scoring with Hard Gates (Outcome, Compliance)
"""
from __future__ import annotations
import math
from typing import Any
import numpy as np

try:
    from scipy import stats
except ImportError:
    stats = None


DEFAULT_IDEAL_METRICS = {
    "runtime": 12.0,      # ms
    "peak_memory": 35.0,  # MB
    "loc": 8.0,           # lines changed
    "cyclomatic": 1.0,    # added complexity
    "maintainability": 95.0,
    "comment_ratio": 0.15,
}


def synthesize_naive(ideal_metrics: dict[str, float], factor: float = 2.5) -> dict[str, float]:
    """Generates an automated naive reference based on ideal metrics with deliberate slowdown/bloat."""
    return {
        "runtime": round(ideal_metrics.get("runtime", 12.0) * factor, 2),
        "peak_memory": round(ideal_metrics.get("peak_memory", 35.0) * 1.4, 2),
        "loc": round(ideal_metrics.get("loc", 8.0) * 1.8, 1),
        "cyclomatic": round(ideal_metrics.get("cyclomatic", 1.0) * 2.0, 1),
        "maintainability": max(30.0, round(ideal_metrics.get("maintainability", 95.0) * 0.65, 2)),
        "comment_ratio": round(ideal_metrics.get("comment_ratio", 0.15) * 0.5, 3),
    }


def derive_references_from_pilot(
    attack_id: int,
    pilot_metrics: list[dict[str, float]],
    ideal_ref: dict[str, float] | None = None,
    naive_ref: dict[str, float] | None = None,
) -> dict[str, dict[str, float]]:
    """Derives R_baseline and R_worst from dry-run / pilot participants, retaining hand-authored R_ideal & R_naive."""
    r_ideal = ideal_ref or DEFAULT_IDEAL_METRICS.copy()
    r_naive = naive_ref or synthesize_naive(r_ideal)

    if not pilot_metrics:
        # Fallback calibration when pilot data is unavailable
        r_baseline = {
            "runtime": round((r_ideal["runtime"] + r_naive["runtime"]) / 2.0, 2),
            "peak_memory": round((r_ideal["peak_memory"] + r_naive["peak_memory"]) / 2.0, 2),
            "loc": round((r_ideal["loc"] + r_naive["loc"]) / 2.0, 1),
            "cyclomatic": round((r_ideal["cyclomatic"] + r_naive["cyclomatic"]) / 2.0, 1),
            "maintainability": round((r_ideal["maintainability"] + r_naive["maintainability"]) / 2.0, 2),
            "comment_ratio": round((r_ideal["comment_ratio"] + r_naive["comment_ratio"]) / 2.0, 3),
        }
        r_worst = {
            "runtime": round(r_naive["runtime"] * 2.0, 2),
            "peak_memory": round(r_naive["peak_memory"] * 1.8, 2),
            "loc": round(r_naive["loc"] * 2.5, 1),
            "cyclomatic": round(r_naive["cyclomatic"] * 2.5, 1),
            "maintainability": 25.0,
            "comment_ratio": 0.0,
        }
    else:
        def get_stat(key: str, p: float, default: float) -> float:
            vals = [float(m[key]) for m in pilot_metrics if key in m]
            return float(np.percentile(vals, p)) if vals else default

        r_baseline = {
            "runtime": round(get_stat("runtime", 50, r_naive["runtime"]), 2),
            "peak_memory": round(get_stat("peak_memory", 50, r_naive["peak_memory"]), 2),
            "loc": round(get_stat("loc", 50, r_naive["loc"]), 1),
            "cyclomatic": round(get_stat("cyclomatic", 50, r_naive["cyclomatic"]), 1),
            "maintainability": round(get_stat("maintainability", 50, r_naive["maintainability"]), 2),
            "comment_ratio": round(get_stat("comment_ratio", 50, r_naive["comment_ratio"]), 3),
        }

        r_worst = {
            "runtime": round(get_stat("runtime", 95, r_naive["runtime"] * 2.0), 2),
            "peak_memory": round(get_stat("peak_memory", 95, r_naive["peak_memory"] * 1.8), 2),
            "loc": round(get_stat("loc", 95, r_naive["loc"] * 2.0), 1),
            "cyclomatic": round(get_stat("cyclomatic", 95, r_naive["cyclomatic"] * 2.0), 1),
            "maintainability": round(get_stat("maintainability", 5, 20.0), 2),
            "comment_ratio": round(get_stat("comment_ratio", 5, 0.0), 3),
        }

    return {
        "R_ideal": r_ideal,
        "R_naive": r_naive,
        "R_baseline": r_baseline,
        "R_worst": r_worst,
    }


def normalize_metric(val: float, r_ideal: float, r_worst: float, lower_is_better: bool = True) -> float:
    """Clamps and normalizes a metric into [0.0, 1.0] relative to ideal (1.0) and worst (0.0)."""
    if math.isclose(r_ideal, r_worst):
        return 1.0
    if lower_is_better:
        score = (r_worst - val) / (r_worst - r_ideal)
    else:
        score = (val - r_worst) / (r_ideal - r_worst)
    return float(np.clip(score, 0.0, 1.0))


def bootstrap_ci(data: list[float], trials: int = 1000, alpha: float = 0.05) -> tuple[float, float]:
    """Computes a (1 - alpha)% bootstrap confidence interval for the mean."""
    if not data:
        return 0.0, 0.0
    if len(data) == 1:
        return float(data[0]), float(data[0])

    arr = np.array(data, dtype=float)
    rng = np.random.default_rng(seed=42)
    boot_indices = rng.choice(len(arr), size=(trials, len(arr)), replace=True)
    boot_means = np.mean(arr[boot_indices], axis=1)
    lo = float(np.percentile(boot_means, 100 * (alpha / 2.0)))
    hi = float(np.percentile(boot_means, 100 * (1 - alpha / 2.0)))
    return round(lo, 3), round(hi, 3)


def mann_whitney_u_test(sample_a: list[float], sample_b: list[float]) -> float:
    """Returns p-value comparing sample_a and sample_b via Mann-Whitney U test."""
    if not stats or len(sample_a) < 3 or len(sample_b) < 3:
        return 1.0
    try:
        _, p = stats.mannwhitneyu(sample_a, sample_b, alternative="two-sided")
        return round(float(p), 4)
    except Exception:
        return 1.0


def compute_composite_score(
    outcome_passed: bool,
    compliance_passed: bool,
    time_score: float,       # 0.0 to 1.0
    quality_score: float,    # 0.0 to 1.0
    efficiency_score: float, # 0.0 to 1.0
    robustness_score: float, # 0.0 to 1.0
    process_score: float,    # 0.0 to 1.0
) -> dict[str, Any]:
    """Computes the 7-dimension evaluation framework score with hard gates.
    Formula:
      Composite = 0.30·Time + 0.25·Quality + 0.20·Efficiency + 0.15·Robustness + 0.10·Process
    """
    gated = outcome_passed and compliance_passed
    if not gated:
        composite = 0.0
    else:
        composite = (
            0.30 * time_score
            + 0.25 * quality_score
            + 0.20 * efficiency_score
            + 0.15 * robustness_score
            + 0.10 * process_score
        )
        composite = round(max(0.0, min(1.0, composite)) * 100.0, 2)

    return {
        "gates": {
            "outcome_passed": outcome_passed,
            "compliance_passed": compliance_passed,
        },
        "dimensions": {
            "time": round(time_score, 4),
            "quality": round(quality_score, 4),
            "efficiency": round(efficiency_score, 4),
            "robustness": round(robustness_score, 4),
            "process": round(process_score, 4),
        },
        "composite_score": composite,
        "certified": gated,
    }
