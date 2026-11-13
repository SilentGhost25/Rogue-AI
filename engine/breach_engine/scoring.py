"""Pure scoring functions (plan section 8). No I/O, so results are deterministic."""
from __future__ import annotations
from typing import Any

PENALTY_PER_FAILED_SUBMISSION = 3
PENALTY_CAP = 15


def clamp(x: float, lo: float = 0.0, hi: float = 1.0) -> float:
    return max(lo, min(hi, x))


def speed_points(weight: int, seconds_to_solve: float, window_seconds: float) -> float:
    """Linear decay from landing to the end of the attack window."""
    if window_seconds <= 0:
        return 0.0
    return round(weight * clamp(1 - seconds_to_solve / window_seconds), 2)


def performance_points(weight: int, value: float, baseline: float, reference: float, lower_is_better: bool = True) -> float:
    """Normalise between the broken baseline (0) and the reference solution (1)."""
    if baseline == reference:
        return float(weight)
    frac = (baseline - value) / (baseline - reference) if lower_is_better else (value - baseline) / (reference - baseline)
    return round(weight * clamp(frac), 2)


def prompt_efficiency_points(weight: int, prompts_used: int, reference_prompts: int) -> float:
    """Full marks at or below reference, zero at 3x reference."""
    if reference_prompts <= 0:
        return float(weight)
    if prompts_used <= reference_prompts:
        return float(weight)
    frac = 1 - (prompts_used - reference_prompts) / (2 * reference_prompts)
    return round(weight * clamp(frac), 2)


def failed_submission_penalty(failed_count: int) -> int:
    return -min(PENALTY_CAP, PENALTY_PER_FAILED_SUBMISSION * max(0, failed_count))


def survival_bonus(total_seconds: float, seconds_with_active_attack: float, max_points: int = 100) -> float:
    if total_seconds <= 0:
        return 0.0
    return round(max_points * clamp(1 - seconds_with_active_attack / total_seconds), 2)


def golden_zone_speed_points(weight: float, seconds_to_solve: float, window_seconds: float = 1800.0) -> float:
    """Golden Engineering Window speed curve (15-35 minutes optimal).

    - < 15 min (< 900s): Capped at 25% (discourages hasty shortcuts/scripts)
    - 15-35 min (900s - 2100s): 100% full speed bonus
    - 35-60 min (2100s - 3600s): Smooth decay down to 20%
    - > 60 min (> 3600s): 0 bonus
    """
    if seconds_to_solve < 0:
        return 0.0
    if seconds_to_solve < 900:
        return round(weight * 0.25, 2)
    elif seconds_to_solve <= 2100:
        return round(float(weight), 2)
    elif seconds_to_solve <= 3600:
        ratio = (seconds_to_solve - 2100) / 1500.0
        return round(weight * (1.0 - 0.80 * ratio), 2)
    return 0.0


def progressive_prompt_efficiency_points(weight: float, prompts_used: int, min_optimal: int = 6, max_optimal: int = 10) -> float:
    """Calibrated prompt efficiency for multi-turn architectural investigations.

    - 1 prompt: 0 points (flags one-shot attempt)
    - 2-5 prompts: ramp up from 20% to 80%
    - 6-10 prompts: 100% full marks
    - > 10 prompts: gradual linear decay down to 0 at 25 prompts
    """
    if prompts_used <= 1:
        return 0.0
    if min_optimal <= prompts_used <= max_optimal:
        return round(float(weight), 2)
    if prompts_used < min_optimal:
        frac = (prompts_used - 1) / (min_optimal - 1)
        return round(weight * (0.20 + 0.80 * frac), 2)
    if prompts_used <= 25:
        frac = 1.0 - (prompts_used - max_optimal) / (25.0 - max_optimal)
        return round(weight * clamp(frac), 2)
    return 0.0


def progressive_tier_points(tiers_spec: list[dict[str, Any]], tier_results: dict[str, bool], base_points: float = 8500.0) -> tuple[float, float, list[str], list[str]]:
    """Calculates additive partial points and percentage based on passed tiers.

    Returns: (earned_points, score_percentage, passed_ids, failed_ids)
    """
    if not tiers_spec or not tier_results:
        return 0.0, 0.0, [], []

    total_weight = sum(float(t.get("weight", 0)) for t in tiers_spec) or 100.0
    passed_ids = []
    failed_ids = []
    earned_weight = 0.0

    for t in tiers_spec:
        tid = t.get("id", "")
        w = float(t.get("weight", 0))
        if tier_results.get(tid, False):
            passed_ids.append(tid)
            earned_weight += w
        else:
            failed_ids.append(tid)

    score_pct = round(earned_weight / total_weight, 4)
    earned_pts = round(base_points * score_pct, 2)
    return earned_pts, score_pct, passed_ids, failed_ids


def gentle_submission_friction(failed_count: int) -> int:
    """Gentle friction to encourage testing before submission, with a strict floor guarantee."""
    if failed_count <= 1:
        return 0
    if failed_count == 2:
        return -5
    if failed_count == 3:
        return -10
    return -15


def calculate_benchmark_multiplier(metrics: dict[str, float], sla: dict[str, Any] | None = None) -> float:
    """Calculates a benchmark performance multiplier between 0.60 (naive fix) and 1.00 (optimal fix).

    sla spec example:
    {
        "target_p95_ms": 5.0,
        "max_p95_ms": 150.0,
        "target_ram_mb": 15.0,
        "max_ram_mb": 60.0,
        "floor": 0.60
    }
    """
    if not sla or not metrics:
        return 1.0

    floor = sla.get("floor", 0.60)
    scores = []

    # 1. Latency benchmark (p95)
    if "target_p95_ms" in sla and "latency_p95_ms" in metrics:
        target = float(sla["target_p95_ms"])
        ceiling = float(sla.get("max_p95_ms", target * 10))
        val = float(metrics["latency_p95_ms"])
        if val <= target:
            scores.append(1.0)
        elif val >= ceiling:
            scores.append(floor)
        else:
            ratio = (val - target) / (ceiling - target)
            scores.append(round(1.0 - ratio * (1.0 - floor), 4))

    # 2. RAM growth benchmark
    if "target_ram_mb" in sla and "peak_memory_mb" in metrics:
        target = float(sla["target_ram_mb"])
        ceiling = float(sla.get("max_ram_mb", target * 4))
        val = float(metrics["peak_memory_mb"])
        if val <= target:
            scores.append(1.0)
        elif val >= ceiling:
            scores.append(floor)
        else:
            ratio = (val - target) / (ceiling - target)
            scores.append(round(1.0 - ratio * (1.0 - floor), 4))

    # 3. CPU time benchmark
    if "target_cpu_ms" in sla and "cpu_time_ms" in metrics:
        target = float(sla["target_cpu_ms"])
        ceiling = float(sla.get("max_cpu_ms", target * 5))
        val = float(metrics["cpu_time_ms"])
        if val <= target:
            scores.append(1.0)
        elif val >= ceiling:
            scores.append(floor)
        else:
            ratio = (val - target) / (ceiling - target)
            scores.append(round(1.0 - ratio * (1.0 - floor), 4))

    if not scores:
        return 1.0

    return round(sum(scores) / len(scores), 4)

